using System;
using Newtonsoft.Json;
using Pogranichye.Client.Networking.Protocol;

internal static class WorldLandFrameParserCheck
{
    private static int _failures;
    private static void Check(string name, bool condition)
    {
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }
    private static void ExpectFormat(string name, Action action)
    {
        try { action(); Check(name, false); }
        catch (FormatException) { Check(name, true); }
    }

    public static int Main()
    {
        var ringYardsPond = JsonConvert.DeserializeObject<WorldLandFrame>(
            "{\"water\":\"pond\",\"fields\":\"yards\",\"forest\":\"ring\"}");
        WorldLandFrameParser.Validate(ringYardsPond);
        Check("maps pond, household gardens and ring forest style",
            ringYardsPond != null && ringYardsPond.Water == "pond" &&
            ringYardsPond.Fields == "yards" && ringYardsPond.Forest == "ring");

        var city = JsonConvert.DeserializeObject<WorldLandFrame>(
            "{\"water\":\"well\",\"fields\":\"none\",\"forest\":\"none\"}");
        WorldLandFrameParser.Validate(city);
        Check("accepts city land with wells and no rural fields or forest",
            city.Water == "well" && city.Fields == "none" && city.Forest == "none");

        WorldLandFrameParser.Validate(null);
        Check("missing land remains compatible with older Layout v2", true);
        ExpectFormat("unknown water style rejected", () =>
            WorldLandFrameParser.Validate(new WorldLandFrame { Water = "sea", Fields = "none", Forest = "none" }));
        ExpectFormat("unknown field style rejected", () =>
            WorldLandFrameParser.Validate(new WorldLandFrame { Water = "well", Fields = "orchard", Forest = "none" }));
        ExpectFormat("unknown forest style rejected", () =>
            WorldLandFrameParser.Validate(new WorldLandFrame { Water = "river", Fields = "strip", Forest = "jungle" }));

        Console.WriteLine(_failures == 0
            ? "WorldLandFrameParser: all checks passed"
            : "WorldLandFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
