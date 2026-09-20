using System;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    /// <summary>
    /// Authoring data for the sphere variant of CharacterBody.
    /// </summary>
    [Serializable]
    public struct SphereColliderSettings : IColliderSettings<SphereCollider>
    {
        public GeometryType GeometryType => UnityEngine.LowLevelPhysics.GeometryType.Sphere;

        public Vector3 Center { get; set; }
        [Min(0.01f)] public float Radius;

        public void Validate() {
            Radius = Mathf.Max(0.01f, Radius);
        }

        public void ApplyTo(SphereCollider collider) {
            if (collider == null) return;

            collider.radius = Radius;
            collider.center = Center;
        }

        public static SphereColliderSettings Default => new() {
            Center = new Vector3(0f, 0.5f, 0f),
            Radius = 0.5f
        };
    }
}