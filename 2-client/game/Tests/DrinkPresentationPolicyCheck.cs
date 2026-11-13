using System;
using Pogranichye.Client.Presentation;

public static class DrinkPresentationPolicyCheck
{
    public static int Main()
    {
        Check(!DrinkPresentationPolicy.IsSoloDrinkTransition(false, true, false), "first snapshot must not play a sip");
        Check(!DrinkPresentationPolicy.IsSoloDrinkTransition(true, false, true), "buying a mug must not play a sip");
        Check(DrinkPresentationPolicy.IsSoloDrinkTransition(true, true, false), "consuming a held mug must play one sip");
        Check(!DrinkPresentationPolicy.IsSoloDrinkTransition(true, false, false), "empty mug state must stay silent");
        Check(DrinkPresentationPolicy.IsCoDrinkEvent(true, "codrink"), "new co-drink event must play the toast");
        Check(DrinkPresentationPolicy.IsCoDrinkEvent(true, "CODRINK"), "event type comparison must be case-insensitive");
        Check(!DrinkPresentationPolicy.IsCoDrinkEvent(false, "codrink"), "retained old event must not replay");
        Check(!DrinkPresentationPolicy.IsCoDrinkEvent(true, "invite"), "invite acknowledgement is not a drink event");
        Check(!DrinkPresentationPolicy.IsCoDrinkEvent(true, null), "missing event type must stay silent");
        Console.WriteLine("drink-presentation-policy: all checks passed");
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
