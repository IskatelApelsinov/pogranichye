using System;
using Pogranichye.Client.Presentation;

internal static class WorldWindowAperturePolicyCheck
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
        Check("tiny wall does not receive an invalid window", WorldWindowAperturePolicy.ResolveWindowCount(2.0d) == 0);
        Check("narrow house gets one window", WorldWindowAperturePolicy.ResolveWindowCount(5.0d) == 1);
        Check("wide facade gets two windows", WorldWindowAperturePolicy.ResolveWindowCount(8.0d) == 2);

        var narrowWidth = WorldWindowAperturePolicy.ResolveWindowWidth(5.0d);
        var wideWidth = WorldWindowAperturePolicy.ResolveWindowWidth(8.0d);
        Check("window width has a safe minimum", narrowWidth >= 0.68d);
        Check("window width has a bounded maximum", wideWidth <= 0.92d);

        var left = WorldWindowAperturePolicy.ResolveWindowCenter(8.0d, 2, 0);
        var right = WorldWindowAperturePolicy.ResolveWindowCenter(8.0d, 2, 1);
        Check("two windows are ordered", left < right);
        Check("two windows fit inside the wall edges",
            left - wideWidth * 0.5d > -4.0d && right + wideWidth * 0.5d < 4.0d);
        Check("single window is centered",
            Math.Abs(WorldWindowAperturePolicy.ResolveWindowCenter(5.0d, 1, 0)) < 0.0001d);

        Check("single-storey house receives a ground-floor aperture",
            WorldWindowAperturePolicy.FitsWall(3.8d, 0));
        Check("single-storey house does not receive an upper-floor aperture",
            !WorldWindowAperturePolicy.FitsWall(3.8d, 1));
        Check("five-storey facade supports its top-floor aperture",
            WorldWindowAperturePolicy.FitsWall(16.0d, 4));
        Check("window bands rise by one storey",
            Math.Abs(WorldWindowAperturePolicy.ResolveWindowBottom(1) -
                WorldWindowAperturePolicy.ResolveWindowBottom(0) - 3.2d) < 0.0001d);

        Console.WriteLine(_failures == 0
            ? "WorldWindowAperturePolicy: all checks passed"
            : "WorldWindowAperturePolicy: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
