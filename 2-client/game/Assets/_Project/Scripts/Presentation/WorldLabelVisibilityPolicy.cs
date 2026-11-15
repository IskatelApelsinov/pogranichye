using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Shared, Unity-independent limits for physical signs and world-space nameplates.
    /// Layer 2 is Unity's built-in Ignore Raycast layer and is reserved here for geometry
    /// that should occlude labels/camera without intercepting ordinary interaction probes.
    /// </summary>
    public static class WorldLabelVisibilityPolicy
    {
        public const int OccluderLayer = 2;
        public const int OccluderMask = 1 << OccluderLayer;
        public const float SignMaxDistance = 24f;
        public const float BillboardMaxDistance = 45f;
        public const float FacingDotThreshold = 0.05f;

        public static bool IsWithinDistance(float distance, float maximum)
        {
            return !float.IsNaN(distance) && !float.IsInfinity(distance) &&
                !float.IsNaN(maximum) && !float.IsInfinity(maximum) &&
                distance >= 0f && maximum > 0f && distance <= maximum;
        }

        public static bool IsFacing(float facingDot)
        {
            return !float.IsNaN(facingDot) && !float.IsInfinity(facingDot) &&
                facingDot > FacingDotThreshold;
        }
    }
}
