using UnityEngine;

namespace Character.Mobility
{
    internal static class VelocityProjectionSolver
    {
        public static Vector3 ProjectForGrounding(Vector3 velocity, Vector3 groundNormal, Vector3 up)
        {
            Vector3 tangent = MobilityMath.ProjectOnPlane(velocity, groundNormal);
            if (Vector3.Dot(tangent, up) > 0f)
            {
                tangent = MobilityMath.ProjectOnPlane(tangent, up);
            }

            return tangent;
        }

        public static Vector3 ProjectOnHit(Vector3 velocity, Vector3 hitNormal)
        {
            return MobilityMath.ProjectOnPlane(velocity, hitNormal);
        }

        public static Vector3 ProjectOnHits(Vector3 velocity, Vector3 firstNormal, Vector3 secondNormal, Vector3 up)
        {
            Vector3 creaseDirection = Vector3.Cross(firstNormal, secondNormal);
            if (creaseDirection.sqrMagnitude <= 0.0001f)
            {
                return MobilityMath.ProjectOnPlane(velocity, secondNormal);
            }

            creaseDirection.Normalize();
            if (Mathf.Abs(Vector3.Dot(creaseDirection, up)) > 0.99f)
            {
                return Vector3.zero;
            }

            float magnitudeAlongCrease = Vector3.Dot(velocity, creaseDirection);
            return creaseDirection * magnitudeAlongCrease;
        }
    }
}
