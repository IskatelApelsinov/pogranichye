using System;
using System.Collections.Generic;
using Pogranichye.Client;

internal static class CharacterMenuHotkeyPolicyCheck
{
    private static int _failures;

    private static void Check(string name, bool condition)
    {
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        Check("inventory is bound to I", CharacterMenuHotkeyPolicy.InventoryBinding == "<Keyboard>/i");
        Check("journal and quests are bound to J", CharacterMenuHotkeyPolicy.JournalBinding == "<Keyboard>/j");
        Check("map is bound to M", CharacterMenuHotkeyPolicy.MapBinding == "<Keyboard>/m");
        Check("fishing moved to K to free J for quests", CharacterMenuHotkeyPolicy.FishingBinding == "<Keyboard>/k");

        var bindings = new[]
        {
            CharacterMenuHotkeyPolicy.InventoryBinding,
            CharacterMenuHotkeyPolicy.JournalBinding,
            CharacterMenuHotkeyPolicy.MapBinding,
            CharacterMenuHotkeyPolicy.FishingBinding
        };
        Check("menu and fishing bindings do not collide", new HashSet<string>(bindings).Count == bindings.Length);

        Check("I selects inventory",
            CharacterMenuHotkeyPolicy.Resolve(true, false, false) == CharacterMenuHotkeySection.Inventory);
        Check("J selects journal",
            CharacterMenuHotkeyPolicy.Resolve(false, true, false) == CharacterMenuHotkeySection.Journal);
        Check("M selects map",
            CharacterMenuHotkeyPolicy.Resolve(false, false, true) == CharacterMenuHotkeySection.Map);
        Check("no shortcut leaves current section unchanged",
            CharacterMenuHotkeyPolicy.Resolve(false, false, false) == CharacterMenuHotkeySection.None);
        Check("simultaneous shortcuts resolve deterministically to inventory",
            CharacterMenuHotkeyPolicy.Resolve(true, true, true) == CharacterMenuHotkeySection.Inventory);
        Check("journal wins over map when inventory is not pressed",
            CharacterMenuHotkeyPolicy.Resolve(false, true, true) == CharacterMenuHotkeySection.Journal);

        Console.WriteLine(_failures == 0
            ? "CharacterMenuHotkeyPolicy: 10 checks passed"
            : "CharacterMenuHotkeyPolicy: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
