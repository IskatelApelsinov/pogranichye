using System;
using Pogranichye.Client.Presentation;

internal static class TerrainWaterSurfacePolicyCheck
{
    private static int _checks;

    private static void Equal(double expected, double actual, string name)
    {
        _checks++;
        if (Math.Abs(expected - actual) > 0.000001d)
            throw new Exception(name + ": expected " + expected + ", got " + actual);
    }

    private static void Main()
    {
        Equal(12.5d, TerrainWaterSurfacePolicy.ResolveLocalHeight(112.5d, 110d, 100d, 0.34d),
            "explicit server surface elevation wins without legacy lift");
        Equal(10.34d, TerrainWaterSurfacePolicy.ResolveLocalHeight(null, 110d, 100d, 0.34d),
            "legacy descriptor retains existing visual lift");
        Equal(10.34d, TerrainWaterSurfacePolicy.ResolveLocalHeight(double.NaN, 110d, 100d, 0.34d),
            "non-finite optional sample falls back safely");
        Equal(0d, TerrainWaterSurfacePolicy.ResolveLocalHeight(112.5d, double.NaN, 100d, 0.34d),
            "valid explicit surface does not depend on fallback water level");
        Equal(0d, TerrainWaterSurfacePolicy.ResolveLocalHeight(null, double.NaN, 100d, 0.34d),
            "invalid fallback data does not propagate NaN");
        Console.WriteLine("Terrain water surface policy: " + _checks + " checks passed.");
    }
}
