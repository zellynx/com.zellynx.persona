using System;
using System.Collections.Generic;
using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Physical movement authority for a kinematic character driven explicitly through Move calls.
    /// Supports managed capsule, sphere, and box collider variants.
    ///
    /// CharacterBody does not process player input: controllers, AI, or any other gameplay
    /// system submit requested movement through VelocityUpdate / PositionUpdate /
    /// OrientationUpdate commands, and the body resolves those requests against its physical
    /// simulation (grounding, gravity, collision, slopes, steps, ledges, platforms,
    /// depenetration, rigidbody interaction). The character's semantic frame is exposed
    /// through <see cref="Basis"/>.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public class CharacterBody : MonoBehaviour
    {
        internal sealed class SolverContext
        {
            public readonly Collider[] OverlapResults;
            public readonly RaycastHit[] SweepResults;
            public readonly List<CharacterRigidbodyHit> RigidbodyHits;
            public readonly HashSet<Rigidbody> PushedRigidbodies;

            public SolverContext(int overlapCapacity = 16, int sweepCapacity = 16) {
                OverlapResults = new Collider[overlapCapacity];
                SweepResults = new RaycastHit[sweepCapacity];
                RigidbodyHits = new List<CharacterRigidbodyHit>(16);
                PushedRigidbodies = new HashSet<Rigidbody>();
            }
        }

        [SerializeField] private GeometryTypes _geometryType = GeometryTypes.Capsule;

        [SerializeField] private CapsuleGeometrySettings _capsuleGeometrySettings = CapsuleGeometrySettings.Default;
        [SerializeField] private SphereGeometrySettings _sphereGeometrySettings = SphereGeometrySettings.Default;
        [SerializeField] private BoxGeometrySettings _boxGeometrySettings = BoxGeometrySettings.Default;

        [SerializeField] private CharacterBodySettings _settings = new();

        private CharacterBodyBasis _basis;
        private bool _basisInitialized;

        private Rigidbody _rigidbody;
        private Collider _collider;
        private CapsuleCollider _capsuleCollider;
        private SphereCollider _sphereCollider;
        private BoxCollider _boxCollider;

        private Velocity _velocity;
        private Velocity _attachedAnchorVelocity;

        private CharacterGroundingReport _groundingReport;
        private bool _isGrounded;
        private bool _wasGrounded;
        private float _forceUngroundTimeRemaining;

        private CharacterBodyAnchor _currentPlatformAnchor;

        private Pose _simulationPose;
        private Pose _previousSimulationPose;

        private float _lastInterpolationStartTime = -1f;
        private float _lastInterpolationDeltaTime = -1f;
        private bool _simulationPoseInitialized;

        /// <summary>
        /// Invoked at the beginning of Move processing with the simulation delta time.
        /// </summary>
        public event Action<CharacterBody, float> BeforeSimulate;

        /// <summary>
        /// Invoked after the final pose and velocity have been committed for the step.
        /// </summary>
        public event Action<CharacterBody, float> AfterSimulate;

        /// <summary>
        /// Invoked when the body detects a movement or grounding collision.
        /// </summary>
        public event Action<CharacterBody, CharacterCollisionEventArgs> Collided;

        /// <summary>
        /// Invoked when the grounded state changes after a Move call.
        /// </summary>
        public event Action<CharacterBody>
            GroundedStateChanged; // Grounded is not a state here, but a flag that gets changed

        /// <summary>
        /// Optional filter for excluding colliders from movement and grounding queries.
        /// </summary>
        public Func<Collider, bool> ColliderFilter;

        /// <summary>
        /// Optional mutator for advanced stability evaluation adjustments. The returned
        /// report replaces the computed one.
        /// </summary>
        public Func<HitStabilityReport, HitStabilityReport> ModifyHitStability;

        /// <summary>
        /// Optional mutator for rigidbody interaction hits before they are processed.
        /// </summary>
        public Func<CharacterRigidbodyHit, CharacterRigidbodyHit> ModifyRigidbodyHit;

        /// <summary>
        /// Serialized movement and collision settings used by the body.
        /// </summary>
        public CharacterBodySettings Settings => _settings;

        internal SolverContext Context { get; private set; }

        internal Rigidbody ActiveRigidbody => _rigidbody;
        internal Collider ActiveCollider => _collider;

        /// <summary>
        /// Selected CharacterBody GeometryType.
        /// </summary>
        public GeometryTypes GeometryType => _geometryType;

        /// <summary>
        /// The managed rigidbody currently used by the body for all movement.
        /// </summary>
        public Rigidbody Rigidbody => ActiveRigidbody;

        /// <summary>
        /// The managed collider currently used by the body for all movement queries.
        /// </summary>
        public Collider Collider => ActiveCollider;

        /// <summary>
        /// True when the body ended its last Move on stable ground.
        /// </summary>
        public bool IsGrounded => _isGrounded;

        /// <summary>
        /// Complete world-space velocity produced by the last Move.
        /// Includes attached-anchor motion.
        /// </summary>
        public Velocity Velocity => _velocity;

        /// <summary>
        /// Complete velocity contribution currently inherited from the attached anchor.
        /// This contribution is already included in Velocity.
        /// </summary>
        public Velocity AttachedAnchorVelocity => _attachedAnchorVelocity;

        /// <summary>
        /// Grounding data from the last Move.
        /// </summary>
        public CharacterGroundingReport GroundingReport => _groundingReport;

        /// <summary>
        /// Current passive platform anchor, primarily useful for debugging.
        /// </summary>
        public CharacterBodyAnchor CurrentPlatformAnchor => _currentPlatformAnchor;

        /// <summary>
        /// The character's semantic simulation coordinate frame.
        ///
        /// Up is the semantic vertical/traversal direction.
        /// Forward is the semantic facing direction.
        /// Right is derived from Up and Forward.
        ///
        /// The initial basis is created from the CharacterBody's Transform. After
        /// initialization, Basis is semantic CharacterBody state and is not
        /// reconstructed from Transform every frame. CharacterBody uses the basis
        /// during simulation to determine the physical orientation of the body.
        /// </summary>
        public CharacterBodyBasis Basis {
            get => _basis;

            set {
                if (!value.IsValid) {
                    throw new ArgumentException(
                        "CharacterBody.Basis must contain normalized, perpendicular Up and Forward directions.");
                }

                _basis = value;
            }
        }

        public float SlopeLimit {
            get => _settings.StepAndSlopeSettings.SlopeLimit;
            set => _settings.StepAndSlopeSettings.SlopeLimit = Mathf.Clamp(value, 0f, 89f);
        }

        public float StepOffset {
            get => _settings.StepAndSlopeSettings.StepOffset;
            set => _settings.StepAndSlopeSettings.StepOffset = Mathf.Max(0f, value);
        }

        public float SkinWidth {
            get => _settings.SkinWidth;
            set => _settings.SkinWidth = Mathf.Max(0f, value);
        }

        /// <summary>
        /// When enabled, the body applies Unity's global Physics.gravity during Move.
        /// </summary>
        public bool UseGravity {
            get => _settings.UseGravity;
            set => _settings.UseGravity = value;
        }

        /// <summary>
        /// When enabled, the body custom-interpolates its own root between simulation ticks.
        /// </summary>
        public bool UseInterpolation {
            get => _settings.UseInterpolation;
            set => _settings.UseInterpolation = value;
        }

        internal bool IsForceUngrounded => _forceUngroundTimeRemaining > 0f;

        private void OnDrawGizmosSelected() {
            var defaultMatrix = Gizmos.matrix;

            switch (_geometryType) {
                case GeometryTypes.Sphere:
                    DrawSphereGizmo();
                    break;

                case GeometryTypes.Box:
                    DrawBoxGizmo();
                    break;

                case GeometryTypes.Capsule:
                    DrawCapsuleGizmo();
                    break;
            }

            Gizmos.matrix = defaultMatrix;
        }

        private Quaternion ResolveBasisRotation(Quaternion fallbackRotation) {
            if (!_basis.IsValid)
                return fallbackRotation;

            var basisRotation = Quaternion.LookRotation(_basis.Forward, _basis.Up);
            return ConstrainRotationForShape(basisRotation);
        }

        private void DrawSphereGizmo() {
            Gizmos.color = new Color(0.4f, 1.0f, 0.4f, 0.8f);

            if (_sphereCollider != null) {
                CharacterPhysicsQueries.GetSphereWorldData(_sphereCollider, transform.position, transform.rotation,
                    out var worldCenter, out var radius);
                Gizmos.DrawWireSphere(worldCenter, radius);
                return;
            }

            var absoluteScale = MobilityMath.Abs(transform.lossyScale);
            var center = transform.position + transform.rotation *
                MobilityMath.ScaleByAbsolute(_sphereGeometrySettings.Center, absoluteScale);
            Gizmos.DrawWireSphere(center, _sphereGeometrySettings.Radius * MobilityMath.MaxComponent(absoluteScale));
        }

        /// <summary>
        /// Resolves the semantic up for editor-time visualization, before Awake creates the basis.
        /// </summary>
        private Vector3 GetVisualizationUp() {
            return _basisInitialized ? _basis.Up : transform.up;
        }

        private void DrawBoxGizmo() {
            Gizmos.color = new Color(0.4f, 1.0f, 0.4f, 0.8f);

            // Mirror the runtime invariant: the drawn box is upright and may only yaw.
            var uprightRotation = MobilityMath.ConstrainToUprightRotation(transform.rotation, GetVisualizationUp());

            Vector3 center;
            Vector3 halfExtents;
            if (_boxCollider != null) {
                CharacterPhysicsQueries.GetBoxWorldData(_boxCollider, transform.position, uprightRotation,
                    out center, out _, out halfExtents);
            }
            else {
                var absoluteScale = MobilityMath.Abs(transform.lossyScale);
                center = transform.position + uprightRotation *
                    MobilityMath.ScaleByAbsolute(_boxGeometrySettings.Center, absoluteScale);
                halfExtents = MobilityMath.ScaleByAbsolute(_boxGeometrySettings.Size * 0.5f, absoluteScale);
            }

            var previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, uprightRotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
            Gizmos.matrix = previousMatrix;
        }

        private void DrawCapsuleGizmo() {
            Vector3 point1;
            Vector3 point2;
            float radius;
            Vector3 worldCenter;

            if (_capsuleCollider != null) {
                CharacterPhysicsQueries.GetCapsuleWorldData(_capsuleCollider, transform.position, transform.rotation,
                    out point1, out point2, out radius);
                worldCenter = (point1 + point2) * 0.5f;
            }
            else {
                var absoluteScale = MobilityMath.Abs(transform.lossyScale);
                radius = Mathf.Max(0.0001f,
                    _capsuleGeometrySettings.Radius * Mathf.Max(absoluteScale.x, absoluteScale.z));
                var cylinderHalf = Mathf.Max(0f, _capsuleGeometrySettings.Height * absoluteScale.y * 0.5f - radius);
                worldCenter = transform.position + transform.rotation *
                    MobilityMath.ScaleByAbsolute(_capsuleGeometrySettings.Center, absoluteScale);
                var upAxis = transform.rotation * Vector3.up;
                point1 = worldCenter + upAxis * cylinderHalf;
                point2 = worldCenter - upAxis * cylinderHalf;
            }

            // Physical Y-axis capsule shape
            Gizmos.color = new Color(0.4f, 1.0f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(point1, radius);
            Gizmos.DrawWireSphere(point2, radius);

            var up = GetVisualizationUp();
            MobilityMath.GetPlanarAxes(up, out var tangentA, out var tangentB);
            var offsetA = tangentA * radius;
            var offsetB = tangentB * radius;
            Gizmos.DrawLine(point1 + offsetA, point2 + offsetA);
            Gizmos.DrawLine(point1 - offsetA, point2 - offsetA);
            Gizmos.DrawLine(point1 + offsetB, point2 + offsetB);
            Gizmos.DrawLine(point1 - offsetB, point2 - offsetB);

            // Simple basis-up visualization for grounding/slope debugging.
            Gizmos.color = Color.green;
            Gizmos.DrawLine(worldCenter, worldCenter + up * Mathf.Max(radius * 2f, 0.25f));
        }


        private void OnValidate() {
            ValidateSerializedData();
        }

        private void Reset() {
            _geometryType = GeometryTypes.Capsule;
            _capsuleGeometrySettings = CapsuleGeometrySettings.Default;
            _sphereGeometrySettings = SphereGeometrySettings.Default;
            _boxGeometrySettings = BoxGeometrySettings.Default;

            ValidateSerializedData();
        }

        /// <summary>
        /// Enforces shape-specific rotation invariants. A box character may only yaw around
        /// the basis Up; pitch and roll are stripped so the box can never tip over.
        /// </summary>
        private Quaternion ConstrainRotationForShape(Quaternion rotation) {
            return _geometryType == GeometryTypes.Box
                ? MobilityMath.ConstrainToUprightRotation(rotation, _basis.Up)
                : rotation;
        }

        private void Awake() {
            // 1. Semantic character frame
            _basis = CharacterBodyBasis.FromTransform(transform);
            _basisInitialized = true;

            // 2. Allocate memory buffer context for the Solvers
            Context = new SolverContext(); //Change name !!

            // 2. Setup Physics Components
            SetupRigidbody();
            SetupCollider();

            // 3. Finalise data and pose
            ValidateSerializedData();
            InitializeSimulationPose(_rigidbody.position, ConstrainRotationForShape(_rigidbody.rotation));
            RestoreSimulationPose();
        }

        private void FixedUpdate() {
            RestoreSimulationPose();
        }

        private void LateUpdate() {
            if (!_simulationPoseInitialized) {
                return;
            }

            if (!_settings.UseInterpolation || _lastInterpolationDeltaTime <= Mathf.Epsilon) {
                RestoreSimulationPose();
                return;
            }

            var interpolationFactor =
                Mathf.Clamp01((Time.time - _lastInterpolationStartTime) / _lastInterpolationDeltaTime);
            ApplyPresentationPose(
                Vector3.Lerp(_previousSimulationPose.Position, _simulationPose.Position, interpolationFactor),
                Quaternion.Slerp(_previousSimulationPose.Orientation, _simulationPose.Orientation,
                    interpolationFactor));
        }

        private void SetupRigidbody() {
            foreach (var rb in GetComponents<Rigidbody>()) {
                Destroy(rb);
            }

            _rigidbody = gameObject.AddComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _rigidbody.interpolation = RigidbodyInterpolation.None;
        }

        private void SetupCollider() {
            foreach (var col in GetComponents<Collider>()) {
                Destroy(col);
            }

            switch (_geometryType) {
                case GeometryTypes.Capsule:
                    _capsuleCollider = gameObject.AddComponent<CapsuleCollider>();
                    _capsuleGeometrySettings.ApplyTo(_capsuleCollider);
                    _collider = _capsuleCollider;
                    break;

                case GeometryTypes.Sphere:
                    _sphereCollider = gameObject.AddComponent<SphereCollider>();
                    _sphereGeometrySettings.ApplyTo(_sphereCollider);
                    _collider = _sphereCollider;
                    break;

                case GeometryTypes.Box:
                    _boxCollider = gameObject.AddComponent<BoxCollider>();
                    _boxGeometrySettings.ApplyTo(_boxCollider);
                    _collider = _boxCollider;
                    break;
            }

            _collider.isTrigger = false;
        }

        public CharacterBodyCollisionFlags Move(VelocityUpdateRequest request) {
            return Simulate(
                request.Velocity,
                request.DeltaTime
            );
        }

        public CharacterBodyCollisionFlags Move(PoseUpdateRequest request) {
            return SimulateTowardPose(
                request.Pose,
                request.DeltaTime
            );
        }

        public void SetVelocity(Vector3 velocity) {
            _velocity.Linear = velocity;
        }

        public void AddVelocity(Vector3 velocityChange, bool projectOnGroundIfStable = false) {
            if (projectOnGroundIfStable && _isGrounded) {
                velocityChange = Vector3.ProjectOnPlane(velocityChange, _basis.Up);
            }

            _velocity.Linear += velocityChange;
        }

        public void Launch(Vector3 velocity, float forceUngroundDuration = 0f) {
            SetVelocity(velocity);

            if (forceUngroundDuration > 0f) {
                ForceUnground(forceUngroundDuration);
            }
        }

        public void Teleport(Vector3 position, Quaternion rotation) {
            rotation = ConstrainRotationForShape(rotation);

            // Teleporting changes physical facing, so keep the semantic
            // Forward direction synchronized with the accepted rotation.
            UpdateBasisForwardFromOrientation(rotation);

            InitializeSimulationPose(position, rotation);

            _groundingReport = GroundQuerySolver.GetDefaultReport(this);

            _isGrounded = false;
            _wasGrounded = false;

            _currentPlatformAnchor = null;

            // Velocity is already the complete physical velocity.
            // Clearing the attached-anchor contribution means any existing
            // velocity is now treated as CharacterBody-owned velocity.
            _attachedAnchorVelocity = default;

            ApplySimulationPose(position, rotation);
        }

        /// <summary>
        /// Temporarily suppresses ground snapping, typically for jumps.
        /// </summary>
        public void ForceUnground(float duration) {
            _forceUngroundTimeRemaining = Mathf.Max(_forceUngroundTimeRemaining, duration);
            _isGrounded = false;
        }

        /// <summary>
        /// Captures the simulation-owned state required to restore this body later.
        /// </summary>
        public CharacterBodyState GetState() {
            return new CharacterBodyState {
                Pose = _simulationPose,
                Velocity = _velocity,
                IsGrounded = _isGrounded,
                GroundNormal = _groundingReport.GroundNormal,
                BasisUp = _basis.Up,
                BasisForward = _basis.Forward,
                ForceUngroundTimeRemaining = _forceUngroundTimeRemaining,
                WasGrounded = _wasGrounded
            };
        }

        /// <summary>
        /// Restores a previously captured simulation state.
        /// </summary>
        public void ApplyState(CharacterBodyState state) {
            var restoredBasis = CharacterBodyBasis
                .FromTransform(transform)
                .WithUpDirection(state.BasisUp)
                .WithForwardDirection(state.BasisForward);

            _basis = restoredBasis;

            Quaternion rotation = ConstrainRotationForShape(state.Pose.Orientation);

            InitializeSimulationPose(state.Pose.Position, rotation);

            RestoreSimulationPose();

            _velocity = state.Velocity;
            _attachedAnchorVelocity = default;
            _isGrounded = state.IsGrounded;
            _wasGrounded = state.WasGrounded;
            _forceUngroundTimeRemaining = state.ForceUngroundTimeRemaining;
            _groundingReport.GroundNormal = state.GroundNormal;
            _groundingReport.IsStableOnGround = state.IsGrounded;
            _groundingReport.FoundAnyGround = state.IsGrounded;
            _currentPlatformAnchor = null;
        }

        internal bool ShouldCollideWith(Collider col) {
            if (col == null || col == _collider) {
                return false;
            }

            if (((1 << col.gameObject.layer) & _settings.CollidableLayers.value) == 0) {
                return false;
            }

            return ColliderFilter?.Invoke(col) ?? true;
        }

        internal HitStabilityReport EvaluateHitStability(Vector3 normal, Collider col, Vector3 position,
            Quaternion rotation, Vector3 hitPoint) {
            return EvaluateHitStability(normal, col, position, rotation, hitPoint, true);
        }

        internal HitStabilityReport EvaluateHitStability(Vector3 normal, Collider col, Vector3 position,
            Quaternion rotation, Vector3 hitPoint, bool allowLedgeSnap) {
            HitStabilityReport report = default;
            var layerStable = col != null && ((1 << col.gameObject.layer) & _settings.StableGroundLayers.value) != 0;
            report.FoundInnerNormal = true;
            report.InnerNormal = normal;
            report.FoundOuterNormal = true;
            report.OuterNormal = normal;
            report.LedgeGroundNormal = normal;
            report.IsStable =
                MobilityMath.IsStableNormal(normal, _basis.Up, _settings.StepAndSlopeSettings.SlopeLimit) &&
                layerStable;

            if (_settings.StepAndSlopeSettings.LedgeHandling && col != null) {
                if (_geometryType == GeometryTypes.Box) {
                    EvaluateBoxSupport(ref report, position, rotation, hitPoint);
                }
                else {
                    EvaluateRoundedLedge(ref report, position, rotation, normal);
                }
            }

            if (!allowLedgeSnap && report.LedgeDetected) {
                report.IsStable = false;
            }

            if (report.IsStable && _settings.StepAndSlopeSettings.LedgeHandling) {
                var ledgeThreshold = GetEffectiveLedgeDistanceThreshold(position, rotation);
                if (report.LedgeDetected && report.IsOnEmptySideOfLedge && report.DistanceFromLedge > ledgeThreshold) {
                    report.IsStable = false;
                }

                var denivelationAngle = Vector3.Angle(report.InnerNormal, report.OuterNormal);
                if (denivelationAngle > _settings.StepAndSlopeSettings.MaxStableDenivelationAngle) {
                    report.IsStable = false;
                }
            }

            report = ModifyHitStability?.Invoke(report) ?? report;
            return report;
        }

        private void EvaluateRoundedLedge(ref HitStabilityReport report, Vector3 position, Quaternion rotation,
            Vector3 normal) {
            var up = _basis.Up;
            var axis = Vector3.Cross(up, normal);
            if (axis.sqrMagnitude <= 0.0001f) {
                return;
            }

            axis.Normalize();
            var outward = Vector3.Cross(axis, up).normalized;
            var supportDistance = CharacterPhysicsQueries.GetSupportDistance(_collider, position, rotation, outward);
            var sampleOrigin = CharacterPhysicsQueries.GetWorldCenter(_collider, position, rotation) +
                               (outward * supportDistance);

            if (!CharacterPhysicsQueries.Raycast(sampleOrigin + (up * 0.05f), -up,
                    GetEffectiveStepOffset(position, rotation) + 0.25f, _settings.CollidableLayers,
                    QueryTriggerInteraction.Ignore, Context.SweepResults, ShouldCollideWith, out RaycastHit ledgeHit)) {
                report.LedgeDetected = true;
                report.IsOnEmptySideOfLedge = true;
                report.DistanceFromLedge = supportDistance;
                report.LedgeRightDirection = axis;
                report.LedgeFacingDirection = outward;
                report.IsMovingTowardsEmptySideOfLedge = false;
            }
            else {
                report.LedgeGroundNormal = ledgeHit.normal;
                report.DistanceFromLedge = 0f;
                report.LedgeDetected = false;
                report.IsOnEmptySideOfLedge = false;
                report.LedgeRightDirection = axis;
                report.LedgeFacingDirection = outward;
            }
        }

        private void EvaluateBoxSupport(ref HitStabilityReport report, Vector3 position, Quaternion rotation,
            Vector3 hitPoint) {
            var up = _basis.Up;
            var down = -up;
            CharacterPhysicsQueries.GetBoxWorldData(_boxCollider, position, rotation, out var worldCenter,
                out var boxRotation, out var halfExtents);

            var planarExtent =
                CharacterPhysicsQueries.GetMinimalPlanarSupportDistance(_boxCollider, position, rotation, up);
            var horizontalInset =
                Mathf.Min(GetEffectiveLedgeDistanceThreshold(position, rotation), planarExtent * 0.75f);
            horizontalInset = Mathf.Max(0.01f, horizontalInset);
            var rayDistance = Mathf.Max(_settings.SkinWidth + 0.1f, GetEffectiveStepOffset(position, rotation) + 0.1f);

            var centerOffset = new Vector3(0f, -halfExtents.y, 0f);
            var frontRightOffset = new Vector3(halfExtents.x - horizontalInset, -halfExtents.y,
                halfExtents.z - horizontalInset);
            var frontLeftOffset = new Vector3(-(halfExtents.x - horizontalInset), -halfExtents.y,
                halfExtents.z - horizontalInset);
            var backRightOffset = new Vector3(halfExtents.x - horizontalInset, -halfExtents.y,
                -(halfExtents.z - horizontalInset));
            var backLeftOffset = new Vector3(-(halfExtents.x - horizontalInset), -halfExtents.y,
                -(halfExtents.z - horizontalInset));

            var supportedSamples = 0;
            var accumulatedNormal = Vector3.zero;
            var centerSupported = TrySampleBoxSupport(worldCenter, boxRotation, centerOffset, down, rayDistance,
                ref accumulatedNormal, ref supportedSamples);
            var frontRightSupported = TrySampleBoxSupport(worldCenter, boxRotation, frontRightOffset, down, rayDistance,
                ref accumulatedNormal, ref supportedSamples);
            var frontLeftSupported = TrySampleBoxSupport(worldCenter, boxRotation, frontLeftOffset, down, rayDistance,
                ref accumulatedNormal, ref supportedSamples);
            var backRightSupported = TrySampleBoxSupport(worldCenter, boxRotation, backRightOffset, down, rayDistance,
                ref accumulatedNormal, ref supportedSamples);
            var backLeftSupported = TrySampleBoxSupport(worldCenter, boxRotation, backLeftOffset, down, rayDistance,
                ref accumulatedNormal, ref supportedSamples);

            if (supportedSamples > 0) {
                var averageNormal = (accumulatedNormal / supportedSamples).normalized;
                if (averageNormal.sqrMagnitude > 0.0001f) {
                    report.OuterNormal = averageNormal;
                    report.InnerNormal = averageNormal;
                    report.LedgeGroundNormal = averageNormal;
                }
            }

            report.LedgeDetected = supportedSamples < 5;
            report.IsOnEmptySideOfLedge = supportedSamples < 3;
            report.DistanceFromLedge = planarExtent * ((5 - supportedSamples) / 5f);
            report.IsStable = report.IsStable && supportedSamples >= 3;

            var localHitPoint = Quaternion.Inverse(boxRotation) * (hitPoint - worldCenter);
            var localFacing = new Vector3(localHitPoint.x, 0f, localHitPoint.z);
            if (localFacing.sqrMagnitude <= 0.0001f) {
                localFacing = Vector3.forward;
            }

            localFacing.Normalize();
            report.LedgeFacingDirection = (boxRotation * localFacing).normalized;
            report.LedgeRightDirection = Vector3.Cross(up, report.LedgeFacingDirection).normalized;

            var frontSupported = frontRightSupported || frontLeftSupported;
            var backSupported = backRightSupported || backLeftSupported;
            var rightSupported = frontRightSupported || backRightSupported;
            var leftSupported = frontLeftSupported || backLeftSupported;

            if (!centerSupported) {
                report.IsOnEmptySideOfLedge = true;
                report.DistanceFromLedge = planarExtent;
            }
            else if (frontSupported != backSupported) {
                report.LedgeFacingDirection =
                    frontSupported ? -(boxRotation * Vector3.forward) : (boxRotation * Vector3.forward);
                report.LedgeRightDirection = Vector3.Cross(up, report.LedgeFacingDirection).normalized;
            }
            else if (rightSupported != leftSupported) {
                report.LedgeFacingDirection =
                    rightSupported ? -(boxRotation * Vector3.right) : (boxRotation * Vector3.right);
                report.LedgeRightDirection = Vector3.Cross(up, report.LedgeFacingDirection).normalized;
            }
        }

        private bool TrySampleBoxSupport(Vector3 worldCenter, Quaternion rotation, Vector3 offset, Vector3 down,
            float rayDistance, ref Vector3 accumulatedNormal, ref int supportedSamples) {
            var sampleOrigin = worldCenter + rotation * offset + _basis.Up * 0.05f;
            if (!CharacterPhysicsQueries.Raycast(sampleOrigin, down, rayDistance, _settings.CollidableLayers,
                    QueryTriggerInteraction.Ignore, Context.SweepResults, ShouldCollideWith,
                    out var supportHit)) {
                return false;
            }

            supportedSamples++;
            accumulatedNormal += supportHit.normal;
            return true;
        }

        internal float GetEffectiveStepOffset(Vector3 position, Quaternion rotation) {
            var supportUp = CharacterPhysicsQueries.GetSupportDistance(_collider, position, rotation, _basis.Up);
            return Mathf.Min(_settings.StepAndSlopeSettings.StepOffset, Mathf.Max(0f, supportUp - _settings.SkinWidth));
        }

        internal float GetEffectiveLedgeDistanceThreshold(Vector3 position, Quaternion rotation) {
            return Mathf.Min(_settings.StepAndSlopeSettings.MaxStableDistanceFromLedge,
                CharacterPhysicsQueries.GetMinimalPlanarSupportDistance(_collider, position, rotation, _basis.Up));
        }

        internal void ReportCollision(RaycastHit hit, HitStabilityReport stabilityReport, bool isGroundHit) {
            if (_settings.EnableRigidbodyInteraction && hit.rigidbody != null && !hit.rigidbody.isKinematic &&
                hit.rigidbody != _rigidbody) {
                var otherCharacterBody = hit.rigidbody.GetComponent<CharacterBody>();
                var rigidbodyHit = new CharacterRigidbodyHit {
                    Rigidbody = hit.rigidbody,
                    OtherCharacterBody = otherCharacterBody,
                    HitPoint = hit.point,
                    HitNormal = hit.normal,
                    HitVelocity = _velocity.Linear,
                    RigidbodyVelocity = hit.rigidbody.GetPointVelocity(hit.point),
                    StableOnHit = stabilityReport.IsStable
                };
                if (ModifyRigidbodyHit != null) {
                    rigidbodyHit = ModifyRigidbodyHit.Invoke(rigidbodyHit);
                }

                Context.RigidbodyHits.Add(rigidbodyHit);
            }

            Collided?.Invoke(this, new CharacterCollisionEventArgs(new CharacterHit {
                Collider = hit.collider,
                Rigidbody = hit.rigidbody,
                Point = hit.point,
                Normal = hit.normal,
                Distance = hit.distance,
                IsGroundHit = isGroundHit,
                StabilityReport = stabilityReport
            }));
        }

        internal void SetGroundingFromHit(ref CharacterGroundingReport groundingReport, RaycastHit hit,
            HitStabilityReport stability) {
            var resolvedGroundNormal = stability.LedgeGroundNormal.sqrMagnitude > 0.0001f
                ? stability.LedgeGroundNormal.normalized
                : hit.normal;

            groundingReport.FoundAnyGround = true;
            groundingReport.IsStableOnGround = stability.IsStable;
            groundingReport.GroundNormal = resolvedGroundNormal;
            groundingReport.InnerGroundNormal =
                stability.FoundInnerNormal ? stability.InnerNormal : resolvedGroundNormal;
            groundingReport.OuterGroundNormal =
                stability.FoundOuterNormal ? stability.OuterNormal : resolvedGroundNormal;
            groundingReport.GroundCollider = hit.collider;
            groundingReport.GroundPoint = hit.point;
        }

        private CharacterBodyCollisionFlags SimulateTowardPose(Pose targetPose, float deltaTime) {
            deltaTime = Mathf.Max(deltaTime, Mathf.Epsilon);

            var displacement = targetPose.Position - _simulationPose.Position;

            var angularVelocity =
                CalculateAngularVelocity(_simulationPose.Orientation, targetPose.Orientation, deltaTime);

            var requestedVelocity = new Velocity {
                Linear = displacement / deltaTime,
                Angular = angularVelocity
            };

            return Simulate(requestedVelocity, deltaTime);
        }

        private static Vector3 CalculateAngularVelocity(Quaternion from, Quaternion to, float deltaTime) {
            var delta = to * Quaternion.Inverse(from);

            delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);

            if (float.IsNaN(axis.x) || axis.sqrMagnitude <= Mathf.Epsilon)
                return Vector3.zero;

            // Use the shortest rotational path.
            if (angleDegrees > 180f)
                angleDegrees -= 360f;

            return axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
        }

        private Velocity ApplyRequestedVelocity(Velocity current, Velocity requested) {
            var currentVertical = Vector3.Project(current.Linear, _basis.Up);
            var requestedPlanar = Vector3.ProjectOnPlane(requested.Linear, _basis.Up);

            current.Linear = requestedPlanar + currentVertical;
            current.Angular = requested.Angular;

            return current;
        }

        private CharacterBodyCollisionFlags Simulate(
            Velocity requestedVelocity,
            float deltaTime) {
            deltaTime =
                Mathf.Max(
                    deltaTime,
                    Mathf.Epsilon
                );

            BeforeSimulate?.Invoke(
                this,
                deltaTime
            );

            Context.RigidbodyHits.Clear();
            Context.PushedRigidbodies.Clear();

            _wasGrounded = _isGrounded;

            if (_forceUngroundTimeRemaining > 0f) {
                _forceUngroundTimeRemaining =
                    Mathf.Max(
                        0f,
                        _forceUngroundTimeRemaining - deltaTime
                    );
            }

            RestoreSimulationPose();

            var simulationPose =
                _simulationPose;

            // ------------------------------------------------------------
            // 1. Resolve requested physical orientation.
            // ------------------------------------------------------------

            var rotation =
                ResolveBasisRotation(
                    simulationPose.Orientation
                );

            if (requestedVelocity.Angular.sqrMagnitude >
                Mathf.Epsilon) {
                var angle =
                    requestedVelocity.Angular.magnitude *
                    Mathf.Rad2Deg *
                    deltaTime;

                rotation =
                    Quaternion.AngleAxis(
                        angle,
                        requestedVelocity.Angular.normalized
                    ) * rotation;
            }

            rotation =
                ConstrainRotationForShape(
                    rotation
                );

            UpdateBasisForwardFromOrientation(
                rotation
            );
            
            // ------------------------------------------------------------
// Refresh grounding at the start of this simulation step.
// This tells CharacterBody whether its current position is
// supported before movement is applied.
// ------------------------------------------------------------

            bool allowLedgeSnap =
                _settings.StepAndSlopeSettings.MaxVelocityForLedgeSnap <= 0f ||
                Vector3.ProjectOnPlane(
                    requestedVelocity.Linear,
                    _basis.Up
                ).magnitude <=
                _settings.StepAndSlopeSettings.MaxVelocityForLedgeSnap;

            _groundingReport =
                GroundQuerySolver.ProbeGround(
                    this,
                    simulationPose.Position,
                    rotation,
                    _settings.StepAndSlopeSettings.GroundDetectionExtraDistance,
                    allowLedgeSnap,
                    false
                );

            if (IsForceUngrounded)
            {
                _groundingReport.SnappingPrevented = true;
                _groundingReport.IsStableOnGround = false;
            }

            // ------------------------------------------------------------
            // 2. Resolve current attached-anchor contribution.
            // ------------------------------------------------------------

            Velocity attachedAnchorVelocity =
                new Velocity {
                    Linear =
                        PlatformAttachmentSolver.GetInheritedVelocity(
                            this,
                            _currentPlatformAnchor,
                            simulationPose.Position,
                            deltaTime
                        ),

                    // CharacterBody does not currently rotate with the anchor.
                    Angular = Vector3.zero
                };

            // ------------------------------------------------------------
            // 3. Remove the OLD anchor contribution from the complete
            //    stored velocity.
            // ------------------------------------------------------------

            Velocity workingVelocity =
                Velocity.SubtractVelocity(
                    _velocity,
                    _attachedAnchorVelocity
                );

            // ------------------------------------------------------------
            // 4. CharacterBody physical effects.
            // ------------------------------------------------------------

            if (_groundingReport.IsStableOnGround) {
                workingVelocity.Linear =
                    VelocityProjectionSolver.ProjectForGrounding(
                        workingVelocity.Linear,
                        _groundingReport.GroundNormal,
                        _basis.Up
                    );
            }
            else if (_settings.UseGravity) {
                workingVelocity.Linear +=
                    Physics.gravity * deltaTime;
            }

            // ------------------------------------------------------------
            // 5. Apply this step's requested locomotion velocity.
            // ------------------------------------------------------------

            workingVelocity =
                ApplyRequestedVelocity(
                    workingVelocity,
                    requestedVelocity
                );

            // ------------------------------------------------------------
            // 6. Compose complete world velocity.
            // ------------------------------------------------------------

            Velocity worldVelocity =
                Velocity.AddVelocity(
                    workingVelocity,
                    attachedAnchorVelocity
                );

            // ------------------------------------------------------------
            // 7. Existing physical movement pipeline.
            // ------------------------------------------------------------

            Vector3 position =
                simulationPose.Position;

            OverlapDecollisionSolver.ResolveOverlaps(
                this,
                ref position,
                rotation
            );

            CharacterBodyCollisionFlags flags =
                MovementSweepSolver.MoveWithCollisions(
                    this,
                    ref position,
                    rotation,
                    ref worldVelocity.Linear,
                    deltaTime,
                    ref _groundingReport
                );

            ApplyCharacterVelocityFeedbackFromRigidbodies(
                ref worldVelocity.Linear
            );

            // Collision response has modified complete world velocity.
            // Recover CharacterBody-owned velocity before applying
            // slope/grounding correction.
            workingVelocity.Linear =
                worldVelocity.Linear -
                attachedAnchorVelocity.Linear;

            workingVelocity.Linear =
                StepAndSlopeSolver.Apply(
                    this,
                    workingVelocity.Linear,
                    _groundingReport
                );

            // Recompose the complete physical velocity.
            worldVelocity.Linear =
                workingVelocity.Linear +
                attachedAnchorVelocity.Linear;
            
            // ------------------------------------------------------------
// Refresh grounding at the resulting position.
// This is what detects walking beyond a finite floor.
// ------------------------------------------------------------

            _groundingReport =
                GroundQuerySolver.ProbeGround(
                    this,
                    position,
                    rotation,
                    _settings.StepAndSlopeSettings.GroundDetectionExtraDistance,
                    allowLedgeSnap,
                    false
                );

            if (IsForceUngrounded)
            {
                _groundingReport.SnappingPrevented = true;
                _groundingReport.IsStableOnGround = false;
            }
            
            
            // ------------------------------------------------------------
// Snap to nearby stable ground.
// Snap is only allowed when the simulation step started grounded.
// It is never allowed while ForceUnground is active.
// ------------------------------------------------------------

            if (_settings.SnapToGround &&
                _wasGrounded &&
                !IsForceUngrounded &&
                !_groundingReport.IsStableOnGround &&
                _settings.GroundSnapDistance > 0f)
            {
                if (GroundQuerySolver.TrySnapToGround(
                        this,
                        ref position,
                        rotation,
                        _settings.GroundSnapDistance,
                        allowLedgeSnap,
                        out CharacterGroundingReport snappedGround))
                {
                    _groundingReport =
                        snappedGround;

                    // The character has been placed onto stable ground.
                    // Remove any velocity directed into that ground.
                    worldVelocity.Linear =
                        VelocityProjectionSolver.ProjectForGrounding(
                            worldVelocity.Linear,
                            _groundingReport.GroundNormal,
                            _basis.Up
                        );
                }
            }

            // ------------------------------------------------------------
            // 8. Resolve which anchor is attached for the NEXT step.
            // ------------------------------------------------------------

            CharacterBodyAnchor previousPlatformAnchor =
                _currentPlatformAnchor;
            
            _currentPlatformAnchor =
                PlatformAttachmentSolver.ResolveAnchor(
                    _groundingReport
                );

            _isGrounded =
                _groundingReport.FoundAnyGround &&
                _groundingReport.IsStableOnGround;
            
            if (!_isGrounded &&
                previousPlatformAnchor != null &&
                !ReferenceEquals(
                    previousPlatformAnchor,
                    _currentPlatformAnchor) &&
                !_settings.PreservePlatformMomentum)
            {
                worldVelocity.Linear -=
                    attachedAnchorVelocity.Linear;
            }

            // ------------------------------------------------------------
            // 9. Commit complete velocity.
            // ------------------------------------------------------------

            _attachedAnchorVelocity =
                _currentPlatformAnchor != null
                    ? attachedAnchorVelocity
                    : default;

            _velocity =
                worldVelocity;

            if (_isGrounded) {
                flags |=
                    CharacterBodyCollisionFlags.Below;
            }

            // ------------------------------------------------------------
            // 10. Commit pose.
            // ------------------------------------------------------------

            _previousSimulationPose =
                _simulationPose;

            _simulationPose =
                new Pose {
                    Position = position,
                    Orientation = rotation
                };

            _simulationPoseInitialized = true;

            _lastInterpolationStartTime =
                Time.time;

            _lastInterpolationDeltaTime =
                deltaTime;

            ApplySimulationPose(
                position,
                rotation
            );

            ApplyDeferredRigidbodyInteractions();

            AfterSimulate?.Invoke(
                this,
                deltaTime
            );

            if (_wasGrounded != _isGrounded) {
                GroundedStateChanged?.Invoke(
                    this
                );
            }

            return flags;
        }

        private void UpdateBasisForwardFromOrientation(Quaternion orientation) {
            var facing = orientation * Vector3.forward;

            _basis = _basis.WithForwardDirection(facing);
        }

        private void ValidateSerializedData() {
            _settings ??= new CharacterBodySettings();

            _capsuleGeometrySettings.Validate();
            _sphereGeometrySettings.Validate();
            _boxGeometrySettings.Validate();

            // Extents represent the actual supported colliders. The capsule is the upright
            // Y-axis invariant shape; its collider orientation does not follow Basis.Up.
            var maxHorizontalExtent = _geometryType switch {
                GeometryTypes.Capsule => _capsuleGeometrySettings.Radius,
                GeometryTypes.Sphere => _sphereGeometrySettings.Radius,
                GeometryTypes.Box => Mathf.Min(_boxGeometrySettings.Size.x, _boxGeometrySettings.Size.z) * 0.5f,
                _ => 0.5f
            };
            var maxVerticalExtent = _geometryType switch {
                GeometryTypes.Capsule => _capsuleGeometrySettings.Height * 0.5f,
                GeometryTypes.Sphere => _sphereGeometrySettings.Radius,
                GeometryTypes.Box => _boxGeometrySettings.Size.y * 0.5f,
                _ => 1f
            };

            _settings.Validate(maxHorizontalExtent, maxVerticalExtent);
        }

        private void InitializeSimulationPose(Vector3 position, Quaternion rotation) {
            _simulationPose.Position = position;
            _simulationPose.Orientation = rotation;
            _previousSimulationPose.Position = position;
            _previousSimulationPose.Orientation = rotation;
            _lastInterpolationStartTime = Time.time;
            _lastInterpolationDeltaTime = Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);
            _simulationPoseInitialized = true;
        }

        private void RestoreSimulationPose() {
            if (!_simulationPoseInitialized || _rigidbody == null) {
                return;
            }

            ApplyPresentationPose(_simulationPose.Position, _simulationPose.Orientation);
        }

        private void ApplySimulationPose(Vector3 position, Quaternion rotation) {
            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
            transform.SetPositionAndRotation(position, rotation);
        }

        private void ApplyPresentationPose(Vector3 position, Quaternion rotation) {
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        private void ApplyCharacterVelocityFeedbackFromRigidbodies(ref Vector3 worldVelocity) {
            if (!_settings.EnableRigidbodyInteraction ||
                _settings.RigidbodyInteractionMode == CharacterBodyRigidbodyInteractionMode.None) {
                return;
            }

            var accumulatedCorrection = Vector3.zero;
            foreach (var hit in Context.RigidbodyHits) {
                if (hit.Rigidbody == null || hit.Rigidbody.isKinematic) {
                    continue;
                }

                var relativeVelocity = worldVelocity - hit.RigidbodyVelocity;
                var intoBody = Vector3.Dot(relativeVelocity, hit.HitNormal);
                if (intoBody >= 0f) {
                    continue;
                }

                var correction = -hit.HitNormal * intoBody;
                if (_settings.RigidbodyInteractionMode == CharacterBodyRigidbodyInteractionMode.SimulatedDynamic) {
                    var bodyMass = Mathf.Max(hit.Rigidbody.mass, 0.01f);
                    var characterMass = Mathf.Max(_settings.SimulatedCharacterMass, 0.01f);
                    var ratio = bodyMass / (bodyMass + characterMass);
                    correction *= ratio;
                }

                if (hit.StableOnHit) {
                    correction = Vector3.ProjectOnPlane(correction, _basis.Up);
                }

                accumulatedCorrection += correction;

                if (hit.OtherCharacterBody != null && hit.OtherCharacterBody != this &&
                    _settings.RigidbodyInteractionMode == CharacterBodyRigidbodyInteractionMode.SimulatedDynamic) {
                    var otherMass = Mathf.Max(hit.OtherCharacterBody.Settings.SimulatedCharacterMass, 0.01f);
                    var selfMass = Mathf.Max(_settings.SimulatedCharacterMass, 0.01f);
                    var counterCorrection = -correction * (selfMass / otherMass);
                    hit.OtherCharacterBody.ReceiveExternalVelocityInfluence(counterCorrection, hit.StableOnHit);
                }
            }

            if (accumulatedCorrection.sqrMagnitude > 0f) {
                worldVelocity += Vector3.ClampMagnitude(accumulatedCorrection, _settings.MaxPushVelocityChange);
            }
        }

        internal void ReceiveExternalVelocityInfluence(Vector3 velocityChange, bool stableContact) {
            if (stableContact) {
                velocityChange = Vector3.ProjectOnPlane(velocityChange, _basis.Up);
            }

            _velocity.Linear += velocityChange;
        }

        private void ApplyDeferredRigidbodyInteractions() {
            if (!_settings.EnableRigidbodyInteraction ||
                _settings.RigidbodyInteractionMode == CharacterBodyRigidbodyInteractionMode.None) {
                return;
            }

            foreach (var hit in Context.RigidbodyHits) {
                if (hit.Rigidbody == null || hit.Rigidbody.isKinematic ||
                    !Context.PushedRigidbodies.Add(hit.Rigidbody)) {
                    continue;
                }

                var pushDirection = Vector3.ProjectOnPlane(-hit.HitNormal, _basis.Up).normalized;
                if (pushDirection.sqrMagnitude <= 0.0001f) {
                    pushDirection = Vector3.ProjectOnPlane(hit.HitVelocity - hit.RigidbodyVelocity, _basis.Up)
                        .normalized;
                }

                if (pushDirection.sqrMagnitude <= 0.0001f) {
                    continue;
                }

                var speed = Vector3.ProjectOnPlane(hit.HitVelocity - hit.RigidbodyVelocity, _basis.Up).magnitude;
                var velocityChangeMagnitude = Mathf.Min(speed, _settings.MaxPushVelocityChange);
                if (_settings.RigidbodyInteractionMode == CharacterBodyRigidbodyInteractionMode.SimulatedDynamic) {
                    var selfMass = Mathf.Max(_settings.SimulatedCharacterMass, 0.01f);
                    var otherMass = Mathf.Max(hit.Rigidbody.mass, 0.01f);
                    velocityChangeMagnitude *= Mathf.Clamp01(selfMass / otherMass);
                }

                var velocityChange = pushDirection * velocityChangeMagnitude;
                if (hit.StableOnHit) {
                    velocityChange = Vector3.ProjectOnPlane(velocityChange, _basis.Up);
                }

                hit.Rigidbody.AddForceAtPosition(velocityChange, hit.HitPoint, ForceMode.VelocityChange);
            }
        }
    }
}