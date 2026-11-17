namespace Pogranichye.Client.World
{
    /// <summary>
    /// Clips a screen-space map segment to a rectangular viewport using Liang-Barsky.
    /// Pure geometry only; it does not know about Unity or world coordinates.
    /// </summary>
    public static class WorldMapLineClipper
    {
        public static bool TryClip(ref double x0, ref double y0, ref double x1, ref double y1,
            double minX, double minY, double maxX, double maxY)
        {
            if (!IsFinite(x0) || !IsFinite(y0) || !IsFinite(x1) || !IsFinite(y1) ||
                !IsFinite(minX) || !IsFinite(minY) || !IsFinite(maxX) || !IsFinite(maxY) ||
                maxX < minX || maxY < minY) return false;

            var dx = x1 - x0;
            var dy = y1 - y0;
            var enter = 0d;
            var exit = 1d;
            if (!Clip(-dx, x0 - minX, ref enter, ref exit) ||
                !Clip(dx, maxX - x0, ref enter, ref exit) ||
                !Clip(-dy, y0 - minY, ref enter, ref exit) ||
                !Clip(dy, maxY - y0, ref enter, ref exit)) return false;

            var originalX = x0;
            var originalY = y0;
            x0 = originalX + enter * dx;
            y0 = originalY + enter * dy;
            x1 = originalX + exit * dx;
            y1 = originalY + exit * dy;
            return true;
        }

        private static bool Clip(double p, double q, ref double enter, ref double exit)
        {
            if (MathAbs(p) < 0.000000000001d) return q >= 0d;
            var ratio = q / p;
            if (p < 0d)
            {
                if (ratio > exit) return false;
                if (ratio > enter) enter = ratio;
            }
            else
            {
                if (ratio < enter) return false;
                if (ratio < exit) exit = ratio;
            }
            return true;
        }

        private static double MathAbs(double value) { return value < 0d ? -value : value; }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
