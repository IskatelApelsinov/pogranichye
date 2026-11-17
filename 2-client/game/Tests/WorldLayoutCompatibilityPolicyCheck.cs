using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking;

internal static class WorldLayoutCompatibilityPolicyCheck
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
        var city = new HashSet<string>(StringComparer.Ordinal)
        {
            "tavern", "well", "hall", "bazaar", "weaver", "tannery",
            "bakery", "carpenter", "potter", "guard", "caravanserai", "inn"
        };
        Check("v1.33 grid city is supported", WorldLayoutCompatibilityPolicy.IsSupportedPlan("grid"));
        Check("city POI set does not require rural woods/field/mine/river",
            WorldLayoutCompatibilityPolicy.HasRequiredPois("grid", city));
        city.Remove("hall");
        Check("city without its hall is rejected", !WorldLayoutCompatibilityPolicy.HasRequiredPois("grid", city));

        var rural = new HashSet<string>(StringComparer.Ordinal)
        {
            "tavern", "woods", "garden", "mine", "river"
        };
        Check("rural layouts keep the existing required POIs",
            WorldLayoutCompatibilityPolicy.HasRequiredPois("ring", rural));
        rural.Remove("river");
        Check("rural layout missing river/water POI is rejected",
            !WorldLayoutCompatibilityPolicy.HasRequiredPois("ring", rural));
        Check("unknown plan is rejected", !WorldLayoutCompatibilityPolicy.IsSupportedPlan("spiral-city"));
        Check("legacy missing plan remains supported", WorldLayoutCompatibilityPolicy.IsSupportedPlan(null));

        Console.WriteLine(_failures == 0
            ? "WorldLayoutCompatibilityPolicy: all checks passed"
            : "WorldLayoutCompatibilityPolicy: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
