using System;
using Pogranichye.Client.Presentation;

internal static class WorldBuildingPresentationPolicyCheck
{
    private static int _failures;
    private static void Check(string name, bool condition)
    {
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        Check("v1.33 five-storey city facade uses server height",
            Math.Abs(WorldBuildingPresentationPolicy.ResolveWallHeight(16d, 5, false) - 16d) < 0.0001d);
        Check("storeys are fallback when height_m is omitted",
            Math.Abs(WorldBuildingPresentationPolicy.ResolveWallHeight(0d, 3, false) - 9.6d) < 0.0001d);
        Check("legacy village house keeps historical fallback height",
            Math.Abs(WorldBuildingPresentationPolicy.ResolveWallHeight(0d, 0, false) - 3.8d) < 0.0001d);
        Check("legacy open market keeps low canopy height",
            Math.Abs(WorldBuildingPresentationPolicy.ResolveWallHeight(0d, 0, true) - 2.8d) < 0.0001d);
        Check("absurd server height is clamped for presentation",
            Math.Abs(WorldBuildingPresentationPolicy.ResolveWallHeight(1000d, 99, false) - 22d) < 0.0001d);
        Check("multi-storey count is derived from facade height",
            WorldBuildingPresentationPolicy.ResolveStoreyCount(16d) == 5);

        Console.WriteLine(_failures == 0
            ? "WorldBuildingPresentationPolicy: all checks passed"
            : "WorldBuildingPresentationPolicy: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
