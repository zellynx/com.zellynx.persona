using UnityEngine;

namespace Character.Mobility
{
    public struct CharacterBodyState
    {
        public Pose Pose;
        public Velocity Velocity;
        public bool IsGrounded;
        public Vector3 GroundNormal;
        public Vector3 BasisUp;
        public Vector3 BasisForward;
        public float ForceUngroundTimeRemaining;
        public bool WasGrounded;
    }
}
