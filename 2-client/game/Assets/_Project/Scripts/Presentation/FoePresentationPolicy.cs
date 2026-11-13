using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Pure presentation rules for server-owned combat entities.</summary>
    public static class FoePresentationPolicy
    {
        public const float BanditHeight = 1.9f;
        public const float BanditLimbLength = 0.64f;
        public const float BanditStrideDegrees = 24f;

        public static bool IsHumanoid(string kind)
        {
            return string.Equals(kind, "bandit", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveDisplayName(string kind, string serverName)
        {
            if (!string.IsNullOrWhiteSpace(serverName) &&
                !string.Equals(serverName.Trim(), kind, StringComparison.OrdinalIgnoreCase))
                return serverName.Trim();

            if (IsHumanoid(kind)) return "Разбойник";
            return string.IsNullOrWhiteSpace(kind) ? "Существо" : kind;
        }
    }
}
