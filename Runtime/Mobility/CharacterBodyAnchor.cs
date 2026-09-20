using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Passive moving-platform helper that exposes pose-derived point velocity.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CharacterBodyAnchor : MonoBehaviour
    {
        private Rigidbody _rigidbody;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _currentPosition;
        private Quaternion _currentRotation;

        /// <summary>
        /// World-space linear velocity computed from the last fixed-step pose delta.
        /// </summary>
        public Vector3 LinearVelocity { get; private set; }

        /// <summary>
        /// World-space angular velocity computed from the last fixed-step pose delta.
        /// </summary>
        public Vector3 AngularVelocity { get; private set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            CaptureCurrentPose();
            _previousPosition = _currentPosition;
            _previousRotation = _currentRotation;
        }

        private void FixedUpdate()
        {
            _previousPosition = _currentPosition;
            _previousRotation = _currentRotation;
            CaptureCurrentPose();

            float deltaTime = Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);
            LinearVelocity = (_currentPosition - _previousPosition) / deltaTime;

            Quaternion delta = _currentRotation * Quaternion.Inverse(_previousRotation);
            delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude <= Mathf.Epsilon)
            {
                AngularVelocity = Vector3.zero;
                return;
            }

            if (angleDegrees > 180f)
            {
                angleDegrees -= 360f;
            }

            AngularVelocity = axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
        }

        /// <summary>
        /// Calculates the velocity at a world-space point from the anchor's translation and rotation.
        /// </summary>
        public Vector3 CalculatePointVelocity(Vector3 worldPoint, float deltaTime)
        {
            deltaTime = Mathf.Max(deltaTime, Mathf.Epsilon);
            Vector3 translationVelocity = (_currentPosition - _previousPosition) / deltaTime;
            Vector3 fromPivot = worldPoint - _previousPosition;
            Vector3 rotatedPoint = _currentPosition + (_currentRotation * Quaternion.Inverse(_previousRotation)) * fromPivot;
            Vector3 rotationalVelocity = (rotatedPoint - worldPoint) / deltaTime;
            return translationVelocity + rotationalVelocity;
        }

        private void CaptureCurrentPose()
        {
            if (_rigidbody != null)
            {
                _currentPosition = _rigidbody.position;
                _currentRotation = _rigidbody.rotation;
            }
            else
            {
                _currentPosition = transform.position;
                _currentRotation = transform.rotation;
            }
        }
    }
}
