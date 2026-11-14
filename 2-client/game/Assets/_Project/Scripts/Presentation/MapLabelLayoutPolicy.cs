using System;

namespace Pogranichye.Client.Presentation
{
    public struct MapLabelPlacement
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;
        public readonly bool AlignRight;

        public MapLabelPlacement(float x, float y, float width, float height, bool alignRight)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            AlignRight = alignRight;
        }
    }

    /// <summary>
    /// Keeps settlement-map labels inside the map viewport. Labels prefer the right side
    /// of a marker, flip to the left near the right edge, and clamp vertically at both edges.
    /// </summary>
    public static class MapLabelLayoutPolicy
    {
        public static bool TryPlace(float viewportX, float viewportY, float viewportWidth,
            float viewportHeight, float markerX, float markerY, float labelWidth,
            float labelHeight, float gap, out MapLabelPlacement placement)
        {
            placement = default(MapLabelPlacement);
            if (!IsFinite(viewportX) || !IsFinite(viewportY) ||
                !IsFinite(viewportWidth) || !IsFinite(viewportHeight) ||
                !IsFinite(markerX) || !IsFinite(markerY) ||
                !IsFinite(labelWidth) || !IsFinite(labelHeight) || !IsFinite(gap) ||
                viewportWidth <= 0f || viewportHeight <= 0f ||
                labelWidth <= 0f || labelHeight <= 0f || gap < 0f ||
                labelWidth > viewportWidth || labelHeight > viewportHeight)
                return false;

            var right = viewportX + viewportWidth;
            var bottom = viewportY + viewportHeight;
            if (!IsFinite(right) || !IsFinite(bottom)) return false;

            var rightSpace = right - (markerX + gap);
            var leftSpace = (markerX - gap) - viewportX;
            var alignRight = rightSpace < labelWidth && leftSpace > rightSpace;
            var x = alignRight ? markerX - gap - labelWidth : markerX + gap;
            var y = markerY - labelHeight * 0.5f;

            x = Clamp(x, viewportX, right - labelWidth);
            y = Clamp(y, viewportY, bottom - labelHeight);
            placement = new MapLabelPlacement(x, y, labelWidth, labelHeight, alignRight);
            return true;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
