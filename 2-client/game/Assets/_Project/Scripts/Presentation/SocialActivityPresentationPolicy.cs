using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Pure helpers for server activity-driven social presentation.</summary>
    public static class SocialActivityPresentationPolicy
    {
        public static bool IsDrinking(string serverActivityCode)
        {
            return string.Equals(serverActivityCode, "drink_tavern", StringComparison.Ordinal);
        }
    }
}
