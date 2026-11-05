using UnityEngine;

namespace Pogranichye.Client.Networking
{
    /// <summary>
    /// Client-side prediction mirror of server sim/world_geometry.go (GeometryVersion 2).
    /// Keep this data byte-for-byte aligned with the server layout; authoritative correction
    /// always comes from the next server frame.
    /// </summary>
    public static class WorldGeometryV2
    {
        public const int Version = 2;
        public const float WorldHalf = 150f;
        public const float VillageHalf = 50f;

        private struct Rect
        {
            public readonly float X0, Y0, X1, Y1;
            public Rect(float x0, float y0, float x1, float y1)
            { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public bool Contains(float x, float y)
            { return x >= X0 && x <= X1 && y >= Y0 && y <= Y1; }
        }

        private static readonly Rect[] Buildings =
        {
            new Rect(-40, 14, -26, 26), // tavern
            new Rect(22, 16, 36, 28),   // smithy
            new Rect(-10, 30, 6, 42),   // granary
            new Rect(26, -30, 40, -18),
            new Rect(-40, -30, -28, -20),
            new Rect(-14, -34, -2, -24),
            new Rect(6, -36, 18, -26),
            new Rect(-6, 6, 8, 16)       // market
        };

        public static bool IsBlocked(Vector2 point)
        {
            if (point.x < -WorldHalf || point.x > WorldHalf ||
                point.y < -WorldHalf || point.y > WorldHalf) return true;
            for (var i = 0; i < Buildings.Length; i++)
                if (Buildings[i].Contains(point.x, point.y)) return true;
            if (point.y >= -90f && point.y <= -78f &&
                (point.x < -6f || point.x > 6f)) return true;
            return false;
        }

        /// <summary>Exact axis-slide fallback order used by server ResolveMove.</summary>
        public static Vector2 ResolveMove(Vector2 origin, Vector2 desired)
        {
            if (!IsBlocked(desired)) return desired;
            if (!IsBlocked(new Vector2(desired.x, origin.y))) return new Vector2(desired.x, origin.y);
            if (!IsBlocked(new Vector2(origin.x, desired.y))) return new Vector2(origin.x, desired.y);
            return origin;
        }
    }
}
