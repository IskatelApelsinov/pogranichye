using System;
using Newtonsoft.Json.Linq;
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
        const string journeyJson = "{\"to\":\"73\",\"to_name\":\"Берёзовка\",\"progress\":0.42,\"eta\":3600,\"scene\":\"Лесная дорога\",\"note\":\"Встреча позади\",\"follow\":false,\"event\":{\"kind\":\"bandits\",\"foe\":\"bandit-2\",\"text\":\"На дороге люди с оружием\",\"options\":[\"fight\",\"flee\"]},\"via\":[\"68\",\"71\"]}";
        var journeyToken = JToken.Parse(journeyJson);
        JourneyFrameParser.Validate(journeyToken, "me.journey");
        var journey = journeyToken.ToObject<JourneyFrame>();
        Check("maps journey progress and destination", journey.To == "73" && journey.ToName == "Берёзовка" &&
            Math.Abs(journey.Progress - 0.42d) < 0.0001d && journey.Eta == 3600d);
        Check("maps journey event choices", journey.Event != null && journey.Event.Kind == "bandits" &&
            journey.Event.Options.Count == 2 && journey.Event.Options[1] == "flee" && journey.Via.Count == 2);
        JourneyFrameParser.Validate(JValue.CreateNull(), "me.journey");
        Check("null journey is valid", true);

        const string questsJson = "{\"offers\":[{\"id\":\"wood:7:4\",\"kind\":\"deliver\",\"via\":\"board\",\"giver\":\"12\",\"giver_name\":\"староста\",\"good\":\"wood\",\"qty\":4,\"text\":\"Принеси дрова\",\"coins\":4}],\"active\":[{\"id\":\"food:7:4\",\"kind\":\"deliver\",\"via\":\"board\",\"giver\":\"12\",\"giver_name\":\"староста\",\"good\":\"fish\",\"species\":\"\",\"qty\":4,\"have\":2,\"ready\":false,\"text\":\"Принеси рыбу\",\"coins\":6,\"settlement_id\":\"7\",\"settlement_name\":\"Берёзовка\",\"realm_name\":\"Северный край\"}],\"done\":3,\"rep\":5}";
        var questsToken = JToken.Parse(questsJson);
        QuestListFrameParser.Validate(questsToken, "me.quests");
        var quests = questsToken.ToObject<QuestListFrame>();
        Check("maps offers and active quests", quests.Offers.Count == 1 && quests.Active.Count == 1 &&
            quests.Offers[0].Good == "wood" && quests.Active[0].Have == 2d &&
            quests.Active[0].Quantity == 4 && !quests.Active[0].Ready);
        Check("maps quest reputation counters", quests.Done == 3 && quests.Reputation == 5);
        QuestListFrameParser.Validate(JValue.CreateNull(), "me.quests");
        Check("null quests are allowed for older additive v1 frame", true);

        ExpectFormat("journey progress above one rejected",
            () => JourneyFrameParser.Validate(JToken.Parse(journeyJson.Replace("\"progress\":0.42", "\"progress\":1.2")), "me.journey"));
        ExpectFormat("journey event options wrong type rejected",
            () => JourneyFrameParser.Validate(JToken.Parse(journeyJson.Replace("[\"fight\",\"flee\"]", "[1]")), "me.journey"));
        ExpectFormat("negative journey ETA rejected",
            () => JourneyFrameParser.Validate(JToken.Parse(journeyJson.Replace("\"eta\":3600", "\"eta\":-1")), "me.journey"));
        ExpectFormat("quest offer missing id rejected",
            () => QuestListFrameParser.Validate(JToken.Parse(questsJson.Replace("\"id\":\"wood:7:4\",", "")), "me.quests"));
        ExpectFormat("quest quantity fractional rejected",
            () => QuestListFrameParser.Validate(JToken.Parse(questsJson.Replace("\"qty\":4", "\"qty\":1.5")), "me.quests"));
        ExpectFormat("quest ready non-boolean rejected",
            () => QuestListFrameParser.Validate(JToken.Parse(questsJson.Replace("\"ready\":false", "\"ready\":0")), "me.quests"));
        ExpectFormat("quest coins negative rejected",
            () => QuestListFrameParser.Validate(JToken.Parse(questsJson.Replace("\"coins\":6", "\"coins\":-6")), "me.quests"));

        Console.WriteLine(_failures == 0
            ? "JourneyQuestFrameParser: all checks passed"
            : "JourneyQuestFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
