using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class CharacterLifeFrameParserCheck
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
        const string modeJson = "{\"selected\":\"work\",\"goal\":\"work\",\"act\":\"carpentry\",\"act_code\":\"carpentry\",\"deviates\":false,\"options\":[{\"id\":\"auto\",\"name\":\"Самостоятельно\",\"hint\":\"Жить по обстоятельствам\"},{\"id\":\"work\",\"name\":\"Трудиться\",\"hint\":\"Ремесло и работа\"}]}";
        var modeToken = JToken.Parse(modeJson);
        CharacterModeFrameParser.Validate(modeToken, "me.mode");
        var mode = modeToken.ToObject<CharacterModeFrame>();
        Check("maps server life-mode selection and available options",
            mode != null && mode.Selected == "work" && mode.Options.Count == 2 &&
            mode.Options[0].Id == "auto" && mode.Options[1].Hint == "Ремесло и работа");
        CharacterModeFrameParser.Validate(JValue.CreateNull(), "me.mode");

        const string vigorJson = "{\"energy\":72,\"per_hour\":-2.2,\"why\":\"бодрствует\",\"tips\":[\"отдохни\",\"поспи\"],\"full_in_hours\":null,\"empty_in_hours\":32,\"warning\":\"усталость\",\"stamina\":{\"now\":0.65,\"regen_per_s\":0.02,\"drain_per_s\":0.05,\"full_in_s\":17.5}}";
        var vigorToken = JToken.Parse(vigorJson);
        CharacterVigorFrameParser.Validate(vigorToken, "me.vigor");
        var vigor = vigorToken.ToObject<CharacterVigorFrame>();
        Check("maps server vigor and stamina rates",
            vigor != null && vigor.Energy == 72d && vigor.PerHour == -2.2d &&
            vigor.Tips.Count == 2 && vigor.Stamina != null &&
            vigor.Stamina.Now == 0.65d && vigor.Stamina.DrainPerSecond == 0.05d &&
            vigor.Stamina.FullInSeconds == 17.5d);
        CharacterVigorFrameParser.Validate(JValue.CreateNull(), "me.vigor");

        ExpectFormat("mode options reject malformed ids", () =>
            CharacterModeFrameParser.Validate(JToken.Parse(modeJson.Replace("\"id\":\"auto\"", "\"id\":7\"")), "me.mode"));
        ExpectFormat("vigor rejects non-finite energy", () =>
            CharacterVigorFrameParser.Validate(JToken.Parse(vigorJson.Replace("\"energy\":72", "\"energy\":\"high\"")), "me.vigor"));
        ExpectFormat("vigor tips reject non-string entries", () =>
            CharacterVigorFrameParser.Validate(JToken.Parse(vigorJson.Replace("\"отдохни\",\"поспи\"", "\"отдохни\",2")), "me.vigor"));

        Console.WriteLine(_failures == 0
            ? "CharacterLifeFrameParser: all checks passed"
            : "CharacterLifeFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
