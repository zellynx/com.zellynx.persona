using System;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    /// <summary>
    /// Authoring data for the capsule variant of CharacterBody.
    /// CharacterBody uses an upright Y-axis CapsuleCollider; direction is not configurable.
    /// </summary>
    [Serializable]
    public struct CapsuleColliderSettings : IColliderSettings<CapsuleCollider>
    {
        public GeometryType GeometryType => UnityEngine.LowLevelPhysics.GeometryType.Capsule;

        public Vector3 Center { get; set; }
        [Min(0.01f)] public float Radius;
        [Min(0.1f)] public float Height;

        public void Validate() {
            Radius = Mathf.Max(0.01f, Radius);
            Height = Mathf.Max(Height, Radius * 2f);
        }

        public void ApplyTo(CapsuleCollider collider) {
            if (collider == null) return;

            collider.center = Center;
            collider.radius = Radius;
            collider.height = Height;
            collider.direction = 1; // CharacterBody invariant: upright Y-axis capsule only.
        }

        public static CapsuleColliderSettings Default => new() {
            Center = new Vector3(0f, 1f, 0f),
            Radius = 0.5f,
            Height = 2f
        };
    }
}