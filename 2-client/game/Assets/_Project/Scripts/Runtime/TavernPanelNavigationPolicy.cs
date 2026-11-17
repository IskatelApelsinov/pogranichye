namespace Pogranichye.Client
{
    public enum TavernPanelScreen
    {
        Home = 0,
        CardGames = 1,
        ActiveGame = 2
    }

    /// <summary>
    /// Navigation rules for the nested tavern UI. Leaving a screen never changes
    /// the server-owned card-game state or sends a gameplay intent.
    /// </summary>
    public static class TavernPanelNavigationPolicy
    {
        public static bool HasBackButton(TavernPanelScreen screen)
        {
            return screen == TavernPanelScreen.CardGames ||
                screen == TavernPanelScreen.ActiveGame;
        }

        public static TavernPanelScreen Back(TavernPanelScreen current, out bool closePanel)
        {
            closePanel = false;
            switch (current)
            {
                case TavernPanelScreen.ActiveGame:
                    return TavernPanelScreen.CardGames;
                case TavernPanelScreen.CardGames:
                    return TavernPanelScreen.Home;
                default:
                    closePanel = true;
                    return TavernPanelScreen.Home;
            }
        }

        public static bool CanStartGame(bool panelOpen, TavernPanelScreen screen, bool gameInProgress)
        {
            return panelOpen && screen == TavernPanelScreen.CardGames && !gameInProgress;
        }

        public static bool CanUseGameControls(bool panelOpen, TavernPanelScreen screen, bool gameInProgress)
        {
            return panelOpen && screen == TavernPanelScreen.ActiveGame && gameInProgress;
        }
    }
}
