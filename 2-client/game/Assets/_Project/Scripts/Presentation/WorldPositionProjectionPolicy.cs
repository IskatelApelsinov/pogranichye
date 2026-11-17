using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Pure double-precision projection from global E/N/U to an anchor-relative Unity frame.</summary>
    public static class WorldPositionProjectionPolicy
    {
        public static bool TryProject(int worldFrame, double worldEast, double worldNorth, double worldUp,
            int anchorFrame, double anchorEast, double anchorNorth, double anchorUp,
            out double localEast, out double localUp, out double localNorth)
        {
            localEast = localUp = localNorth = 0d;
            if (worldFrame != anchorFrame ||
                !IsFinite(worldEast) || !IsFinite(worldNorth) || !IsFinite(worldUp) ||
                !IsFinite(anchorEast) || !IsFinite(anchorNorth) || !IsFinite(anchorUp))
                return false;

            localEast = worldEast - anchorEast;
            localNorth = worldNorth - anchorNorth;
            localUp = worldUp - anchorUp;
            if (!IsFinite(localEast) || !IsFinite(localNorth) || !IsFinite(localUp) ||
                Math.Abs(localEast) > 1000000d || Math.Abs(localNorth) > 1000000d ||
                Math.Abs(localUp) > 1000000d)
            {
                localEast = localUp = localNorth = 0d;
                return false;
            }
            return true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
