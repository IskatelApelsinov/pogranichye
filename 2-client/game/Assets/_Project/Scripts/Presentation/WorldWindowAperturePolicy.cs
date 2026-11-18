using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Deterministic, engine-independent dimensions for daylight window apertures.</summary>
    public static class WorldWindowAperturePolicy
    {
        public static int ResolveWindowCount(double wallAxisLength)
        {
            if (wallAxisLength < 2.2d) return 0;
            return wallAxisLength >= 6.2d ? 2 : 1;
        }

        public static double ResolveWindowWidth(double wallAxisLength)
        {
            return Math.Max(0.68d, Math.Min(0.92d, wallAxisLength * 0.13d));
        }

        public static double ResolveWindowCenter(double wallAxisLength, int count, int index)
        {
            if (count <= 0 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count == 1) return 0d;
            if (count == 2) return index == 0 ? -wallAxisLength * 0.24d : wallAxisLength * 0.24d;
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        public static double ResolveWindowBottom(int floor)
        {
            if (floor < 0) throw new ArgumentOutOfRangeException(nameof(floor));
            return 1.22d + floor * 3.2d;
        }

        public static double ResolveWindowTop(int floor)
        {
            return ResolveWindowBottom(floor) + 0.92d;
        }

        public static bool FitsWall(double wallHeight, int floor)
        {
            return ResolveWindowTop(floor) <= wallHeight - 0.12d;
        }
    }
}
