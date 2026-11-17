using System;
using Pogranichye.Client;

internal static class TavernPanelNavigationPolicyCheck
{
    private static int _failures;
    private static int _checks;

    private static void Check(string name, bool condition)
    {
        _checks++;
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        Check("tavern home has no Back button",
            !TavernPanelNavigationPolicy.HasBackButton(TavernPanelScreen.Home));
        Check("card-game selection has Back",
            TavernPanelNavigationPolicy.HasBackButton(TavernPanelScreen.CardGames));
        Check("active game has Back",
            TavernPanelNavigationPolicy.HasBackButton(TavernPanelScreen.ActiveGame));

        bool closePanel;
        var next = TavernPanelNavigationPolicy.Back(TavernPanelScreen.ActiveGame, out closePanel);
        Check("Back from active game returns to game selection",
            next == TavernPanelScreen.CardGames && !closePanel);
        next = TavernPanelNavigationPolicy.Back(TavernPanelScreen.CardGames, out closePanel);
        Check("Back from game selection returns to tavern home",
            next == TavernPanelScreen.Home && !closePanel);
        next = TavernPanelNavigationPolicy.Back(TavernPanelScreen.Home, out closePanel);
        Check("Back from tavern home closes the context panel",
            next == TavernPanelScreen.Home && closePanel);

        Check("new game can start only from selection with no active game",
            TavernPanelNavigationPolicy.CanStartGame(true, TavernPanelScreen.CardGames, false));
        Check("closed tavern cannot start a game",
            !TavernPanelNavigationPolicy.CanStartGame(false, TavernPanelScreen.CardGames, false));
        Check("active game cannot be replaced by another start",
            !TavernPanelNavigationPolicy.CanStartGame(true, TavernPanelScreen.CardGames, true));
        Check("tavern home cannot start a game from selection hotkeys",
            !TavernPanelNavigationPolicy.CanStartGame(true, TavernPanelScreen.Home, false));

        Check("game controls are available on the active-game screen",
            TavernPanelNavigationPolicy.CanUseGameControls(true, TavernPanelScreen.ActiveGame, true));
        Check("game controls are unavailable after the game is done",
            !TavernPanelNavigationPolicy.CanUseGameControls(true, TavernPanelScreen.ActiveGame, false));
        Check("game controls are unavailable when the tavern is closed",
            !TavernPanelNavigationPolicy.CanUseGameControls(false, TavernPanelScreen.ActiveGame, true));
        Check("game controls are unavailable on the tavern home screen",
            !TavernPanelNavigationPolicy.CanUseGameControls(true, TavernPanelScreen.Home, true));

        Console.WriteLine(_failures == 0
            ? "TavernPanelNavigationPolicy: " + _checks + " checks passed"
            : "TavernPanelNavigationPolicy: FAILED " + _failures + " / " + _checks);
        return _failures == 0 ? 0 : 1;
    }
}
