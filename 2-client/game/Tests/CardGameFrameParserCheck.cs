using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class CardGameFrameParserCheck
{
    private static int _checks;

    private static void Check(string name, bool condition)
    {
        _checks++;
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("ok: " + name);
    }

    private static void ExpectFormat(string name, Action action)
    {
        try { action(); }
        catch (FormatException) { Check(name, true); return; }
        Check(name, false);
    }

    public static int Main()
    {
        const string ochko = "{\"game\":\"ochko\",\"phase\":\"playing\",\"stake\":10,\"done\":false,\"result\":\"\",\"message\":\"Твой ход\",\"player\":[{\"c\":\"7♠\",\"v\":7}],\"dealer\":[{\"c\":\"9♣\",\"v\":9}],\"ptotal\":7,\"dtotal\":null,\"hidden\":1}";
        var ochkoToken = JToken.Parse(ochko);
        CardGameFrameParser.Validate(ochkoToken, "me.card_game");
        var ochkoFrame = ochkoToken.ToObject<CardGameFrame>();
        Check("maps the public Ochko hand and one dealer up-card",
            ochkoFrame != null && ochkoFrame.Game == "ochko" && ochkoFrame.Player.Count == 1 &&
            ochkoFrame.Dealer.Count == 1 && ochkoFrame.HiddenDealerCards == 1 && !ochkoFrame.Done);

        const string finishedOchko = "{\"game\":\"ochko\",\"phase\":\"done\",\"stake\":10,\"done\":true,\"result\":\"win\",\"message\":\"Победа\",\"player\":[{\"c\":\"7♠\",\"v\":7}],\"dealer\":[{\"c\":\"9♣\",\"v\":9},{\"c\":\"8♦\",\"v\":8}],\"ptotal\":7,\"dtotal\":17,\"hidden\":0}";
        CardGameFrameParser.Validate(JToken.Parse(finishedOchko), "me.card_game");
        Check("allows dealer cards only after Ochko settles", true);

        const string durak = "{\"game\":\"durak\",\"phase\":\"attack\",\"stake\":10,\"done\":false,\"result\":\"\",\"message\":\"Твой заход\",\"player\":[{\"c\":\"6♠\",\"rank\":6,\"suit\":\"spades\"}],\"dealer_cards\":6,\"table\":[{\"a\":{\"c\":\"7♥\",\"rank\":7,\"suit\":\"hearts\"},\"d\":null}],\"trump\":\"clubs\",\"trump_card\":\"6♣\",\"deck\":23,\"attacker\":\"player\",\"role\":\"attack\",\"your_turn\":true}";
        var durakToken = JToken.Parse(durak);
        CardGameFrameParser.Validate(durakToken, "me.card_game");
        var durakFrame = durakToken.ToObject<CardGameFrame>();
        Check("maps Durak role, turn, own hand and open table",
            durakFrame != null && durakFrame.Game == "durak" && durakFrame.YourTurn &&
            durakFrame.Player.Count == 1 && durakFrame.Table.Count == 1 &&
            durakFrame.Table[0].Attack.Code == "7♥" && durakFrame.Table[0].Defence == null &&
            durakFrame.DealerCardCount == 6 && durakFrame.Dealer == null);

        ExpectFormat("rejects a full hidden Ochko dealer hand before settlement", () =>
            CardGameFrameParser.Validate(JToken.Parse(ochko.Replace("\"dealer\":[{\"c\":\"9♣\",\"v\":9}]", "\"dealer\":[{\"c\":\"9♣\",\"v\":9},{\"c\":\"8♦\",\"v\":8}]")), "me.card_game"));
        ExpectFormat("rejects private Durak dealer cards", () =>
            CardGameFrameParser.Validate(JToken.Parse(durak.Replace("\"dealer_cards\":6", "\"dealer\":[{\"c\":\"A♠\",\"rank\":14,\"suit\":\"spades\"}],\"dealer_cards\":6")), "me.card_game"));
        ExpectFormat("rejects invalid Durak rank", () =>
            CardGameFrameParser.Validate(JToken.Parse(durak.Replace("\"rank\":6", "\"rank\":2")), "me.card_game"));
        ExpectFormat("rejects a turn flag inconsistent with the current phase", () =>
            CardGameFrameParser.Validate(JToken.Parse(durak.Replace("\"your_turn\":true", "\"your_turn\":false")), "me.card_game"));
        ExpectFormat("rejects malformed open table pair", () =>
            CardGameFrameParser.Validate(JToken.Parse(durak.Replace("\"a\":{\"c\":\"7♥\",\"rank\":7,\"suit\":\"hearts\"}", "\"a\":null")), "me.card_game"));
        ExpectFormat("rejects unknown game", () =>
            CardGameFrameParser.Validate(JToken.Parse(ochko.Replace("\"ochko\"", "\"poker\"")), "me.card_game"));
        Check("missing additive card_game is compatible with older v1 frames", ValidateNull());

        Console.WriteLine("CardGameFrameParser: " + _checks + " checks passed.");
        return 0;
    }

    private static bool ValidateNull()
    {
        CardGameFrameParser.Validate(JValue.CreateNull(), "me.card_game");
        return true;
    }
}
