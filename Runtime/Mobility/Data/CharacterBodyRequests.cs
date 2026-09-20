namespace Character.Mobility
{
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

    /// <summary>
    /// Requests CharacterBody to simulate using the supplied physical velocity
    /// for one simulation step.
    ///
    /// The requested velocity is not necessarily the resulting velocity.
    /// CharacterBody resolves the request against its physical simulation.
    /// </summary>
    public struct VelocityUpdateRequest
    {
        public Velocity Velocity;
        public float DeltaTime;
    }
}
