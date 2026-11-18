using System;
using Pogranichye.Client.Presentation;

internal static class ActivityAnimationPolicyCheck
{
    private static int _checks;

    private static void Main()
    {
        MapsEveryKnownServerActivity();
        UnknownValuesRemainNeutral();
        Console.WriteLine("Activity animation policy: " + _checks + " checks passed.");
    }

    private static void MapsEveryKnownServerActivity()
    {
        Expect("meal", "eat");
        Expect("sleep", "rest");
        Expect("rest_idle", "rest");
        Expect("drink_tavern", "drink");
        Expect("visit_neighbors", "social");
        Expect("fish_river", "fish");
        Expect("fisher_mend", "mend_net");
        Expect("chop_wood", "chop");
        Expect("farm_sow", "farm");
        Expect("farm_reap", "farm");
        Expect("farm_tend", "farm");
        Expect("farm_mend", "mend_gear");
        Expect("farm_nosow", "wait");
        Expect("farm_noreap", "wait");
        Expect("smith_forge", "forge");
        Expect("mine_ore", "mine");
        Expect("mine_spent", "wait");
        Expect("hunter_empty", "wait");
        Expect("hunt_wild", "hunt");
        Expect("cook_hearth", "cook");
        Expect("brew_remedy", "brew");
        Expect("forage_herbs", "forage");
        Expect("weave", "weave");
        Expect("weaver_wait", "wait");
        Expect("herd_barn", "herd");
        Expect("herd_graze", "herd");
        Expect("keep_tavern", "tavern");
        Expect("study_mana", "study");
        Expect("smith_cold", "wait");
        Expect("trade_stall", "trade");
        Expect("guard_patrol", "work");
        Expect("carpentry", "carpentry");
        Expect("tanning", "tanning");
        Expect("pottery", "pottery");
        Expect("bake_bread", "cook");
        Expect("scribe_desk", "study");
        Expect("jewel_work", "forge");
        Expect("work", "work");
    }

    private static void UnknownValuesRemainNeutral()
    {
        Expect("future_server_activity", string.Empty);
        Expect("Drink_Tavern", string.Empty);
        Expect("drink", string.Empty);
        Expect("goal: chop wood", string.Empty);
        Expect(null, string.Empty);
        Expect(string.Empty, string.Empty);
    }

    private static void Expect(string code, string expected)
    {
        _checks++;
        var actual = ActivityAnimationPolicy.Resolve(code);
        if (actual != expected)
            throw new Exception("Expected act_code '" + (code ?? "<null>") + "' => '" + expected +
                "', got '" + actual + "'.");
    }
}
