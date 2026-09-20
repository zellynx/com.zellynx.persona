using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Complete world-space linear and angular velocity.
    /// </summary>
    public struct Velocity
    {
        /// <summary>
        /// World-space linear velocity in meters per second.
        /// </summary>
        public Vector3 Linear;

        /// <summary>
        /// World-space angular velocity in radians per second.
        /// </summary>
        public Vector3 Angular;
        
        public static Velocity AddVelocity(Velocity a, Velocity b) {
            return new Velocity {
                Linear = a.Linear + b.Linear,
                Angular = a.Angular + b.Angular
            };
        }

        public static Velocity SubtractVelocity(Velocity a, Velocity b) {
            return new Velocity {
                Linear = a.Linear - b.Linear,
                Angular = a.Angular - b.Angular
            };
        }
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