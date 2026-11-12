using System;
using Pogranichye.Client.Presentation;

public static class NpcActivitySoundPolicyCheck
{
    private static int _failed;

    private static void Check(bool value, string message)
    {
        if (value) Console.WriteLine("ok:   " + message);
        else
        {
            _failed++;
            Console.WriteLine("FAIL: " + message);
        }
    }

    public static int Main()
    {
        Check(NpcActivitySoundPolicy.FromServerCode("chop_wood") == NpcActivitySoundKind.Woodcut,
            "woodcut activity maps to a wood impact");
        Check(NpcActivitySoundPolicy.FromServerCode("smith_forge") == NpcActivitySoundKind.Forge,
            "active forging maps to a hammer strike");
        Check(NpcActivitySoundPolicy.FromServerCode("fish_river") == NpcActivitySoundKind.Fishing,
            "active fishing maps to a water cue");
        Check(NpcActivitySoundPolicy.FromServerCode("farm_reap") == NpcActivitySoundKind.Fieldwork &&
              NpcActivitySoundPolicy.FromServerCode("farm_sow") == NpcActivitySoundKind.Fieldwork &&
              NpcActivitySoundPolicy.FromServerCode("farm_tend") == NpcActivitySoundKind.Fieldwork,
            "productive field activities share one sound category");
        Check(NpcActivitySoundPolicy.FromServerCode("mine_ore") == NpcActivitySoundKind.Mining,
            "active mining maps to a stone/metal impact");
        Check(NpcActivitySoundPolicy.FromServerCode("cook_hearth") == NpcActivitySoundKind.Hearth &&
              NpcActivitySoundPolicy.FromServerCode("brew_remedy") == NpcActivitySoundKind.Hearth,
            "hearth work maps to one quiet category");
        Check(NpcActivitySoundPolicy.FromServerCode("drink_tavern") == NpcActivitySoundKind.Drinking,
            "server-confirmed tavern drinking maps to a mug cue");
        Check(NpcActivitySoundPolicy.FromServerCode("herd_graze") == NpcActivitySoundKind.Herding &&
              NpcActivitySoundPolicy.FromServerCode("herd_barn") == NpcActivitySoundKind.Herding,
            "herding activities share one category");
        Check(NpcActivitySoundPolicy.FromServerCode("mine_spent") == NpcActivitySoundKind.None &&
              NpcActivitySoundPolicy.FromServerCode("smith_cold") == NpcActivitySoundKind.None &&
              NpcActivitySoundPolicy.FromServerCode("farm_nosow") == NpcActivitySoundKind.None &&
              NpcActivitySoundPolicy.FromServerCode("hunter_empty") == NpcActivitySoundKind.None,
            "blocked and exhausted activities stay silent");
        Check(NpcActivitySoundPolicy.FromServerCode(null) == NpcActivitySoundKind.None &&
              NpcActivitySoundPolicy.FromServerCode("") == NpcActivitySoundKind.None &&
              NpcActivitySoundPolicy.FromServerCode("future_unknown_code") == NpcActivitySoundKind.None,
            "missing and future activity codes safely fall back to silence");

        Console.WriteLine(_failed == 0
            ? "NpcActivitySoundPolicy: ALL CHECKS PASSED"
            : "NpcActivitySoundPolicy: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
