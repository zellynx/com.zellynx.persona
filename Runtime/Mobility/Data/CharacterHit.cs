using UnityEngine;

namespace Character.Mobility
{
    public struct CharacterHit
    {
        public Collider Collider;
        public Rigidbody Rigidbody;
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public bool IsGroundHit;
        public HitStabilityReport StabilityReport;
    }
}
