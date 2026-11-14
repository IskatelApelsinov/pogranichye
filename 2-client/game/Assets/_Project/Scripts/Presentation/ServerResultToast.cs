using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Layout helper for server me.result / reason toasts: full text, word wrap, no mid-sentence clip.
    /// Pure functions — safe for EditMode tests.
    /// </summary>
    public static class ServerResultToast
    {
        public const float MinWidth = 320f;
        public const float MaxWidthFraction = 0.55f;
        public const float SideMargin = 24f;
        public const float LineHeight = 20f;
        public const float PaddingY = 16f;
        public const int MaxVisibleLines = 6;

        public static float WidthForScreen(float screenWidth)
        {
            if (screenWidth < 1f) screenWidth = 1f;
            var max = Mathf.Max(MinWidth, screenWidth * MaxWidthFraction);
            return Mathf.Clamp(max, MinWidth, Mathf.Max(MinWidth, screenWidth - SideMargin * 2f));
        }

        public static float HeightForText(string text, float width)
        {
            if (string.IsNullOrEmpty(text)) return LineHeight + PaddingY;
            var charsPerLine = Mathf.Max(12, Mathf.FloorToInt(width / 8.5f));
            var lines = 1;
            var col = 0;
            foreach (var ch in text)
            {
                if (ch == '\n')
                {
                    lines++;
                    col = 0;
                    continue;
                }
                col++;
                if (col >= charsPerLine)
                {
                    lines++;
                    col = 0;
                }
            }
            lines = Mathf.Clamp(lines, 1, MaxVisibleLines);
            return lines * LineHeight + PaddingY;
        }

        public static Rect RectForScreen(float screenWidth, float screenHeight, string text)
        {
            var w = WidthForScreen(screenWidth);
            var h = HeightForText(text, w);
            var x = (screenWidth - w) * 0.5f;
            var y = screenHeight * 0.58f;
            if (y + h > screenHeight - 12f) y = Mathf.Max(12f, screenHeight - h - 12f);
            return new Rect(x, y, w, h);
        }

        public static string DisplayText(string reasonOrToast)
        {
            return string.IsNullOrEmpty(reasonOrToast) ? string.Empty : reasonOrToast.Trim();
        }
    }
}
