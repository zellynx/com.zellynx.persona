using System;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Character.Mobility
{
    /// <summary>
    /// Authoring data for the box variant of CharacterBody.
    /// </summary>
    [Serializable]
    public struct BoxColliderSettings : IColliderSettings<BoxCollider>
    {
        public GeometryType GeometryType => UnityEngine.LowLevelPhysics.GeometryType.Box;

        public Vector3 Center { get; set; }
        public Vector3 Size;

        public void Validate() {
            Size.x = Mathf.Max(0.01f, Mathf.Abs(Size.x));
            Size.y = Mathf.Max(0.01f, Mathf.Abs(Size.y));
            Size.z = Mathf.Max(0.01f, Mathf.Abs(Size.z));
        }

        public void ApplyTo(BoxCollider collider) {
            if (collider == null) return;

            collider.center = Center;
            collider.size = Size;
        }

        public static BoxColliderSettings Default => new() {
            Center = new Vector3(0f, 1f, 0f),
            Size = new Vector3(1f, 2f, 1f)
        };
    }
}