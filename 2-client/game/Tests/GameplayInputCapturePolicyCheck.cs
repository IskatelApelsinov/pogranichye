using System;
using Pogranichye.Client;

internal static class GameplayInputCapturePolicyCheck
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        Assert(!GameplayInputCapturePolicy.Captures(false, false, false, false),
            "normal world input must remain available");
        Assert(GameplayInputCapturePolicy.Captures(true, false, false, false),
            "character journal must own gameplay input");
        Assert(GameplayInputCapturePolicy.Captures(false, true, false, false),
            "weave wheel must own gameplay input");
        Assert(GameplayInputCapturePolicy.Captures(false, false, true, false),
            "modal menus must own gameplay input");
        Assert(GameplayInputCapturePolicy.Captures(false, false, false, true),
            "chat, contextual panels, minigames and pointer-over-UI must own gameplay input");
        Console.WriteLine("Gameplay input capture policy checks passed.");
    }
}
