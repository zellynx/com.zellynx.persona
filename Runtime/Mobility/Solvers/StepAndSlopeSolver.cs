using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    internal static class StepAndSlopeSolver
    {
        public static Vector3 Apply(CharacterBody body, Vector3 characterVelocity, in CharacterGroundingReport groundingReport)
        {
            if (!groundingReport.IsStableOnGround) {
                return characterVelocity;
            }

            return MobilityMath.ProjectVelocityForGrounding(
                characterVelocity,
                groundingReport.GroundNormal,
                body.Orientation.Up
            );
        }

        public static bool TryStepUp(
            CharacterBody body,
            Vector3 currentPosition,
            Quaternion rotation,
            Vector3 movementDirection,
            RaycastHit obstructionHit,
            bool wasStableOnGround,
            bool allowLedgeSnap,
            out Vector3 steppedPosition,
            out CharacterGroundingReport steppedGroundingReport)
        {
            steppedPosition = currentPosition;
            steppedGroundingReport = default;

            if (movementDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            if (!wasStableOnGround && !body.Settings.StepAndSlopeSettings.AllowSteppingWithoutStableGrounding)
            {
                return false;
            }

            Vector3 up = body.Orientation.Up.normalized;
            float stepOffset = body.GetEffectiveStepOffset(currentPosition, rotation);
            if (stepOffset <= 0f)
            {
                return false;
            }

            float obstructionUpDot = Vector3.Dot(obstructionHit.normal, up);
            if (obstructionUpDot > 0.2f)
            {
                return false;
            }

            Vector3 stepUpPosition = currentPosition + (up * (stepOffset + body.SkinWidth));
            if (MobilityPhysics.Check(body.ActiveCollider, stepUpPosition, rotation, body.Settings.SkinWidth,
                    body.Settings.CollidableLayers, QueryTriggerInteraction.Ignore,
                    body.Context.OverlapResults, body.ShouldCollideWith))
            {
                return false;
            }

            float forwardDistanceScalar = body.GeometryType == GeometryType.Box
                ? Mathf.Max(MobilityPhysics.GetSupportDistance(body.ActiveCollider, currentPosition, rotation,
                        Vector3.ProjectOnPlane(movementDirection, up).normalized), body.Settings.SkinWidth * 2f)
                : Mathf.Max(body.Settings.SkinWidth * 2f, 0.03f);
            Vector3 forwardDistance = movementDirection.normalized * forwardDistanceScalar;
            Vector3 stepForwardPosition = stepUpPosition + forwardDistance;
            if (MobilityPhysics.Check(body.ActiveCollider, stepForwardPosition, rotation, body.Settings.SkinWidth,
                    body.Settings.CollidableLayers, QueryTriggerInteraction.Ignore,
                    body.Context.OverlapResults, body.ShouldCollideWith))
            {
                return false;
            }

            steppedGroundingReport = GroundQuerySolver.ProbeGround(body, stepForwardPosition, rotation, stepOffset + body.Settings.StepAndSlopeSettings.GroundDetectionExtraDistance, allowLedgeSnap);
            if (!steppedGroundingReport.FoundAnyGround || !steppedGroundingReport.IsStableOnGround)
            {
                return false;
            }

            steppedPosition = stepForwardPosition;
            Vector3 snapOffset = Vector3.Project(steppedGroundingReport.GroundPoint - stepForwardPosition, -up);
            if (Vector3.Dot(snapOffset, -up) > 0f)
            {
                steppedPosition += snapOffset;
            }

            if (body.GeometryType == GeometryType.Box)
            {
                CharacterGroundingReport validationGround = GroundQuerySolver.ProbeGround(body, steppedPosition, rotation, body.Settings.StepAndSlopeSettings.GroundDetectionExtraDistance, allowLedgeSnap);
                if (!validationGround.IsStableOnGround)
                {
                    return false;
                }

                steppedGroundingReport = validationGround;
            }

            return true;
        }
    }
}
