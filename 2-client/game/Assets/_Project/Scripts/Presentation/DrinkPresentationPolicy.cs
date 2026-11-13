using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Pure event policy: invitation acknowledgements are not drink presentation events.</summary>
    public static class DrinkPresentationPolicy
    {
        public static bool IsSoloDrinkTransition(bool hasBaseline, bool previousMug, bool currentMug)
        {
            return hasBaseline && previousMug && !currentMug;
        }

        public static bool IsCoDrinkEvent(bool isNewSocialEvent, string eventType)
        {
            return isNewSocialEvent && string.Equals(eventType, "codrink", StringComparison.OrdinalIgnoreCase);
        }
    }
}
