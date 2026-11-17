using System;
using Pogranichye.Client.Presentation;

internal static class TerrainSeamHeightSamplerCheck
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static double[] Plane(int side, int originX, int originZ)
    {
        var values = new double[side * side];
        for (var z = 0; z < side; z++)
        for (var x = 0; x < side; x++)
            values[z * side + x] = (originX + x) * 2d + (originZ + z) * 3d;
        return values;
    }

    public static int Main()
    {
        const int side = 5;
        var center = Plane(side, 0, 0);
        var west = Plane(side, -side, 0);
        var east = Plane(side, side, 0);
        var south = Plane(side, 0, -side);
        var north = Plane(side, 0, side);
        double left, edge, right, below, above;

        Assert(TerrainSeamHeightSampler.TryGetHeight(center, west, east, south, north,
            side, -1, 2, out left), "west edge sample must come from adjacent chunk");
        Assert(TerrainSeamHeightSampler.TryGetHeight(center, west, east, south, north,
            side, 0, 2, out edge), "current edge sample must exist");
        Assert(TerrainSeamHeightSampler.TryGetHeight(center, west, east, south, north,
            side, 1, 2, out right), "inner sample must exist");
        Assert(Math.Abs((edge - left) - 2d) < 0.000001d, "west boundary derivative must match plane");
        Assert(Math.Abs((right - edge) - 2d) < 0.000001d, "eastward derivative must match plane");

        Assert(TerrainSeamHeightSampler.TryGetHeight(center, west, east, south, north,
            side, 2, -1, out below), "south edge sample must come from adjacent chunk");
        Assert(TerrainSeamHeightSampler.TryGetHeight(center, west, east, south, north,
            side, 2, 1, out above), "northward sample must exist");
        Assert(Math.Abs((center[2]) - 3d) < 0.000001d,
            "south boundary derivative must match plane");
        Assert(Math.Abs((above - center[2]) - 3d) < 0.000001d,
            "northward derivative must match plane");

        Assert(!TerrainSeamHeightSampler.TryGetHeight(center, null, east, south, north,
            side, -1, 2, out _), "missing neighbor must not fabricate a height");
        Assert(!TerrainSeamHeightSampler.TryGetHeight(center, west, east, south, north,
            side, -1, -1, out _), "diagonal samples are deliberately unsupported");
        Console.WriteLine("Terrain seam height sampling checks passed.");
        return 0;
    }
}
