using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Pure placement policy for small, non-shadowed interior presentation lights.
    /// It consumes only server-authored room bounds; it never creates world entities.
    /// </summary>
    public static class WorldInteriorLightingPolicy
    {
        public struct LightPlan
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;
            public readonly float Range;
            public readonly float Intensity;

            public LightPlan(float x, float y, float z, float range, float intensity)
            {
                X = x;
                Y = y;
                Z = z;
                Range = range;
                Intensity = intensity;
            }
        }

        public static bool TryResolve(string roomKind,
            double minEast, double minNorth, double minUp,
            double maxEast, double maxNorth, double maxUp,
            double originEast, double originNorth, double originUp,
            out LightPlan plan)
        {
            plan = default(LightPlan);
            if (!string.Equals(roomKind, "tavern", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Finite(minEast) || !Finite(minNorth) || !Finite(minUp) ||
                !Finite(maxEast) || !Finite(maxNorth) || !Finite(maxUp) ||
                !Finite(originEast) || !Finite(originNorth) || !Finite(originUp)) return false;

            var width = maxEast - minEast;
            var depth = maxNorth - minNorth;
            var height = maxUp - minUp;
            if (width < 1.5d || depth < 1.5d || height < 1.8d) return false;

            var x = (minEast + maxEast) * 0.5d - originEast;
            var z = (minNorth + maxNorth) * 0.5d - originNorth;
            // Hang the light just below the server-defined ceiling, never above it.
            var y = maxUp - originUp - Math.Min(0.65d, height * 0.2d);
            var range = Math.Max(3d, Math.Min(7d, Math.Max(width, depth) * 0.55d));
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(range)) return false;

            plan = new LightPlan((float)x, (float)y, (float)z, (float)range, 0.72f);
            return true;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
