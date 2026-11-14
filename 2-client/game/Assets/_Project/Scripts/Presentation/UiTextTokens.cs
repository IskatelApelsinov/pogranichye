using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Single source of IMGUI / world-label text sizes for the thick client.
    /// Keeps HUD readable and stops ad-hoc fontSize 8…62 drift (comms 0246, task 014).
    /// </summary>
    public static class UiTextTokens
    {
        public const int HudPrimary = 16;
        public const int HudSecondary = 14;
        public const int HudButton = 14;
        public const int Toast = 15;
        public const int Prompt = 16;
        public const int Debug = 11;
        public const int WorldNameplate = 32;

        public static readonly Color HudText = new Color(0.88f, 0.85f, 0.76f, 1f);
        public static readonly Color ToastText = new Color(0.96f, 0.90f, 0.78f, 1f);
        public static readonly Color PromptText = new Color(0.96f, 0.88f, 0.70f, 1f);
        public static readonly Color PanelBg = new Color(0.10f, 0.085f, 0.065f, 0.90f);
    }
}
