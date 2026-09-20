using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    /// <summary>
    /// Defines the common structure for CharacterBody collider configuration data.
    /// </summary>
    public interface IColliderSettings<in T> where T : Collider
    {
        /// <summary>
        /// The Unity low-level physics geometry type represented by these settings.
        /// </summary>
        GeometryType GeometryType { get; }

        /// <summary>
        /// The local center offset of the collider shape.
        /// </summary>
        Vector3 Center { get; set; }

        /// <summary>
        /// Validates the configuration values to prevent invalid physics shapes.
        /// </summary>
        void Validate();

        /// <summary>
        /// Applies the configured collider settings to the corresponding collider.
        /// </summary>
        /// <param name="collider">The collider to configure.</param>
        void ApplyTo(T collider);
    }
}