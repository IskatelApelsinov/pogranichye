using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class TavernCounterFrameParserCheck
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
        var valid = JToken.Parse("{\"x\":-33,\"y\":20,\"radius\":2.2,\"bartender_present\":true}");
        TavernCounterFrameParser.Validate(valid, "place.tavern_counter");
        var frame = valid.ToObject<TavernCounterFrame>();
        Check("maps server counter position, radius and keeper presence", frame != null &&
            frame.X == -33d && frame.Y == 20d && Math.Abs(frame.Radius - 2.2d) < 0.0001d &&
            frame.BartenderPresent);

        TavernCounterFrameParser.Validate(null, "place.tavern_counter");
        TavernCounterFrameParser.Validate(JValue.CreateNull(), "place.tavern_counter");
        Check("missing and null counter remain compatible with older v1 snapshots", true);

        ExpectFormat("array rejected", () =>
            TavernCounterFrameParser.Validate(new JArray(), "place.tavern_counter"));
        ExpectFormat("missing x rejected", () =>
            TavernCounterFrameParser.Validate(JToken.Parse("{\"y\":20,\"radius\":2.2,\"bartender_present\":true}"), "place.tavern_counter"));
        ExpectFormat("string x rejected", () =>
            TavernCounterFrameParser.Validate(JToken.Parse("{\"x\":\"-33\",\"y\":20,\"radius\":2.2,\"bartender_present\":true}"), "place.tavern_counter"));
        ExpectFormat("zero radius rejected", () =>
            TavernCounterFrameParser.Validate(JToken.Parse("{\"x\":-33,\"y\":20,\"radius\":0,\"bartender_present\":true}"), "place.tavern_counter"));
        ExpectFormat("excessive radius rejected", () =>
            TavernCounterFrameParser.Validate(JToken.Parse("{\"x\":-33,\"y\":20,\"radius\":11,\"bartender_present\":true}"), "place.tavern_counter"));
        ExpectFormat("non-boolean keeper state rejected", () =>
            TavernCounterFrameParser.Validate(JToken.Parse("{\"x\":-33,\"y\":20,\"radius\":2.2,\"bartender_present\":1}"), "place.tavern_counter"));
        var nonFinite = JObject.Parse("{\"x\":-33,\"y\":20,\"radius\":2.2,\"bartender_present\":true}");
        nonFinite["x"] = new JValue(double.PositiveInfinity);
        ExpectFormat("non-finite coordinate rejected", () =>
            TavernCounterFrameParser.Validate(nonFinite, "place.tavern_counter"));

        Console.WriteLine(_failures == 0
            ? "TavernCounterFrameParser: all checks passed"
            : "TavernCounterFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
