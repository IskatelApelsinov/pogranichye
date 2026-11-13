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
        public static bool IsLayoutForSettlement(string layoutSettlementId, string currentPlaceId)
        {
            return !string.IsNullOrEmpty(layoutSettlementId) &&
                !string.IsNullOrEmpty(currentPlaceId) &&
                string.Equals(layoutSettlementId, currentPlaceId, StringComparison.Ordinal);
        }

        public static bool AllowsLegacyFallback(string plan)
        {
            // A missing plan is the pre-v1.19 compatibility shape; explicit generated plans
            // must wait for their own Layout v2 rather than borrowing tutorial coordinates.
            return string.IsNullOrEmpty(plan) || string.Equals(plan, "starter", StringComparison.Ordinal);
        }

        public static bool TryResolve(string poiId,
            IDictionary<string, PoiCoordinate> layoutPois,
            IDictionary<string, PoiCoordinate> legacyPois,
            bool hasLayoutPois,
            bool allowLegacyFallback,
            out PoiCoordinate coordinate)
        {
            coordinate = default(PoiCoordinate);
            if (string.IsNullOrEmpty(poiId)) return false;

            // A generated settlement may be waiting for its own layout or have a failed
            // request. In that state, starter coordinates are actively misleading, so the
            // caller must opt in to the legacy map only for starter/legacy places.
            var source = hasLayoutPois ? layoutPois : allowLegacyFallback ? legacyPois : null;
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
