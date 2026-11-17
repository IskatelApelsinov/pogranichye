using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class RegionFrameParserCheck
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
        const string regionJson = "{\"nodes\":[{\"id\":\"1\",\"x\":0,\"y\":0,\"state\":\"here\",\"name\":\"Сосновка\",\"tier\":\"village\"},{\"id\":\"2\",\"x\":120,\"y\":-40,\"state\":\"visited\",\"name\":\"Лесное\",\"tier\":\"village\"},{\"id\":\"3\",\"x\":250,\"y\":15,\"state\":\"unknown\",\"name\":\"\",\"tier\":\"\"}],\"roads\":[{\"a\":\"1\",\"b\":\"2\",\"len\":120},{\"a\":\"1\",\"b\":\"3\",\"len\":250}]}";
        var token = JToken.Parse(regionJson);
        RegionFrameParser.Validate(token, "me.region");
        var region = token.ToObject<RegionFrame>();
        Check("maps known and unknown region nodes", region != null && region.Nodes.Count == 3 &&
            region.Nodes[0].State == "here" && region.Nodes[1].Name == "Лесное" &&
            region.Nodes[2].State == "unknown" && region.Nodes[2].Name == "" &&
            region.Roads.Count == 2 && region.Roads[1].A == "1" && region.Roads[1].Length == 250d);
        RegionFrameParser.Validate(JToken.Parse("{\"nodes\":[],\"roads\":[]}"), "me.region");
        RegionFrameParser.Validate(JValue.CreateNull(), "me.region");
        RegionFrameParser.Validate(null, "me.region");
        Check("empty and absent region remain compatible", true);

        ExpectFormat("unknown place name leak rejected", () =>
            RegionFrameParser.Validate(JToken.Parse(regionJson.Replace("\"name\":\"\",\"tier\":\"\"", "\"name\":\"Секретное\",\"tier\":\"city\"")), "me.region"));
        ExpectFormat("missing current place rejected", () =>
            RegionFrameParser.Validate(JToken.Parse(regionJson.Replace("\"state\":\"here\"", "\"state\":\"visited\"")), "me.region"));
        ExpectFormat("duplicate region node id rejected", () =>
            RegionFrameParser.Validate(JToken.Parse(regionJson.Replace("\"id\":\"3\"", "\"id\":\"2\"")), "me.region"));
        ExpectFormat("road endpoint outside visible knowledge rejected", () =>
            RegionFrameParser.Validate(JToken.Parse(regionJson.Replace("\"b\":\"3\"", "\"b\":\"404\"")), "me.region"));
        ExpectFormat("negative road length rejected", () =>
            RegionFrameParser.Validate(JToken.Parse(regionJson.Replace("\"len\":120", "\"len\":-1")), "me.region"));
        ExpectFormat("unknown node state rejected", () =>
            RegionFrameParser.Validate(JToken.Parse(regionJson.Replace("\"state\":\"visited\"", "\"state\":\"secret\"")), "me.region"));

        var nonFinite = (JObject)JToken.Parse(regionJson);
        nonFinite["nodes"][0]["x"] = new JValue(double.PositiveInfinity);
        ExpectFormat("non-finite region coordinate rejected", () =>
            RegionFrameParser.Validate(nonFinite, "me.region"));

        Console.WriteLine(_failures == 0
            ? "RegionFrameParser: all checks passed"
            : "RegionFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
