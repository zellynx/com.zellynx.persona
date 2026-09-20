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
        /// World-space attitude.
        /// </summary>
        public Quaternion Attitude;
    }
}