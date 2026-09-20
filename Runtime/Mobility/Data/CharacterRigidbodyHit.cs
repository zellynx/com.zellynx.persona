using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Mutable interaction data for a character contact against a rigidbody or another character body.
    /// </summary>
    public struct CharacterRigidbodyHit
    {
        public Rigidbody Rigidbody;
        public CharacterBody OtherCharacterBody;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public Vector3 HitVelocity;
        public Vector3 RigidbodyVelocity;
        public bool StableOnHit;
    }
}
