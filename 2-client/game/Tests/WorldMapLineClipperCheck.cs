using System;
using Pogranichye.Client.World;

public static class WorldMapLineClipperCheck
{
    public static int Main()
    {
        var x0 = -5d; var y0 = 5d; var x1 = 15d; var y1 = 5d;
        Check(WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "crossing horizontal segment should be retained");
        Near(x0, 0d, "horizontal clipped start x");
        Near(y0, 5d, "horizontal clipped start y");
        Near(x1, 10d, "horizontal clipped end x");
        Near(y1, 5d, "horizontal clipped end y");

        x0 = 2d; y0 = 3d; x1 = 8d; y1 = 9d;
        Check(WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "fully contained segment should remain");
        Near(x0, 2d, "contained start unchanged");
        Near(y1, 9d, "contained end unchanged");

        x0 = -4d; y0 = -3d; x1 = 14d; y1 = 15d;
        Check(WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "diagonal crossing should be clipped");
        Near(x0, 0d, "diagonal start x");
        Near(y0, 1d, "diagonal start y");
        Near(x1, 9d, "diagonal end x");
        Near(y1, 10d, "diagonal end y");

        x0 = -5d; y0 = -1d; x1 = 15d; y1 = -1d;
        Check(!WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "parallel segment outside viewport should be rejected");

        x0 = 11d; y0 = 2d; x1 = 15d; y1 = 8d;
        Check(!WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "segment fully outside viewport should be rejected");

        x0 = double.NaN; y0 = 2d; x1 = 5d; y1 = 8d;
        Check(!WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "non-finite input should be rejected");

        x0 = 5d; y0 = 5d; x1 = 5d; y1 = 5d;
        Check(WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1, 0d, 0d, 10d, 10d),
            "degenerate point inside viewport should remain valid");

        Console.WriteLine("World map line clipper checks passed.");
        return 0;
    }

    private static void Near(double actual, double expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.000001d, message + ": expected " + expected + ", got " + actual);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
