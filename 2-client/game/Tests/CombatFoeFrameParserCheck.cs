using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;

internal static class CombatFoeFrameParserCheck
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
        const string combatJson = "{\"stance\":\"balanced\",\"guard\":true,\"sneak\":false,\"feint\":true,\"cd\":0.5,\"shield\":12,\"light\":4,\"arrows\":8,\"weapon\":\"bow\",\"kills\":2,\"ko\":false}";
        var combat = CombatFrameParser.Parse(JToken.Parse(combatJson), "me.combat");
        Check("maps server combat fields", combat != null && combat.Stance == "balanced" &&
            combat.Guard && !combat.Sneak && combat.Feint && combat.Arrows == 8 &&
            combat.Weapon == "bow" && combat.Kills == 2 && !combat.KnockedOut &&
            Math.Abs(combat.Cooldown - 0.5d) < 0.0001d);

        const string foesJson = "[{\"id\":\"wolf-1\",\"kind\":\"wolf\",\"name\":\"Волк\",\"x\":3,\"y\":-2,\"facing\":90,\"hp\":24,\"max\":30,\"state\":\"windup\",\"aware\":true,\"slow\":false,\"weak\":true,\"blind\":false,\"stun\":false,\"windup\":0.45}]";
        var foes = FoeFrameParser.Parse(JToken.Parse(foesJson), "place.foes");
        Check("maps server foe fields", foes != null && foes.Count == 1 &&
            foes[0].Id == "wolf-1" && foes[0].Kind == "wolf" && foes[0].State == "windup" &&
            foes[0].Aware && foes[0].Weak == true && Math.Abs(foes[0].Windup.Value - 0.45d) < 0.0001d);
        Check("null foe list remains null", FoeFrameParser.Parse(JValue.CreateNull(), "place.foes") == null);
        Check("null combat remains null", CombatFrameParser.Parse(JValue.CreateNull(), "me.combat") == null);

        ExpectFormat("combat missing guard rejected",
            () => CombatFrameParser.Validate(JToken.Parse("{\"stance\":\"balanced\",\"sneak\":false,\"feint\":false,\"cd\":0,\"shield\":0,\"light\":0,\"arrows\":0,\"weapon\":\"\",\"kills\":0,\"ko\":false}"), "me.combat"));
        ExpectFormat("negative cooldown rejected",
            () => CombatFrameParser.Validate(JToken.Parse(combatJson.Replace("\"cd\":0.5", "\"cd\":-1")), "me.combat"));
        ExpectFormat("fractional arrows rejected",
            () => CombatFrameParser.Validate(JToken.Parse(combatJson.Replace("\"arrows\":8", "\"arrows\":1.5")), "me.combat"));
        ExpectFormat("non-string foe id rejected",
            () => FoeFrameParser.Validate(JToken.Parse("[{\"id\":7,\"kind\":\"wolf\",\"name\":\"Wolf\",\"x\":0,\"y\":0,\"facing\":0,\"hp\":1,\"max\":1,\"state\":\"roam\",\"aware\":false}]"), "place.foes"));
        ExpectFormat("negative foe health rejected",
            () => FoeFrameParser.Validate(JToken.Parse(foesJson.Replace("\"hp\":24", "\"hp\":-1")), "place.foes"));
        ExpectFormat("duplicate foe IDs rejected",
            () => FoeFrameParser.Validate(JToken.Parse(foesJson.Replace("}]","},{\"id\":\"wolf-1\",\"kind\":\"wolf\",\"name\":\"Волк\",\"x\":0,\"y\":0,\"facing\":0,\"hp\":1,\"max\":1,\"state\":\"roam\",\"aware\":false}]")), "place.foes"));
        ExpectFormat("wrong optional foe flag rejected",
            () => FoeFrameParser.Validate(JToken.Parse(foesJson.Replace("\"slow\":false", "\"slow\":1")), "place.foes"));

        const string fastJson = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"combat\":" + combatJson + "},\"place\":{\"foes\":" + foesJson + "}}";
        FastFramePatch patch;
        Check("fast combat and foes parsed", FastFrameParser.TryParse(fastJson, out patch) &&
            patch != null && patch.HasCombat && patch.Combat != null && patch.Combat.Weapon == "bow" &&
            patch.HasFoes && patch.Foes != null && patch.Foes.Count == 1 && patch.Foes[0].Id == "wolf-1");
        Check("fast combat/foes omission preserved", FastFrameParser.TryParse(
            "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"health\":90},\"place\":{}}", out patch) &&
            patch != null && !patch.HasCombat && !patch.HasFoes);
        Check("fast explicit null distinguished", FastFrameParser.TryParse(
            "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"combat\":null},\"place\":{\"foes\":null}}", out patch) &&
            patch != null && patch.HasCombat && patch.Combat == null && patch.HasFoes && patch.Foes == null);

        Console.WriteLine(_failures == 0
            ? "CombatFoeFrameParser: all checks passed"
            : "CombatFoeFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
