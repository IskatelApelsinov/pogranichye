using System.Collections.Generic;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Samples one grid point from a chunk heightfield, borrowing only the four
    /// cardinal edge samples from adjacent chunks with the same grid resolution.
    /// Coordinates are grid indices; x/z may be exactly one sample outside the chunk.
    /// </summary>
    public static class TerrainSeamHeightSampler
    {
        public static bool TryGetHeight(IList<double> current, IList<double> west,
            IList<double> east, IList<double> south, IList<double> north,
            int side, int x, int z, out double height)
        {
            height = 0d;
            if (side < 2 || current == null || current.Count < side * side) return false;

            if (x >= 0 && x < side && z >= 0 && z < side)
            {
                height = current[z * side + x];
                return IsFinite(height);
            }

            if (z >= 0 && z < side && x == -1 && HasGrid(west, side))
                height = west[z * side + side - 2];
            else if (z >= 0 && z < side && x == side && HasGrid(east, side))
                height = east[z * side + 1];
            else if (x >= 0 && x < side && z == -1 && HasGrid(south, side))
                height = south[(side - 2) * side + x];
            else if (x >= 0 && x < side && z == side && HasGrid(north, side))
                height = north[side + x];
            else
                return false;

            return IsFinite(height);
        }

        private static bool HasGrid(IList<double> values, int side) =>
            values != null && values.Count >= side * side;

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
