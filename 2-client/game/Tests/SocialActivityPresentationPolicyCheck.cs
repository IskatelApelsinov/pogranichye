using System;
using Pogranichye.Client.Presentation;

internal static class SocialActivityPresentationPolicyCheck
{
    private static int _checks;

    private static void Main()
    {
        Assert(SocialActivityPresentationPolicy.IsDrinking("drink_tavern"),
            "server tavern-drink code should trigger a drinking gesture");
        Assert(!SocialActivityPresentationPolicy.IsDrinking("visit_neighbors"),
            "visiting neighbors is not drinking");
        Assert(!SocialActivityPresentationPolicy.IsDrinking("meal"),
            "eating is not drinking");
        Assert(!SocialActivityPresentationPolicy.IsDrinking("work"),
            "generic work is not drinking");
        Assert(!SocialActivityPresentationPolicy.IsDrinking("drink"),
            "presentation category text is not a server code");
        Assert(!SocialActivityPresentationPolicy.IsDrinking("поднимает кружку"),
            "localized prose must never trigger gameplay presentation");
        Assert(!SocialActivityPresentationPolicy.IsDrinking(null),
            "missing activity must remain neutral");
        Console.WriteLine("Social activity presentation policy: " + _checks + " checks passed.");
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
}
