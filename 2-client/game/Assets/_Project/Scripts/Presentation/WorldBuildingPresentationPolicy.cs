using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Maps optional server storeys/height into safe visual facade dimensions only.</summary>
    public static class WorldBuildingPresentationPolicy
    {
        public static double ResolveWallHeight(double heightMeters, int storeys, bool market)
        {
            if (market && storeys <= 1 && heightMeters <= 0d) return 2.8d;
            if (!double.IsNaN(heightMeters) && !double.IsInfinity(heightMeters) && heightMeters > 0d)
                return Clamp(heightMeters, 2.8d, 22d);
            if (storeys > 0) return Clamp(storeys * 3.2d, 2.8d, 22d);
            return market ? 2.8d : 3.8d;
        }

        public static int ResolveStoreyCount(double wallHeight)
        {
            if (double.IsNaN(wallHeight) || double.IsInfinity(wallHeight) || wallHeight <= 0d) return 1;
            return Math.Max(1, Math.Min(7, (int)Math.Round(wallHeight / 3.2d, MidpointRounding.AwayFromZero)));
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
