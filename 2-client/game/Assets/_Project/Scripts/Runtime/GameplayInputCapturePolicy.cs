namespace Pogranichye.Client
{
    /// <summary>Single policy for deciding whether UI owns gameplay input.</summary>
    public static class GameplayInputCapturePolicy
    {
        public static bool Captures(bool characterMenuOpen, bool weaveOpen,
            bool modalMenuOpen, bool externalUiCapture)
        {
            return characterMenuOpen || weaveOpen || modalMenuOpen || externalUiCapture;
        }
    }
}
