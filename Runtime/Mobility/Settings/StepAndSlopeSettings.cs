using System;
using UnityEngine;

namespace Character.Mobility
{
    [Serializable]
    public class StepAndSlopeSettings
    {
        [Range(0f, 89f)]
        public float SlopeLimit = 60f;
        [Min(0f)]
        public float StepOffset = 0.5f;
        public bool AllowSteppingWithoutStableGrounding;
        [Min(0f)]
        public float GroundDetectionExtraDistance = 0.1f;
        public bool EnableFutureSlopeUngrounding = true;
        public bool LedgeHandling = true;
        [Min(0f)]
        public float MaxStableDistanceFromLedge = 0.5f;
        [Min(0f)]
        public float MaxVelocityForLedgeSnap = 0f;
        [Range(1f, 180f)]
        public float MaxStableDenivelationAngle = 180f;
        
        public void Validate() {
            StepOffset = Mathf.Max(0f, StepOffset);
            GroundDetectionExtraDistance = Mathf.Max(0f, GroundDetectionExtraDistance);
            SlopeLimit = Mathf.Clamp(SlopeLimit, 0f, 89f);
            MaxStableDistanceFromLedge = Mathf.Max(0f, MaxStableDistanceFromLedge);
            MaxVelocityForLedgeSnap = Mathf.Max(0f, MaxVelocityForLedgeSnap);
            MaxStableDenivelationAngle = Mathf.Clamp(MaxStableDenivelationAngle, 1f, 180f);
        }
    }
}
