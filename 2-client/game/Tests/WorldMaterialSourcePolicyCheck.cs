using System;
using Pogranichye.Client.Presentation;

internal static class WorldMaterialSourcePolicyCheck
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
        Check("available 1024px albedo is authored", WorldMaterialSourcePolicy.IsAuthoredAvailable("available", true));
        Check("pending pack is never authored", !WorldMaterialSourcePolicy.IsAuthoredAvailable("pending", true));
        Check("available pack without full-size albedo is not authored", !WorldMaterialSourcePolicy.IsAuthoredAvailable("available", false));
        Check("legacy manifest remains compatible with full-size albedo", WorldMaterialSourcePolicy.IsAuthoredAvailable(null, true));
        Check("legacy manifest without albedo is not authored", !WorldMaterialSourcePolicy.IsAuthoredAvailable(null, false));
        Check("available 1024px maps may be applied", WorldMaterialSourcePolicy.ShouldApplyGeneratedMaps("available", 1024));
        Check("pending 512px preview may be applied as fallback", WorldMaterialSourcePolicy.ShouldApplyGeneratedMaps("pending", 512));
        Check("pending 1024px map is blocked", !WorldMaterialSourcePolicy.ShouldApplyGeneratedMaps("pending", 1024));
        Check("available manifest without albedo is blocked", !WorldMaterialSourcePolicy.ShouldApplyGeneratedMaps("available", 0));
        Check("unknown status fails closed", !WorldMaterialSourcePolicy.ShouldApplyGeneratedMaps("broken", 1024));
        Check("legacy generated maps keep previous behavior", WorldMaterialSourcePolicy.ShouldApplyGeneratedMaps(null, 512));
        Check("authored source wins over Grok", WorldMaterialSourcePolicy.Select(true, true) == WorldMaterialSource.Authored);
        Check("Grok wins over procedural", WorldMaterialSourcePolicy.Select(false, true) == WorldMaterialSource.Grok);
        Check("procedural remains the final fallback", WorldMaterialSourcePolicy.Select(false, false) == WorldMaterialSource.Procedural);
        Console.WriteLine(_failures == 0 ? "WorldMaterialSourcePolicy: " + _checks + " checks passed" :
            "WorldMaterialSourcePolicy: FAILED " + _failures + " / " + _checks);
        return _failures == 0 ? 0 : 1;
    }
}
