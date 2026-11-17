using System;

namespace Pogranichye.Client.Presentation
{
    public enum TerrainResourceVisualKind
    {
        Generic = 0,
        LogPile = 1,
        BerryBush = 2,
        StoneCluster = 3
    }

    /// <summary>
    /// Maps only the existing /api/terrain resource-hint vocabulary to visual archetypes.
    /// These hints are not harvestable entities and must not grant interaction or inventory.
    /// </summary>
    public static class TerrainResourceVisualPolicy
    {
        public static TerrainResourceVisualKind Resolve(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wood":
                    return TerrainResourceVisualKind.LogPile;
                case "berries":
                    return TerrainResourceVisualKind.BerryBush;
                case "stone":
                case "ore":
                    return TerrainResourceVisualKind.StoneCluster;
                default:
                    return TerrainResourceVisualKind.Generic;
            }
        }
    }
}
