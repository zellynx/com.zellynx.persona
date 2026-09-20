using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Complete world-space physical pose.
    /// </summary>
    public struct Pose
    {
        /// <summary>
        /// World-space position.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// World-space orientation.
        /// </summary>
        public Quaternion Orientation;
    }
    
    /// <summary>
    /// Requests CharacterBody to perform a collision-aware simulation toward
    /// the supplied target pose.
    ///
    /// This is not Teleport.
    /// </summary>
    public struct PoseUpdateRequest
    {
        public Pose Pose;
        public float DeltaTime;
    }
}