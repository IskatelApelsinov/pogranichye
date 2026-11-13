using System;
using System.Collections.Generic;

namespace Pogranichye.Client.Presentation
{
    public struct PoiCoordinate
    {
        public readonly double X;
        public readonly double Y;

        public PoiCoordinate(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Resolves current Layout v2 POIs before any legacy starter-layout fallback.</summary>
    public static class PoiPositionPolicy
    {
        public static bool TryResolve(string poiId,
            IDictionary<string, PoiCoordinate> layoutPois,
            IDictionary<string, PoiCoordinate> legacyPois,
            bool hasLayoutPois,
            out PoiCoordinate coordinate)
        {
            coordinate = default(PoiCoordinate);
            if (string.IsNullOrEmpty(poiId)) return false;

            var source = hasLayoutPois ? layoutPois : legacyPois;
            if (source == null) return false;
            if (source.TryGetValue(poiId, out coordinate)) return true;

            // The server's apiary action is validated at the garden POI; the visual hive is a
            // nearby presentation prop, not a separate Layout v2/navigation anchor.
            return hasLayoutPois && string.Equals(poiId, "apiary", StringComparison.Ordinal) &&
                source.TryGetValue("garden", out coordinate);
        }

        public static bool IsWithin(double playerX, double playerY,
            double poiX, double poiY, double radius)
        {
            if (!IsFinite(playerX) || !IsFinite(playerY) ||
                !IsFinite(poiX) || !IsFinite(poiY) ||
                !IsFinite(radius) || radius < 0d)
                return false;

            var dx = playerX - poiX;
            var dy = playerY - poiY;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
