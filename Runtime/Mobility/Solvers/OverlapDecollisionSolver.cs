using UnityEngine;

namespace Character.Mobility
{
    internal static class OverlapDecollisionSolver
    {
        public static void ResolveOverlaps(CharacterBody body, ref Vector3 position, Quaternion rotation)
        {
            Collider shape = body.ActiveCollider;
            int count = MobilityPhysics.OverlapNonAlloc(shape, position, rotation, 0f,
                body.Context.OverlapResults, body.Settings.CollidableLayers, QueryTriggerInteraction.Ignore);

            for (int iteration = 0; iteration < body.Settings.MaxDecollisionIterations; iteration++)
            {
                bool moved = false;

                for (int i = 0; i < count; i++)
                {
                    Collider other = body.Context.OverlapResults[i];
                    if (other == null || other == body.ActiveCollider || !body.ShouldCollideWith(other))
                    {
                        continue;
                    }

                    if (MobilityPhysics.ComputePenetration(body.ActiveCollider, position, rotation, other,
                            other.transform.position, other.transform.rotation, out var direction, out var distance)) {
                        position += direction * (distance + body.Settings.SkinWidth);
                        moved = true;

                        Rigidbody otherBody = other.attachedRigidbody;
                        if (body.Settings.EnableRigidbodyInteraction && otherBody != null && !otherBody.isKinematic)
                        {
                            Vector3 hitPoint = MobilityPhysics.GetSupportPoint(shape, position, rotation, -direction);
                            CharacterBody otherCharacterBody = otherBody.GetComponent<CharacterBody>();
                            CharacterRigidbodyHit rigidbodyHit = new CharacterRigidbodyHit
                            {
                                Rigidbody = otherBody,
                                OtherCharacterBody = otherCharacterBody,
                                HitPoint = hitPoint,
                                HitNormal = direction,
                                HitVelocity = body.Velocity.Linear,
                                RigidbodyVelocity = otherBody.GetPointVelocity(hitPoint),
                                StableOnHit = Vector3.Dot(direction, body.Orientation.Up) > 0.5f,
                            };
                            if (body.ModifyRigidbodyHit != null)
                            {
                                rigidbodyHit = body.ModifyRigidbodyHit.Invoke(rigidbodyHit);
                            }

                            body.Context.RigidbodyHits.Add(rigidbodyHit);
                        }
                    }
                }

                if (!moved)
                {
                    break;
                }

                count = MobilityPhysics.OverlapNonAlloc(shape, position, rotation,0f,
                    body.Context.OverlapResults, body.Settings.CollidableLayers, QueryTriggerInteraction.Ignore);
            }
        }
    }
}
