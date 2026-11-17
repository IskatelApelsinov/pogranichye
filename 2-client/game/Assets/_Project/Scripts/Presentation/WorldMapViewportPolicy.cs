using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Keeps the square world map usable in compact journal mode and allocates the
    /// largest safe square viewport in expanded mode without exceeding the screen.
    /// </summary>
    public static class WorldMapViewportPolicy
    {
        public static float GetMapSize(float screenWidth, float screenHeight, bool expanded)
        {
            if (!IsFinite(screenWidth) || !IsFinite(screenHeight) ||
                screenWidth <= 0f || screenHeight <= 0f)
                return 180f;

            var horizontalMargin = expanded ? 64f : 72f;
            var availableWidth = Math.Max(180f, screenWidth - horizontalMargin);
            var maximum = expanded
                ? Math.Min(availableWidth, Math.Max(180f, screenHeight - 190f))
                : Math.Min(520f, availableWidth);
            return (float)Math.Max(180f, maximum);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
