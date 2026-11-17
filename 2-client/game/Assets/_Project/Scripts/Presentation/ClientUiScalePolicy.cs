using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Resolution-aware typography policy for IMGUI. It never shrinks the baseline on
    /// compact displays and caps growth so large monitors do not produce oversized panels.
    /// </summary>
    // Presentation policy; deliberately independent from Unity runtime APIs.
    public static class ClientUiScalePolicy
    {
        public const int ReferenceHeight = 1080;
        public const float MaximumScale = 1.25f;

        public static float ScaleForHeight(int screenHeight)
        {
            if (screenHeight <= 0 || screenHeight <= ReferenceHeight) return 1f;
            return Math.Min(MaximumScale, (float)screenHeight / ReferenceHeight);
        }

        public static int ResolveFontSize(int baseFontSize, int screenHeight)
        {
            if (baseFontSize <= 0) return 1;
            return Math.Max(1, (int)Math.Round(baseFontSize * ScaleForHeight(screenHeight),
                MidpointRounding.AwayFromZero));
        }
    }
}
