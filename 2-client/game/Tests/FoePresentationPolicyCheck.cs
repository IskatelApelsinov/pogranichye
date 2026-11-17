using System;
using Pogranichye.Client.Presentation;

public static class FoePresentationPolicyCheck
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
        Check(FoePresentationPolicy.IsHumanoid("bandit") &&
              FoePresentationPolicy.IsHumanoid("BANDIT"),
            "server bandit kind selects the humanoid presentation");
        Check(!FoePresentationPolicy.IsHumanoid("wolf") &&
              !FoePresentationPolicy.IsHumanoid("boar") &&
              !FoePresentationPolicy.IsHumanoid(null),
            "animal, unknown and missing kinds remain on the creature presentation");
        Check(FoePresentationPolicy.ResolveDisplayName("bandit", null) == "Разбойник" &&
              FoePresentationPolicy.ResolveDisplayName("bandit", "bandit") == "Разбойник",
            "an unnamed bandit receives a localized fallback name");
        Check(FoePresentationPolicy.ResolveDisplayName("bandit", "Разбойник с тракта") ==
              "Разбойник с тракта",
            "a server-provided display name is preserved");
        Check(FoePresentationPolicy.ResolveDisplayName("wolf", null) == "wolf" &&
              FoePresentationPolicy.ResolveDisplayName(null, null) == "Существо",
            "non-humanoid fallback labels remain safe");
        Check(FoePresentationPolicy.BanditHeight >= 1.8f &&
              FoePresentationPolicy.BanditLimbLength > 0.5f &&
              FoePresentationPolicy.BanditStrideDegrees > 0f,
            "humanoid gait dimensions are valid and non-zero");

        Console.WriteLine(_failed == 0
            ? "FoePresentationPolicy: ALL CHECKS PASSED"
            : "FoePresentationPolicy: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
