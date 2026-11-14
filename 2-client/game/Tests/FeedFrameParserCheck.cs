using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking;

internal static class FeedFrameParserCheck
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
        try
        {
            action();
            Check(name, false);
        }
        catch (FormatException)
        {
            Check(name, true);
        }
    }

    public static int Main()
    {
        var parsed = FeedFrameParser.Parse(JToken.Parse(
            "[{\"seq\":4,\"kind\":\"hit\",\"text\":\"Попадание\",\"foe\":\"foe-7\",\"part\":\"head\",\"dmg\":4.5}," +
            "{\"seq\":5,\"kind\":\"loot\",\"text\":\"Найдено\"}]"), "me.feed");
        Check("typed feed keeps both events", parsed != null && parsed.Count == 2);
        Check("snake-case fields map to typed properties",
            parsed != null && parsed[0].Sequence == 4 && parsed[0].Kind == "hit" &&
            parsed[0].Foe == "foe-7" && parsed[0].Part == "head" && Math.Abs(parsed[0].Damage - 4.5d) < 0.0001d);
        Check("omitted optional fields keep neutral defaults",
            parsed != null && parsed[1].Foe == null && parsed[1].Part == null && parsed[1].Damage == 0d);

        Check("absent feed is accepted", FeedFrameParser.Parse(null, "me.feed") == null);
        Check("explicit null feed is accepted",
            FeedFrameParser.Parse(JValue.CreateNull(), "me.feed") == null);
        FeedFrameParser.Validate(JToken.Parse("[]"), "me.feed");

        ExpectFormat("non-array feed is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("{}"), "me.feed"));
        ExpectFormat("non-object event is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[1]"), "me.feed"));
        ExpectFormat("missing sequence is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[{\"kind\":\"hit\",\"text\":\"x\"}]"), "me.feed"));
        ExpectFormat("duplicate or decreasing sequence is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[{\"seq\":2,\"kind\":\"hit\",\"text\":\"x\"},{\"seq\":2,\"kind\":\"loot\",\"text\":\"y\"}]"), "me.feed"));
        ExpectFormat("non-string kind is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[{\"seq\":1,\"kind\":2,\"text\":\"x\"}]"), "me.feed"));
        ExpectFormat("non-string text is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[{\"seq\":1,\"kind\":\"hit\",\"text\":2}]"), "me.feed"));
        ExpectFormat("non-numeric damage is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[{\"seq\":1,\"kind\":\"hit\",\"text\":\"x\",\"dmg\":\"4\"}]"), "me.feed"));
        ExpectFormat("negative damage is rejected",
            () => FeedFrameParser.Validate(JToken.Parse("[{\"seq\":1,\"kind\":\"hit\",\"text\":\"x\",\"dmg\":-1}]"), "me.feed"));

        FastFramePatch patch;
        var validFast = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"feed\":[{\"seq\":8,\"kind\":\"hurt\",\"text\":\"Удар\",\"dmg\":2}]}}";
        Check("fast parser accepts typed feed", FastFrameParser.TryParse(validFast, out patch));
        Check("fast patch exposes typed feed", patch != null && patch.HasFeed &&
            patch.Feed != null && patch.Feed.Count == 1 && patch.Feed[0].Sequence == 8 && patch.Feed[0].Damage == 2d);

        var noFeed = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"health\":10}}";
        Check("fast patch distinguishes absent feed", FastFrameParser.TryParse(noFeed, out patch) &&
            patch != null && !patch.HasFeed && patch.Feed == null);

        var nullFeed = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"feed\":null}}";
        Check("fast patch preserves explicit null feed", FastFrameParser.TryParse(nullFeed, out patch) &&
            patch != null && patch.HasFeed && patch.Feed == null);

        ExpectFormat("malformed fast feed is rejected",
            () => FastFrameParser.TryParse("{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"feed\":[{\"seq\":1,\"kind\":\"hit\"}]}}", out patch));

        Console.WriteLine(_failures == 0
            ? "FeedFrameParser: all checks passed"
            : "FeedFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
