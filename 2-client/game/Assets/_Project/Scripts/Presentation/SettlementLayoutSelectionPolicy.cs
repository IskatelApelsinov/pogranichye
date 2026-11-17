using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Keeps the tutorial settlement's default server Layout v2 available for gameplay data
    /// even when the active presentation switches to a generated settlement.
    /// </summary>
    public static class SettlementLayoutSelectionPolicy
    {
        public static bool IsTutorialLayout(string plan)
        {
            // The no-id /api/layout endpoint is the tutorial layout on older compatible servers.
            return string.IsNullOrEmpty(plan) || string.Equals(plan, "starter", StringComparison.Ordinal);
        }

        public static bool IsTutorialSettlement(
            string plan, string placeId, string tutorialPlaceId, bool hasSelectedSettlement)
        {
            // An explicit server plan always wins over a remembered place ID.
            if (!string.IsNullOrEmpty(plan))
                return string.Equals(plan, "starter", StringComparison.Ordinal);

            if (!string.IsNullOrEmpty(tutorialPlaceId) &&
                string.Equals(placeId, tutorialPlaceId, StringComparison.Ordinal))
                return true;

            // Before the first snapshot, a missing plan means the legacy tutorial settlement.
            return !hasSelectedSettlement;
        }

        public static T SelectForSettlement<T>(
            bool isTutorialSettlement, T tutorialLayout, T currentLayout) where T : class
        {
            // Never fall back to a generated layout when the tutorial layout is unavailable.
            return isTutorialSettlement ? tutorialLayout : currentLayout;
        }
    }
}
