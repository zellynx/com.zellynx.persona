using UnityEngine;

namespace Character.Mobility
{
    internal static class MobilityMath
    {
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
        {
            return vector - Vector3.Project(vector, planeNormal);
        }

        public static float GetSlopeAngle(Vector3 normal, Vector3 up)
        {
            return Vector3.Angle(normal, up);
        }

        public static bool IsStableNormal(Vector3 normal, Vector3 up, float slopeLimit)
        {
            return Vector3.Angle(normal, up) <= slopeLimit;
        }

        public static Quaternion ResolveRotation(Quaternion current, Quaternion target, bool slerp)
        {
            return slerp ? Quaternion.Slerp(current, target, 1f) : target;
        }

        /// <summary>
        /// Constrains a rotation to remain upright relative to the given up direction:
        /// only yaw around up is preserved; pitch and roll are removed.
        /// </summary>
        public static Quaternion ConstrainToUprightRotation(Quaternion rotation, Vector3 up)
        {
            var normalizedUp = up.sqrMagnitude > 0f ? up.normalized : Vector3.up;
            var forward = ProjectOnPlane(rotation * Vector3.forward, normalizedUp);
            if (forward.sqrMagnitude < 0.0001f)
            {
                // Orientation forward is (anti)parallel to up: fall back to any stable planar axis.
                GetPlanarAxes(normalizedUp, out forward, out _);
            }

            return Quaternion.LookRotation(forward.normalized, normalizedUp);
        }

        public static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        public static Vector3 ScaleByAbsolute(Vector3 value, Vector3 scale)
        {
            return Vector3.Scale(value, Abs(scale));
        }

        public static float MaxComponent(Vector3 value)
        {
            return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
        }

        public static Vector3 ClampMin(Vector3 value, float minValue)
        {
            return new Vector3(
                Mathf.Max(minValue, value.x),
                Mathf.Max(minValue, value.y),
                Mathf.Max(minValue, value.z));
        }

        public static void GetPlanarAxes(Vector3 up, out Vector3 tangentA, out Vector3 tangentB) {
            tangentA = Vector3.Cross(up, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.001f) {
                tangentA = Vector3.Cross(up, Vector3.right);
            }

            tangentA.Normalize();
            tangentB = Vector3.Cross(up, tangentA).normalized;
        }
    }
}
