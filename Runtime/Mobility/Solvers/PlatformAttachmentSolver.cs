using UnityEngine;

namespace Character.Mobility
{
    internal static class PlatformAttachmentSolver
    {
        public static Vector3 GetInheritedVelocity(CharacterBody body, CharacterBodyAnchor anchor, Vector3 worldPoint, float deltaTime)
        {
            if (anchor == null)
            {
                return Vector3.zero;
            }

            return anchor.CalculatePointVelocity(worldPoint, deltaTime);
        }

        public static CharacterBodyAnchor ResolveAnchor(CharacterGroundingReport groundingReport)
        {
            if (groundingReport.GroundCollider == null)
            {
                return null;
            }

            return groundingReport.GroundCollider.GetComponentInParent<CharacterBodyAnchor>();
        }
    }
}
