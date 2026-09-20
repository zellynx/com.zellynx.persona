using System;
using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// Selects how the character exchanges momentum with dynamic rigidbodies.
    /// </summary>
    public enum CharacterBodyRigidbodyInteractionMode
    {
        None,
        Kinematic,
        SimulatedDynamic
    }

    /// <summary>
    /// Serialized configuration for CharacterBody movement, grounding, and interaction behavior.
    /// </summary>
    [Serializable]
    public class CharacterBodySettings
    {
        [Header("Collision")]
        [Min(0f)] public float SkinWidth = 0.05f;
        public LayerMask CollidableLayers = ~0;
        public LayerMask StableGroundLayers = ~0;
        [Min(1)] public int MaxMovementIterations = 5;
        [Min(1)] public int MaxDecollisionIterations = 4;
        [Min(0f)] public float MinMoveDistance = 0.0001f;

        [Header("Grounding")]
        public StepAndSlopeSettings StepAndSlopeSettings = new();
        public bool SnapToGround = true;
        [Min(0f)] public float GroundSnapDistance = 0.1f;
        public bool PreservePlatformMomentum = true;
        public bool UseGravity;

        [Header("Interpolation")]
        public bool UseInterpolation;

        [Header("Rigidbody Interaction")]
        public bool EnableRigidbodyInteraction = true;
        public CharacterBodyRigidbodyInteractionMode RigidbodyInteractionMode = CharacterBodyRigidbodyInteractionMode.Kinematic;
        [Min(0.01f)] public float SimulatedCharacterMass = 1f;
        [Min(0f)] public float MaxPushVelocityChange = 10f;

        [Header("Rotation")]
        public bool SlerpRotation;

        public void Validate(float maxHorizontalExtent, float maxVerticalExtent) {
            SkinWidth = Mathf.Max(0f, SkinWidth);
            MaxMovementIterations = Mathf.Max(1, MaxMovementIterations);
            MaxDecollisionIterations = Mathf.Max(1, MaxDecollisionIterations);
            MinMoveDistance = Mathf.Max(0f, MinMoveDistance);
            SimulatedCharacterMass = Mathf.Max(0.01f, SimulatedCharacterMass);
            MaxPushVelocityChange = Mathf.Max(0f, MaxPushVelocityChange);
            
            // Ensure the nested class isn't null, then validate it
            StepAndSlopeSettings ??= new StepAndSlopeSettings();
            StepAndSlopeSettings.Validate();
            
            // Finally, apply the geometry limits (so SkinWidth isn't larger than the character, etc.)
            SkinWidth = Mathf.Min(SkinWidth, Mathf.Max(0.001f, maxHorizontalExtent * 0.5f));
            GroundSnapDistance = Mathf.Max(0f, GroundSnapDistance);
            StepAndSlopeSettings.StepOffset = Mathf.Min(StepAndSlopeSettings.StepOffset, Mathf.Max(0f, maxVerticalExtent - SkinWidth));
            StepAndSlopeSettings.MaxStableDistanceFromLedge = Mathf.Min(StepAndSlopeSettings.MaxStableDistanceFromLedge, maxHorizontalExtent);
        }
    }
}
