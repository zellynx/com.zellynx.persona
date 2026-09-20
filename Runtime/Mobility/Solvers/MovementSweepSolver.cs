using UnityEngine;

namespace Character.Mobility
{
    internal static class MovementSweepSolver
    {
        public static CharacterBodyCollisionFlags MoveWithCollisions(
            CharacterBody body,
            ref Vector3 position,
            Quaternion rotation,
            ref Vector3 worldVelocity,
            float deltaTime,
            ref CharacterGroundingReport groundingReport)
        {
            CharacterBodyCollisionFlags flags = CharacterBodyCollisionFlags.None;
            Vector3 remaining = worldVelocity * deltaTime;
            Vector3 up = body.Basis.Up.normalized;
            bool wasStableOnGround = groundingReport.IsStableOnGround;
            bool allowLedgeSnap = body.Settings.StepAndSlopeSettings.MaxVelocityForLedgeSnap <= 0f
                || Vector3.ProjectOnPlane(worldVelocity, up).magnitude <= body.Settings.StepAndSlopeSettings.MaxVelocityForLedgeSnap;
            bool hasPreviousObstruction = false;
            Vector3 previousObstructionNormal = Vector3.zero;

            for (int iteration = 0; iteration < body.Settings.MaxMovementIterations; iteration++)
            {
                float distance = remaining.magnitude;
                if (distance <= body.Settings.MinMoveDistance)
                {
                    break;
                }

                if (!CharacterPhysicsQueries.Cast(body.ActiveCollider, position, rotation, body.Settings.SkinWidth,
                        remaining.normalized, distance + body.Settings.SkinWidth, body.Settings.CollidableLayers,
                        QueryTriggerInteraction.Ignore, body.Context.SweepResults, body.ShouldCollideWith,
                        out RaycastHit hit))
                {
                    position += remaining;
                    remaining = Vector3.zero;
                    break;
                }

                float moveDistance = Mathf.Max(0f, hit.distance - body.Settings.SkinWidth);
                if (moveDistance > 0f)
                {
                    position += remaining.normalized * moveDistance;
                }

                HitStabilityReport stability = body.EvaluateHitStability(hit.normal, hit.collider, position, rotation, hit.point, allowLedgeSnap);
                body.ReportCollision(hit, stability, false);

                if (StepAndSlopeSolver.TryStepUp(body, position, rotation, remaining, hit, wasStableOnGround, allowLedgeSnap, out Vector3 steppedPosition, out CharacterGroundingReport steppedGroundingReport))
                {
                    position = steppedPosition;
                    groundingReport = steppedGroundingReport;
                    flags |= CharacterBodyCollisionFlags.Below;
                    worldVelocity = VelocityProjectionSolver.ProjectForGrounding(worldVelocity, groundingReport.GroundNormal, body.Basis.Up);
                    remaining = worldVelocity * deltaTime;
                    continue;
                }

                float upDot = Vector3.Dot(hit.normal, up);
                if (upDot > 0.5f)
                {
                    flags |= CharacterBodyCollisionFlags.Below;
                    body.SetGroundingFromHit(ref groundingReport, hit, stability);
                }
                else if (upDot < -0.5f)
                {
                    flags |= CharacterBodyCollisionFlags.Above;
                }
                else
                {
                    flags |= CharacterBodyCollisionFlags.Sides;
                }

                if (hasPreviousObstruction)
                {
                    worldVelocity = VelocityProjectionSolver.ProjectOnHits(worldVelocity, previousObstructionNormal, hit.normal, up);
                }
                else
                {
                    worldVelocity = VelocityProjectionSolver.ProjectOnHit(worldVelocity, hit.normal);
                    hasPreviousObstruction = true;
                    previousObstructionNormal = hit.normal;
                }

                if (groundingReport.IsStableOnGround && body.Settings.StepAndSlopeSettings.EnableFutureSlopeUngrounding)
                {
                    Vector3 alongGround = Vector3.ProjectOnPlane(worldVelocity, up);
                    if (alongGround.sqrMagnitude > 0.0001f)
                    {
                        Vector3 probePosition = position + alongGround.normalized * Mathf.Max(body.Settings.SkinWidth * 2f, 0.05f);
                        CharacterGroundingReport futureGround = GroundQuerySolver.ProbeGround(body, probePosition, rotation, body.Settings.StepAndSlopeSettings.GroundDetectionExtraDistance, allowLedgeSnap);
                        if (futureGround.FoundAnyGround && futureGround.IsStableOnGround)
                        {
                            float denivelation = Vector3.Angle(groundingReport.GroundNormal, futureGround.GroundNormal);
                            if (denivelation > body.Settings.StepAndSlopeSettings.MaxStableDenivelationAngle)
                            {
                                groundingReport.SnappingPrevented = true;
                                groundingReport.IsStableOnGround = false;
                            }
                        }
                    }
                }

                remaining = worldVelocity * deltaTime;
            }

            return flags;
        }
    }
}
