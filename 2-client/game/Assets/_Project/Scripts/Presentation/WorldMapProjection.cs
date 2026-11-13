using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Projects authoritative settlement metres (+X east, +Y north) onto a north-up UI map.</summary>
    public static class WorldMapProjection
    {
        public static Vector2 ToMapPoint(double x, double y, double worldHalf, Rect mapRect)
        {
            if (worldHalf <= 0d) throw new ArgumentOutOfRangeException(nameof(worldHalf));
            var span = worldHalf * 2d;
            // Do not clamp: zoomed maps need off-screen geometry to remain outside the viewport
            // so the caller can clip it instead of pinning every distant feature to the map edge.
            var normalizedX = (float)((x + worldHalf) / span);
            var normalizedY = (float)((worldHalf - y) / span);
            return new Vector2(
                mapRect.x + normalizedX * mapRect.width,
                mapRect.y + normalizedY * mapRect.height);
        }

        public static Rect ToMapRect(double x0, double y0, double x1, double y1, double worldHalf, Rect mapRect)
        {
            var topLeft = ToMapPoint(x0, y1, worldHalf, mapRect);
            var bottomRight = ToMapPoint(x1, y0, worldHalf, mapRect);
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }
    }
}
