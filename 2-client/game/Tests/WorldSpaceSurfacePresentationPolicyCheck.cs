using System;
using Pogranichye.Client.Presentation;

internal static class WorldSpaceSurfacePresentationPolicyCheck
{
    private static int _checks;
    private static int _failures;

    private static void Check(string name, bool condition)
    {
        _checks++;
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        Check("explicitly transparent server window uses glass",
            WorldSpaceSurfacePresentationPolicy.UsesWindowGlass("window", true));
        Check("window without transparency remains opaque",
            WorldSpaceSurfacePresentationPolicy.UsesOpaqueWindowFallback("window", false));
        Check("opaque window does not use glass",
            !WorldSpaceSurfacePresentationPolicy.UsesWindowGlass("window", false));
        Check("transparent non-window surface does not become glass",
            !WorldSpaceSurfacePresentationPolicy.UsesWindowGlass("wall", true));
        Check("transparent window does not cast shadows",
            !WorldSpaceSurfacePresentationPolicy.CastsShadows("window", true));
        Check("transparent window does not receive shadows",
            !WorldSpaceSurfacePresentationPolicy.ReceivesShadows("window", true));
        Check("opaque wall retains shadow casting",
            WorldSpaceSurfacePresentationPolicy.CastsShadows("wall", false));
        Check("opaque wall retains shadow receiving",
            WorldSpaceSurfacePresentationPolicy.ReceivesShadows("wall", false));
        Check("floor retains existing no-shadow behavior",
            !WorldSpaceSurfacePresentationPolicy.CastsShadows("floor", false));
        Check("ceiling retains existing no-shadow behavior",
            !WorldSpaceSurfacePresentationPolicy.CastsShadows("ceiling", false));
        Check("kind matching is case-insensitive",
            WorldSpaceSurfacePresentationPolicy.UsesWindowGlass("WINDOW", true));

        Console.WriteLine(_failures == 0
            ? "WorldSpaceSurfacePresentationPolicy: " + _checks + " checks passed"
            : "WorldSpaceSurfacePresentationPolicy: FAILED " + _failures + " / " + _checks);
        return _failures == 0 ? 0 : 1;
    }
}
