using System;

namespace Pogranichye.Client.Presentation
{
    public enum WorldMaterialSource
    {
        Procedural = 0,
        Grok = 1,
        Authored = 2
    }

    public static class WorldMaterialSourcePolicy
    {
        public static bool IsAuthoredAvailable(string availability, bool has1024Albedo)
        {
            if (!has1024Albedo) return false;
            return string.IsNullOrWhiteSpace(availability) ||
                string.Equals(availability, "available", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldApplyGeneratedMaps(string availability, int albedoWidth)
        {
            if (string.IsNullOrWhiteSpace(availability)) return true;
            if (string.Equals(availability, "available", StringComparison.OrdinalIgnoreCase))
                return albedoWidth >= 1024;
            if (string.Equals(availability, "pending", StringComparison.OrdinalIgnoreCase))
                return albedoWidth > 0 && albedoWidth < 1024;
            return false;
        }

        public static WorldMaterialSource Select(bool hasAuthoredAlbedo, bool hasGrokAlbedo)
        {
            if (hasAuthoredAlbedo) return WorldMaterialSource.Authored;
            if (hasGrokAlbedo) return WorldMaterialSource.Grok;
            return WorldMaterialSource.Procedural;
        }
    }
}
