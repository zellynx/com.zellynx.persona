using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Defines the common structure for CharacterBody Shape configuration data.
    /// </summary>
    public interface IGeometrySettings<in T> where T : Collider
    {
        /// <summary>
        /// The local center offset of the shape.
        /// </summary>
        Vector3 Center { get; set; }

        /// <summary>
        /// Validates the configuration values to prevent invalid physics shapes.
        /// </summary>
        void Validate();

        /// <summary>
        /// Applies the configured shape settings to the corresponding collider.
        /// </summary>
        /// <param name="collider">The collider to configure.</param>
        void ApplyTo(T collider);
    }
}