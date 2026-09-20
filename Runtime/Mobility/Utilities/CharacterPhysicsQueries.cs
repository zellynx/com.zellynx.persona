using System;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    /// <summary>
    /// Translates the body's actual Unity collider into parameters for Unity physics queries
    /// and shape support calculations. Owns no state; the Unity collider remains the single
    /// physical source of truth.
    /// CharacterBody uses an upright Y-axis CapsuleCollider: the LowLevelPhysics CapsuleGeometry
    /// supplies Radius/HalfLength, while the Y orientation comes from the CharacterBody invariant.
    /// </summary>
    internal static class CharacterPhysicsQueries
    {
        // ------------------------------------------------------------------
        // LowLevelPhysics geometry access
        // ------------------------------------------------------------------

        public static CapsuleGeometry GetCapsuleGeometry(CapsuleCollider capsule) {
            return capsule.GetGeometry<CapsuleGeometry>();
        }

        public static SphereGeometry GetSphereGeometry(SphereCollider sphere) {
            return sphere.GetGeometry<SphereGeometry>();
        }

        public static BoxGeometry GetBoxGeometry(BoxCollider box) {
            return box.GetGeometry<BoxGeometry>();
        }

        // ------------------------------------------------------------------
        // World-space shape data
        // ------------------------------------------------------------------

        /// <summary>
        /// Converts an actual CapsuleCollider plus body pose into the endpoint/radius
        /// representation required by CapsuleCast/OverlapCapsule.
        /// Combines CapsuleGeometry with the Y-axis capsule invariant, collider center,
        /// body pose, and scale.
        /// </summary>
        public static void GetCapsuleWorldData(CapsuleCollider capsule, Vector3 bodyPosition, Quaternion bodyRotation,
            out Vector3 point1, out Vector3 point2, out float radius) {
            GetCapsuleWorldParts(capsule, bodyPosition, bodyRotation, out var worldCenter, out var worldUpAxis,
                out radius, out var cylinderHalf);

            point1 = worldCenter + worldUpAxis * cylinderHalf;
            point2 = worldCenter - worldUpAxis * cylinderHalf;
        }

        private static void GetCapsuleWorldParts(CapsuleCollider capsule, Vector3 bodyPosition, Quaternion bodyRotation,
            out Vector3 worldCenter, out Vector3 axis, out float radius, out float cylinderHalf) {
            var absoluteScale = MobilityMath.Abs(capsule.transform.lossyScale);
            var geometry = GetCapsuleGeometry(capsule);

            // Y-only capsule invariant: radial size scales with X/Z, axial size scales with Y.
            radius = Mathf.Max(0.0001f, geometry.Radius * Mathf.Max(absoluteScale.x, absoluteScale.z));
            cylinderHalf = Mathf.Max(0f, geometry.HalfLength * absoluteScale.y);
            worldCenter = GetScaledCenter(capsule.center, absoluteScale, bodyPosition, bodyRotation);
            axis = bodyRotation * Vector3.up;
        }

        public static void GetSphereWorldData(SphereCollider sphere, Vector3 bodyPosition, Quaternion bodyRotation,
            out Vector3 center, out float radius) {
            var absoluteScale = MobilityMath.Abs(sphere.transform.lossyScale);
            var geometry = GetSphereGeometry(sphere);

            center = GetScaledCenter(sphere.center, absoluteScale, bodyPosition, bodyRotation);
            radius = Mathf.Max(0.0001f, geometry.Radius * MobilityMath.MaxComponent(absoluteScale));
        }

        public static void GetBoxWorldData(BoxCollider box, Vector3 bodyPosition, Quaternion bodyRotation,
            out Vector3 center, out Quaternion rotation, out Vector3 halfExtents) {
            var absoluteScale = MobilityMath.Abs(box.transform.lossyScale);

            center = GetScaledCenter(box.center, absoluteScale, bodyPosition, bodyRotation);
            rotation = bodyRotation;
            halfExtents = MobilityMath.ClampMin(MobilityMath.ScaleByAbsolute(GetBoxGeometry(box).HalfExtents, absoluteScale), 0.0001f);
        }

        public static Vector3 GetWorldCenter(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation) {
            var absoluteScale = MobilityMath.Abs(collider.transform.lossyScale);
            return GetScaledCenter(GetLocalCenter(collider), absoluteScale, bodyPosition, bodyRotation);
        }

        private static Vector3 GetLocalCenter(Collider collider) {
            return collider switch {
                CapsuleCollider capsule => capsule.center,
                SphereCollider sphere => sphere.center,
                BoxCollider box => box.center,
                _ => Vector3.zero
            };
        }

        private static Vector3 GetScaledCenter(Vector3 localCenter, Vector3 absoluteScale,
            Vector3 bodyPosition, Quaternion bodyRotation) {
            return bodyPosition + bodyRotation * MobilityMath.ScaleByAbsolute(localCenter, absoluteScale);
        }

        // ------------------------------------------------------------------
        // Skin-inflated query data
        // ------------------------------------------------------------------

        private static void GetCapsuleQueryData(CapsuleCollider capsule, Vector3 bodyPosition, Quaternion bodyRotation,
            float inset, out Vector3 point1, out Vector3 point2, out float radius) {
            GetCapsuleWorldParts(capsule, bodyPosition, bodyRotation, out var worldCenter, out var axis,
                out var worldRadius, out var worldCylinderHalf);

            // Solver skin behavior: the capsule erodes radially by the inset while the
            // endcap centers keep their full separation, so casts/overlaps/checks all see
            // the same configuration-space reduction without changing axial geometry.
            radius = Mathf.Max(0.001f, worldRadius - inset);
            point1 = worldCenter + axis * worldCylinderHalf;
            point2 = worldCenter - axis * worldCylinderHalf;
        }

        private static void GetSphereQueryData(SphereCollider sphere, Vector3 bodyPosition, Quaternion bodyRotation,
            float inset, out Vector3 center, out float radius) {
            GetSphereWorldData(sphere, bodyPosition, bodyRotation, out center, out var worldRadius);
            radius = Mathf.Max(0.001f, worldRadius - inset);
        }

        private static void GetBoxQueryData(BoxCollider box, Vector3 bodyPosition, Quaternion bodyRotation,
            float inset, out Vector3 center, out Quaternion rotation, out Vector3 halfExtents) {
            GetBoxWorldData(box, bodyPosition, bodyRotation, out center, out rotation, out var worldHalfExtents);
            halfExtents = MobilityMath.ClampMin(worldHalfExtents - Vector3.one * inset, 0.001f);
        }

        // ------------------------------------------------------------------
        // Shape casts
        // ------------------------------------------------------------------

        /// <summary>
        /// Casts the shape and returns the nearest hit whose collider passes the supplied
        /// filter, behaving as though rejected colliders do not exist. When the filter is
        /// null, the nearest physics-query hit wins. The result buffer must be supplied by
        /// the caller and is not required to be sorted or empty.
        /// </summary>
        public static bool Cast(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation, float inset,
            Vector3 direction, float distance, LayerMask layerMask, QueryTriggerInteraction triggerInteraction,
            RaycastHit[] results, Func<Collider, bool> filter, out RaycastHit hit) {
            if (collider == null || results == null || results.Length == 0 || direction.sqrMagnitude <= Mathf.Epsilon) {
                hit = default;
                return false;
            }

            var normalizedDirection = direction.normalized;
            int count;
            switch (collider) {
                case CapsuleCollider capsule:
                    GetCapsuleQueryData(capsule, bodyPosition, bodyRotation, inset, out var p1, out var p2, out var capRadius);
                    count = Physics.CapsuleCastNonAlloc(p1, p2, capRadius, normalizedDirection, results, distance,
                        layerMask, triggerInteraction);
                    break;

                case SphereCollider sphere:
                    GetSphereQueryData(sphere, bodyPosition, bodyRotation, inset, out var sphereCenter, out var sphereRadius);
                    count = Physics.SphereCastNonAlloc(sphereCenter, sphereRadius, normalizedDirection, results,
                        distance, layerMask, triggerInteraction);
                    break;

                case BoxCollider box:
                    GetBoxQueryData(box, bodyPosition, bodyRotation, inset, out var boxCenter, out var boxRotation,
                        out var halfExtents);
                    count = Physics.BoxCastNonAlloc(boxCenter, halfExtents, normalizedDirection, results, boxRotation,
                        distance, layerMask, triggerInteraction);
                    break;

                default:
                    count = 0;
                    break;
            }

            return SelectNearestValidHit(results, count, filter, out hit);
        }

        /// <summary>
        /// Raycasts and returns the nearest hit whose collider passes the supplied filter,
        /// behaving as though rejected colliders do not exist. When the filter is null, the
        /// nearest physics-query hit wins. The result buffer must be supplied by the caller.
        /// </summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, float distance, LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction, RaycastHit[] results, Func<Collider, bool> filter,
            out RaycastHit hit) {
            if (results == null || results.Length == 0 || direction.sqrMagnitude <= Mathf.Epsilon) {
                hit = default;
                return false;
            }

            int count = Physics.RaycastNonAlloc(origin, direction.normalized, results, distance, layerMask,
                triggerInteraction);
            return SelectNearestValidHit(results, count, filter, out hit);
        }

        private static bool SelectNearestValidHit(RaycastHit[] results, int count,
            Func<Collider, bool> filter, out RaycastHit hit) {
            var found = false;
            hit = default;

            // NonAlloc output ordering is not guaranteed: explicitly compare distances.
            for (int i = 0; i < count; i++) {
                var candidate = results[i];
                if (candidate.collider == null) {
                    continue;
                }

                if (filter != null && !filter(candidate.collider)) {
                    continue;
                }

                if (!found || candidate.distance < hit.distance) {
                    hit = candidate;
                    found = true;
                }
            }

            return found;
        }

        // ------------------------------------------------------------------
        // Overlaps
        // ------------------------------------------------------------------

        public static int OverlapNonAlloc(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation, float inset,
            Collider[] results, LayerMask layerMask, QueryTriggerInteraction triggerInteraction) {
            if (collider == null) {
                return 0;
            }

            switch (collider) {
                case CapsuleCollider capsule:
                    GetCapsuleQueryData(capsule, bodyPosition, bodyRotation, inset, out var op1, out var op2, out var oCapRadius);
                    return Physics.OverlapCapsuleNonAlloc(op1, op2, oCapRadius, results, layerMask, triggerInteraction);

                case SphereCollider sphere:
                    GetSphereQueryData(sphere, bodyPosition, bodyRotation, inset, out var oSphereCenter, out var oSphereRadius);
                    return Physics.OverlapSphereNonAlloc(oSphereCenter, oSphereRadius, results, layerMask, triggerInteraction);

                case BoxCollider box:
                    GetBoxQueryData(box, bodyPosition, bodyRotation, inset, out var oBoxCenter, out var oBoxRotation,
                        out var oHalfExtents);
                    return Physics.OverlapBoxNonAlloc(oBoxCenter, oHalfExtents, results, oBoxRotation, layerMask,
                        triggerInteraction);

                default:
                    return 0;
            }
        }

        // ------------------------------------------------------------------
        // Checks
        // ------------------------------------------------------------------

        /// <summary>
        /// Reports whether any collider accepted by the supplied filter overlaps the shape.
        /// With a null filter this uses Unity's native boolean Check APIs; with a custom
        /// filter it discovers candidates through the corresponding Overlap*NonAlloc query
        /// into the caller-owned buffer and returns true on the first accepted collider,
        /// behaving as though rejected colliders do not exist.
        /// </summary>
        public static bool Check(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation, float inset,
            LayerMask layerMask, QueryTriggerInteraction triggerInteraction, Collider[] results,
            Func<Collider, bool> filter) {
            if (collider == null) {
                return false;
            }

            if (filter == null) {
                switch (collider) {
                    case CapsuleCollider capsule:
                        GetCapsuleQueryData(capsule, bodyPosition, bodyRotation, inset, out var p1, out var p2, out var capRadius);
                        return Physics.CheckCapsule(p1, p2, capRadius, layerMask, triggerInteraction);

                    case SphereCollider sphere:
                        GetSphereQueryData(sphere, bodyPosition, bodyRotation, inset, out var sphereCenter, out var sphereRadius);
                        return Physics.CheckSphere(sphereCenter, sphereRadius, layerMask, triggerInteraction);

                    case BoxCollider box:
                        GetBoxQueryData(box, bodyPosition, bodyRotation, inset, out var boxCenter, out var boxRotation,
                            out var halfExtents);
                        return Physics.CheckBox(boxCenter, halfExtents, boxRotation, layerMask, triggerInteraction);

                    default:
                        return false;
                }
            }

            if (results == null || results.Length == 0) {
                return false;
            }

            int count;
            switch (collider) {
                case CapsuleCollider capsule:
                    GetCapsuleQueryData(capsule, bodyPosition, bodyRotation, inset, out var fp1, out var fp2, out var fCapRadius);
                    count = Physics.OverlapCapsuleNonAlloc(fp1, fp2, fCapRadius, results, layerMask, triggerInteraction);
                    break;

                case SphereCollider sphere:
                    GetSphereQueryData(sphere, bodyPosition, bodyRotation, inset, out var fSphereCenter, out var fSphereRadius);
                    count = Physics.OverlapSphereNonAlloc(fSphereCenter, fSphereRadius, results, layerMask, triggerInteraction);
                    break;

                case BoxCollider box:
                    GetBoxQueryData(box, bodyPosition, bodyRotation, inset, out var fBoxCenter, out var fBoxRotation,
                        out var fHalfExtents);
                    count = Physics.OverlapBoxNonAlloc(fBoxCenter, fHalfExtents, results, fBoxRotation, layerMask,
                        triggerInteraction);
                    break;

                default:
                    return false;
            }

            for (int i = 0; i < count; i++) {
                var candidate = results[i];
                if (candidate == null) {
                    continue;
                }

                if (filter(candidate)) {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Penetration resolution
        // ------------------------------------------------------------------

        public static bool ComputePenetration(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation,
            Collider other, Vector3 otherPosition, Quaternion otherRotation,
            out Vector3 direction, out float distance) {
            if (collider == null || other == null) {
                direction = default;
                distance = 0f;
                return false;
            }

            return Physics.ComputePenetration(collider, bodyPosition, bodyRotation, other, otherPosition,
                otherRotation, out direction, out distance);
        }

        // ------------------------------------------------------------------
        // Support math
        // ------------------------------------------------------------------

        public static float GetSupportDistance(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation,
            Vector3 worldDirection) {
            if (collider == null) {
                return 0f;
            }

            var direction = worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.up;
            switch (collider) {
                case CapsuleCollider capsule: {
                    GetCapsuleWorldParts(capsule, bodyPosition, bodyRotation, out _, out var axis,
                        out var radius, out var cylinderHalf);
                    return GetCapsuleSupportDistance(axis, cylinderHalf, radius, direction);
                }

                case SphereCollider sphere:
                    GetSphereWorldData(sphere, bodyPosition, bodyRotation, out _, out var sphereRadius);
                    return sphereRadius;

                case BoxCollider box:
                    GetBoxWorldData(box, bodyPosition, bodyRotation, out _, out var boxRotation, out var halfExtents);
                    return GetBoxSupportDistance(boxRotation, halfExtents, direction);

                default:
                    return 0f;
            }
        }

        public static Vector3 GetSupportPoint(Collider collider, Vector3 bodyPosition, Quaternion bodyRotation,
            Vector3 worldDirection) {
            if (collider == null) {
                return bodyPosition;
            }

            var direction = worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.up;
            switch (collider) {
                case CapsuleCollider capsule: {
                    GetCapsuleWorldParts(capsule, bodyPosition, bodyRotation, out var worldCenter, out var axis,
                        out var radius, out var cylinderHalf);
                    return GetCapsuleSupportPoint(worldCenter, axis, cylinderHalf, radius, direction);
                }

                case SphereCollider sphere:
                    GetSphereWorldData(sphere, bodyPosition, bodyRotation, out var sphereCenter, out var sphereRadius);
                    return sphereCenter + direction * sphereRadius;

                case BoxCollider box:
                    GetBoxWorldData(box, bodyPosition, bodyRotation, out var boxCenter, out var boxRotation,
                        out var halfExtents);
                    return GetBoxSupportPoint(boxCenter, boxRotation, halfExtents, direction);

                default:
                    return bodyPosition;
            }
        }

        /// <summary>
        /// Support extent of the CharacterBody's upright Y-axis capsule in any world-space
        /// query direction: the cylinder half length scaled by alignment with the capsule
        /// axis, plus the radius.
        /// </summary>
        public static float GetCapsuleSupportDistance(Vector3 axis, float cylinderHalf, float radius, Vector3 direction) {
            return cylinderHalf * Mathf.Abs(Vector3.Dot(direction, axis)) + radius;
        }

        public static Vector3 GetCapsuleSupportPoint(Vector3 worldCenter, Vector3 axis, float cylinderHalf,
            float radius, Vector3 direction) {
            var axialDot = Vector3.Dot(direction, axis);
            var axialOffset = axis * (Mathf.Sign(axialDot) * cylinderHalf);

            // Minkowski sum of the cylinder segment and the cap spheres: the support point
            // is the segment end shifted by the query direction scaled by the radius.
            // For a purely axial query direction this yields center + axis*(cylinderHalf + radius),
            // i.e. the tip of the hemisphere, not just the segment end.
            return worldCenter + axialOffset + direction * radius;
        }

        public static float GetBoxSupportDistance(Quaternion rotation, Vector3 halfExtents, Vector3 direction) {
            var local = Quaternion.Inverse(rotation) * direction;
            return halfExtents.x * Mathf.Abs(local.x) + halfExtents.y * Mathf.Abs(local.y) +
                   halfExtents.z * Mathf.Abs(local.z);
        }

        public static Vector3 GetBoxSupportPoint(Vector3 worldCenter, Quaternion rotation, Vector3 halfExtents,
            Vector3 direction) {
            var local = Quaternion.Inverse(rotation) * direction;
            var localPoint = new Vector3(
                local.x >= 0f ? halfExtents.x : -halfExtents.x,
                local.y >= 0f ? halfExtents.y : -halfExtents.y,
                local.z >= 0f ? halfExtents.z : -halfExtents.z);
            return worldCenter + rotation * localPoint;
        }

        /// <summary>
        /// Smallest support extent across the character's planar axes, relative to the
        /// supplied semantic character up direction.
        /// </summary>
        public static float GetMinimalPlanarSupportDistance(Collider collider, Vector3 bodyPosition,
            Quaternion bodyRotation, Vector3 up) {
            MobilityMath.GetPlanarAxes(up, out var tangentA, out var tangentB);
            return Mathf.Min(GetSupportDistance(collider, bodyPosition, bodyRotation, tangentA),
                GetSupportDistance(collider, bodyPosition, bodyRotation, tangentB));
        }
    }
}
