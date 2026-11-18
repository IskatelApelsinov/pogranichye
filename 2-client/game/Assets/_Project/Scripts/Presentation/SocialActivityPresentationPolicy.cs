namespace Pogranichye.Client.Presentation
{
    /// <summary>Pure helpers for server activity-driven social presentation.</summary>
    public static class SocialActivityPresentationPolicy
    {
        public static bool IsDrinking(string serverActivityCode)
        {
            return ActivityAnimationPolicy.Resolve(serverActivityCode) == "drink";
        }
    }
}
