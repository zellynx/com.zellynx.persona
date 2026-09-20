using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    internal static class GroundQuerySolver
    {
        public static CharacterGroundingReport GetDefaultReport(CharacterBody body)
        {
            CharacterGroundingReport report = default;
            report.GroundNormal = body.Orientation.Up;
            report.InnerGroundNormal = body.Orientation.Up;
            report.OuterGroundNormal = body.Orientation.Up;
            report.SnappingPrevented = body.IsForceUngrounded;
            return report;
        }

        public static CharacterGroundingReport ProbeGround(CharacterBody body, Vector3 position, Quaternion rotation, float extraDistance) {
            return ProbeGround(body, position, rotation, extraDistance, true, true);
        }

        public static CharacterGroundingReport ProbeGround(CharacterBody body, Vector3 position, Quaternion rotation, float extraDistance, bool allowLedgeSnap) {
            return ProbeGround(body, position, rotation, extraDistance, allowLedgeSnap, true);
        }

        public static CharacterGroundingReport ProbeGround(CharacterBody body, Vector3 position, Quaternion rotation, float extraDistance, bool allowLedgeSnap, bool reportCollision) {
            CharacterGroundingReport report = GetDefaultReport(body);
            return body.GeometryType == GeometryType.Box ?
                ProbeBoxGround(body, position, rotation, extraDistance, allowLedgeSnap, reportCollision, ref report)
                : ProbeRoundedGround(body, position, rotation, extraDistance, allowLedgeSnap, reportCollision, ref report);

        }

        private static CharacterGroundingReport ProbeRoundedGround(CharacterBody body, Vector3 position, Quaternion rotation, float extraDistance,
            bool allowLedgeSnap, bool reportCollision, ref CharacterGroundingReport report) {
            Vector3 up = body.Orientation.Up.normalized;
            Vector3 down = -up;
            float castDistance = Mathf.Max(body.Settings.SkinWidth + extraDistance, body.Settings.SkinWidth + 0.02f);
            Collider shape = body.ActiveCollider;

            bool foundHit = false;
            RaycastHit bestHit = default;
            HitStabilityReport bestStability = default;

            TryUpdateRoundedBestHit(body, position + (up * 0.02f), rotation, down, castDistance + 0.02f,
                position, rotation, allowLedgeSnap, ref foundHit, ref bestHit, ref bestStability);

            MobilityMath.GetPlanarAxes(up, out Vector3 tangentA, out Vector3 tangentB);
            Vector3 worldCenter = MobilityPhysics.GetWorldCenter(shape, position, rotation);
            float lateralOffsetA = MobilityPhysics.GetSupportDistance(shape, position, rotation, tangentA) * 0.5f;
            float lateralOffsetB = MobilityPhysics.GetSupportDistance(shape, position, rotation, tangentB) * 0.5f;
            TryUpdateRayBestHit(body, worldCenter + (tangentA * lateralOffsetA) + (up * 0.05f), down, castDistance + 0.05f,
                position, rotation, allowLedgeSnap, ref foundHit, ref bestHit, ref bestStability);
            TryUpdateRayBestHit(body, worldCenter - (tangentA * lateralOffsetA) + (up * 0.05f), down, castDistance + 0.05f,
                position, rotation, allowLedgeSnap, ref foundHit, ref bestHit, ref bestStability);
            TryUpdateRayBestHit(body, worldCenter + (tangentB * lateralOffsetB) + (up * 0.05f), down, castDistance + 0.05f,
                position, rotation, allowLedgeSnap, ref foundHit, ref bestHit, ref bestStability);
            TryUpdateRayBestHit(body, worldCenter - (tangentB * lateralOffsetB) + (up * 0.05f), down, castDistance + 0.05f,
                position, rotation, allowLedgeSnap, ref foundHit, ref bestHit, ref bestStability);

            if (foundHit)
            {
                body.SetGroundingFromHit(
                    ref report,
                    bestHit,
                    bestStability);

                if (reportCollision)
                {
                    body.ReportCollision(
                        bestHit,
                        bestStability,
                        true);
                }
            }

            return report;
        }

        private static CharacterGroundingReport ProbeBoxGround(CharacterBody body, Vector3 position, Quaternion rotation, float extraDistance,
            bool allowLedgeSnap, bool reportCollision, ref CharacterGroundingReport report) {
            Vector3 up = body.Orientation.Up.normalized;
            Vector3 down = -up;
            float castDistance = Mathf.Max(body.Settings.SkinWidth + extraDistance, body.Settings.SkinWidth + 0.02f);

            bool foundHit = false;
            RaycastHit bestHit = default;
            HitStabilityReport bestStability = default;

            if (MobilityPhysics.Cast(body.ActiveCollider, position + (up * 0.02f), rotation, body.Settings.SkinWidth,
                    down, castDistance + 0.02f, body.Settings.CollidableLayers, QueryTriggerInteraction.Ignore,
                    body.Context.SweepResults, body.ShouldCollideWith, out RaycastHit primaryHit))
            {
                bestStability = body.EvaluateHitStability(primaryHit.normal, primaryHit.collider, position, rotation, primaryHit.point, allowLedgeSnap);
                bestHit = primaryHit;
                foundHit = true;
            }

            MobilityPhysics.GetBoxWorldData((BoxCollider)body.ActiveCollider, position, rotation, out var worldCenter,
                out var boxRotation, out var halfExtents);
            float supportInset =
                Mathf.Max(0.01f, MobilityPhysics.GetMinimalPlanarSupportDistance(body.ActiveCollider, position, rotation, up) * 0.25f);
            Vector3[] localOffsets =
            {
                Vector3.down * halfExtents.y,
                new Vector3(halfExtents.x - supportInset, -halfExtents.y, halfExtents.z - supportInset),
                new Vector3(-(halfExtents.x - supportInset), -halfExtents.y, halfExtents.z - supportInset),
                new Vector3(halfExtents.x - supportInset, -halfExtents.y, -(halfExtents.z - supportInset)),
                new Vector3(-(halfExtents.x - supportInset), -halfExtents.y, -(halfExtents.z - supportInset)),
            };

            for (int i = 0; i < localOffsets.Length; i++)
            {
                Vector3 origin = worldCenter + (boxRotation * localOffsets[i]) + (up * 0.05f);
                if (!MobilityPhysics.Raycast(origin, down, castDistance + 0.05f, body.Settings.CollidableLayers,
                        QueryTriggerInteraction.Ignore, body.Context.SweepResults, body.ShouldCollideWith,
                        out RaycastHit supportHit))
                {
                    continue;
                }

                HitStabilityReport stability = body.EvaluateHitStability(supportHit.normal, supportHit.collider, position, rotation, supportHit.point, allowLedgeSnap);
                if (!foundHit || (stability.IsStable && !bestStability.IsStable) || supportHit.distance < bestHit.distance)
                {
                    foundHit = true;
                    bestHit = supportHit;
                    bestStability = stability;
                }
            }

            if (foundHit)
            {
                body.SetGroundingFromHit(
                    ref report,
                    bestHit,
                    bestStability);

                if (reportCollision)
                {
                    body.ReportCollision(
                        bestHit,
                        bestStability,
                        true);
                }
            }

            return report;
        }

        private static void TryUpdateRoundedBestHit(
            CharacterBody body,
            Vector3 castPosition,
            Quaternion castRotation,
            Vector3 direction,
            float distance,
            Vector3 stabilityPosition,
            Quaternion stabilityRotation,
            bool allowLedgeSnap,
            ref bool foundHit,
            ref RaycastHit bestHit,
            ref HitStabilityReport bestStability)
        {
            if (!MobilityPhysics.Cast(body.ActiveCollider, castPosition, castRotation, body.Settings.SkinWidth, direction,
                    distance, body.Settings.CollidableLayers, QueryTriggerInteraction.Ignore,
                    body.Context.SweepResults, body.ShouldCollideWith, out RaycastHit hit))
            {
                return;
            }

            HitStabilityReport stability = body.EvaluateHitStability(hit.normal, hit.collider, stabilityPosition, stabilityRotation, hit.point, allowLedgeSnap);
            if (!foundHit || hit.distance < bestHit.distance || (stability.IsStable && !bestStability.IsStable))
            {
                foundHit = true;
                bestHit = hit;
                bestStability = stability;
            }
        }

        private static void TryUpdateRayBestHit(
            CharacterBody body,
            Vector3 origin,
            Vector3 direction,
            float distance,
            Vector3 stabilityPosition,
            Quaternion stabilityRotation,
            bool allowLedgeSnap,
            ref bool foundHit,
            ref RaycastHit bestHit,
            ref HitStabilityReport bestStability)
        {
            if (!MobilityPhysics.Raycast(origin, direction, distance, body.Settings.CollidableLayers,
                    QueryTriggerInteraction.Ignore, body.Context.SweepResults, body.ShouldCollideWith,
                    out RaycastHit hit))
            {
                return;
            }

            HitStabilityReport stability = body.EvaluateHitStability(hit.normal, hit.collider, stabilityPosition, stabilityRotation, hit.point, allowLedgeSnap);
            if (!foundHit || hit.distance < bestHit.distance || (stability.IsStable && !bestStability.IsStable))
            {
                foundHit = true;
                bestHit = hit;
                bestStability = stability;
            }
        }

        public static bool TrySnapToGround(
            CharacterBody body,
            ref Vector3 position,
            Quaternion rotation,
            float snapDistance,
            bool allowLedgeSnap,
            out CharacterGroundingReport groundingReport)
        {
            groundingReport =
                GetDefaultReport(body);

            if (snapDistance <= 0f ||
                body.IsForceUngrounded)
            {
                return false;
            }

            Vector3 up =
                body.Orientation.Up.normalized;

            Vector3 down =
                -up;

            float castDistance =
                snapDistance +
                body.Settings.SkinWidth;

            if (!MobilityPhysics.Cast(
                    body.ActiveCollider,
                    position,
                    rotation,
                    body.Settings.SkinWidth,
                    down,
                    castDistance,
                    body.Settings.CollidableLayers,
                    QueryTriggerInteraction.Ignore,
                    body.Context.SweepResults,
                    body.ShouldCollideWith,
                    out RaycastHit hit))
            {
                return false;
            }

            HitStabilityReport stability =
                body.EvaluateHitStability(
                    hit.normal,
                    hit.collider,
                    position,
                    rotation,
                    hit.point,
                    allowLedgeSnap);

            if (!stability.IsStable)
            {
                return false;
            }

            float snapMoveDistance =
                hit.distance -
                body.Settings.SkinWidth;

            if (snapMoveDistance < 0f ||
                snapMoveDistance > snapDistance)
            {
                return false;
            }

            position +=
                down *
                snapMoveDistance;

            body.SetGroundingFromHit(
                ref groundingReport,
                hit,
                stability);

            groundingReport.SnappingPrevented = false;

            return true;
        }
    }
}
