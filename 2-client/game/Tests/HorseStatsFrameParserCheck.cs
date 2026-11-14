using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class HorseStatsFrameParserCheck
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
        const string statsJson = "{\"strength\":10,\"agility\":11.2,\"endurance\":9.8,\"intellect\":10,\"perception\":12,\"will\":10}";
        var statsToken = JToken.Parse(statsJson);
        StatsFrameParser.Validate(statsToken, "me.stats");
        var stats = statsToken.ToObject<StatsFrame>();
        Check("maps all six server stats", stats != null && stats.Strength == 10d &&
            Math.Abs(stats.Agility - 11.2d) < 0.0001d && Math.Abs(stats.Endurance - 9.8d) < 0.0001d &&
            stats.Intellect == 10d && stats.Perception == 12d && stats.Will == 10d &&
            stats.Get("will") == 10d && stats.Get("unknown") == 0d);
        StatsFrameParser.Validate(JValue.CreateNull(), "me.stats");
        Check("null stats accepted for older additive compatibility", true);

        const string bagJson = "{\"load\":24,\"cap\":40,\"over\":false}";
        var bagToken = JToken.Parse(bagJson);
        CarryBagFrameParser.Validate(bagToken, "me.bag");
        var bag = bagToken.ToObject<CarryBagFrame>();
        Check("maps carried bag summary", bag != null && bag.Load == 24d && bag.Capacity == 40d && !bag.Overloaded);
        CarryBagFrameParser.Validate(JValue.CreateNull(), "me.bag");
        CarryBagFrameParser.Validate(null, "me.bag");
        Check("missing or null bag remains compatible with older v1 servers", true);

        const string horseJson = "{\"id\":\"horse-77\",\"name\":\"Сивка\",\"stam\":85,\"mounted\":true,\"here\":true,\"bag\":{\"hay\":2,\"fish\":1},\"bag_load\":3,\"bag_cap\":40}";
        var horseToken = JToken.Parse(horseJson);
        HorseFrameParser.Validate(horseToken, "me.horse");
        var horse = horseToken.ToObject<HorseFrame>();
        Check("maps horse and separate saddle bag", horse != null && horse.Id == "horse-77" &&
            horse.Name == "Сивка" && horse.Stamina == 85d && horse.Mounted && horse.Here &&
            horse.Bag != null && horse.Bag["hay"] == 2d && horse.Bag["fish"] == 1d &&
            horse.BagLoad == 3d && horse.BagCapacity == 40d);
        HorseFrameParser.Validate(JValue.CreateNull(), "me.horse");
        Check("null horse represents no owned horse", true);

        ExpectFormat("missing stat rejected", () =>
            StatsFrameParser.Validate(JToken.Parse(statsJson.Replace(",\"will\":10", "")), "me.stats"));
        ExpectFormat("negative stat rejected", () =>
            StatsFrameParser.Validate(JToken.Parse(statsJson.Replace("\"strength\":10", "\"strength\":-1")), "me.stats"));
        var nonFiniteStats = (JObject)JToken.Parse(statsJson);
        nonFiniteStats["strength"] = new JValue(double.PositiveInfinity);
        ExpectFormat("non-finite stat rejected", () =>
            StatsFrameParser.Validate(nonFiniteStats, "me.stats"));
        ExpectFormat("negative carried load rejected", () =>
            CarryBagFrameParser.Validate(JToken.Parse(bagJson.Replace("\"load\":24", "\"load\":-1")), "me.bag"));
        ExpectFormat("non-boolean overloaded flag rejected", () =>
            CarryBagFrameParser.Validate(JToken.Parse(bagJson.Replace("\"over\":false", "\"over\":0")), "me.bag"));
        ExpectFormat("horse stamina above server range rejected", () =>
            HorseFrameParser.Validate(JToken.Parse(horseJson.Replace("\"stam\":85", "\"stam\":101")), "me.horse"));
        ExpectFormat("horse without stable server id rejected", () =>
            HorseFrameParser.Validate(JToken.Parse(horseJson.Replace("\"id\":\"horse-77\"", "\"id\":7")), "me.horse"));
        ExpectFormat("negative saddle item count rejected", () =>
            HorseFrameParser.Validate(JToken.Parse(horseJson.Replace("\"hay\":2", "\"hay\":-2")), "me.horse"));
        ExpectFormat("missing saddle bag rejected", () =>
            HorseFrameParser.Validate(JToken.Parse(horseJson.Replace(",\"bag\":{\"hay\":2,\"fish\":1}", "")), "me.horse"));
        ExpectFormat("missing carried bag summary field rejected", () =>
            CarryBagFrameParser.Validate(JToken.Parse(bagJson.Replace(",\"over\":false", "")), "me.bag"));

        Console.WriteLine(_failures == 0
            ? "HorseStatsFrameParser: all checks passed"
            : "HorseStatsFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
