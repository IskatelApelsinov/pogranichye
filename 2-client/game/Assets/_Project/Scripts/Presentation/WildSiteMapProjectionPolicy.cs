using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Converts global wilderness E/N coordinates into the current settlement-centred map.</summary>
    public static class WildSiteMapProjectionPolicy
    {
        public static bool TryToSettlementLocal(double east, double north,
            double anchorEast, double anchorNorth, out double localEast, out double localNorth)
        {
            localEast = 0d;
            localNorth = 0d;
            if (!IsFinite(east) || !IsFinite(north) || !IsFinite(anchorEast) || !IsFinite(anchorNorth))
                return false;
            localEast = east - anchorEast;
            localNorth = north - anchorNorth;
            if (!IsFinite(localEast) || !IsFinite(localNorth) ||
                Math.Abs(localEast) > 1000000d || Math.Abs(localNorth) > 1000000d)
            {
                localEast = 0d;
                localNorth = 0d;
                return false;
            }
            return true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
