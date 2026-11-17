using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;

internal static class JourneyQuestFrameParserCheck
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
        const string journeyJson = "{\"to\":\"settlement-2\",\"to_name\":\"Лесное\",\"progress\":0.45,\"eta\":37,\"scene\":\"Дорога тянется через лес.\",\"note\":\"\",\"follow\":false,\"event\":{\"kind\":\"ambush\",\"foe\":\"bandit\",\"text\":\"На тропе люди с дубьём.\",\"options\":[\"fight\",\"pay\",\"flee\"]},\"via\":[\"Сосновка\"]}";
        var journey = JourneyFrameParser.Parse(JToken.Parse(journeyJson), "me.journey");
        Check("maps public journey view", journey != null && journey.To == "settlement-2" &&
            journey.ToName == "Лесное" && Math.Abs(journey.Progress - 0.45d) < 0.0001d &&
            journey.Eta == 37d && journey.Event != null && journey.Event.Kind == "ambush" &&
            journey.Event.Options.Count == 3 && journey.Via.Count == 1);
        Check("null journey remains null", JourneyFrameParser.Parse(JValue.CreateNull(), "me.journey") == null);
        ExpectFormat("journey progress above one rejected",
            () => JourneyFrameParser.Validate(JToken.Parse(journeyJson.Replace("\"progress\":0.45", "\"progress\":1.5")), "me.journey"));
        ExpectFormat("non-string journey option rejected",
            () => JourneyFrameParser.Validate(JToken.Parse(journeyJson.Replace("\"pay\"", "2")), "me.journey"));

        const string questsJson = "{\"offers\":[{\"id\":\"food:1:2\",\"kind\":\"deliver\",\"via\":\"board\",\"giver\":\"10\",\"giver_name\":\"староста\",\"good\":\"fish\",\"qty\":4,\"text\":\"Принеси рыбу.\",\"coins\":6,\"settlement_id\":\"2\",\"settlement_name\":\"Лесное\",\"realm_name\":\"Святоборская земля\"}],\"active\":[{\"id\":\"cull:wolf:1:2\",\"kind\":\"cull\",\"via\":\"board\",\"giver\":\"10\",\"giver_name\":\"староста\",\"species\":\"wolf\",\"qty\":2,\"text\":\"Убей двух волков.\",\"coins\":8,\"have\":1,\"ready\":false,\"settlement_id\":\"1\",\"settlement_name\":\"Сосновка\",\"realm_name\":\"\"}],\"done\":3,\"rep\":-2}";
        var quests = QuestsFrameParser.Parse(JToken.Parse(questsJson), "me.quests");
        Check("maps offers and active quest views", quests != null && quests.Offers.Count == 1 &&
            quests.Active.Count == 1 && quests.Done == 3 && quests.Reputation == -2 &&
            quests.Offers[0].Good == "fish" && quests.Offers[0].SettlementName == "Лесное" &&
            quests.Offers[0].RealmName == "Святоборская земля" && quests.Active[0].Have == 1 &&
            quests.Active[0].Ready == false && quests.Active[0].SettlementName == "Сосновка");
        Check("null quests remains null", QuestsFrameParser.Parse(JValue.CreateNull(), "me.quests") == null);
        ExpectFormat("fractional quest quantity rejected",
            () => QuestsFrameParser.Validate(JToken.Parse(questsJson.Replace("\"qty\":4", "\"qty\":1.5")), "me.quests"));
        ExpectFormat("negative quest reward rejected",
            () => QuestsFrameParser.Validate(JToken.Parse(questsJson.Replace("\"coins\":6", "\"coins\":-1")), "me.quests"));
        ExpectFormat("non-string offered quest settlement metadata rejected",
            () => QuestsFrameParser.Validate(JToken.Parse(questsJson.Replace("\"settlement_name\":\"Лесное\"", "\"settlement_name\":7")), "me.quests"));
        ExpectFormat("active quest without settlement snapshot rejected",
            () => QuestsFrameParser.Validate(JToken.Parse(questsJson.Replace(",\"settlement_id\":\"1\",\"settlement_name\":\"Сосновка\",\"realm_name\":\"\"", "")), "me.quests"));
        ExpectFormat("non-boolean quest ready rejected",
            () => QuestsFrameParser.Validate(JToken.Parse(questsJson.Replace("\"ready\":false", "\"ready\":1")), "me.quests"));

        Console.WriteLine(_failures == 0
            ? "JourneyQuestFrameParser: all checks passed"
            : "JourneyQuestFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
