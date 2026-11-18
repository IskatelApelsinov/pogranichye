using System;
using System.Globalization;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Pure validation and visibility rules for server-authored terrain gather props.
    /// Unknown future kinds remain in the DTO but are not given invented client models.
    /// </summary>
    public static class TerrainGatherNodeVisualPolicy
    {
        public static bool IsKnownKind(string kind)
        {
            switch (kind)
            {
                case "herb":
                case "mushroom":
                case "berries":
                case "hive":
                case "deadwood":
                case "nest":
                case "flax":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsRenderable(string id, string kind,
            double x, double z, double remaining, double sizeMeters)
        {
            return IsGatherId(id) && IsKnownKind(kind) &&
                IsFinite(x) && IsFinite(z) && IsFinite(remaining) && IsFinite(sizeMeters) &&
                sizeMeters > 0d && x >= 0d && z >= 0d &&
                x < sizeMeters && z < sizeMeters && remaining >= 0d;
        }

        public static bool ShouldBeVisible(double remaining)
        {
            return IsFinite(remaining) && remaining > 0d;
        }

        private static bool IsGatherId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Split(':');
            long cellX, cellZ;
            return parts.Length == 3 && parts[0] == "g" &&
                long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellX) &&
                long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellZ);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
