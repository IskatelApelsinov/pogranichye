using System;

namespace Pogranichye.Client
{
    /// <summary>
    /// Resolves the shared Input System event-byte budget without replacing an explicit
    /// unlimited setting or reducing a larger project-level value.
    /// </summary>
    public static class InputEventBudgetPolicy
    {
        public const int MinimumSafeBytesPerUpdate = 8 * 1024 * 1024;

        public static int Resolve(int currentBytesPerUpdate, int configuredMinimumBytesPerUpdate)
        {
            // Unity Input System uses exactly zero to disable the per-update byte limit.
            // Negative values are invalid, not an unlimited sentinel, so normalize them
            // to the safe finite minimum instead of preserving a broken configuration.
            if (currentBytesPerUpdate == 0)
                return 0;

            var requestedMinimum = Math.Max(
                MinimumSafeBytesPerUpdate,
                configuredMinimumBytesPerUpdate);
            return Math.Max(currentBytesPerUpdate, requestedMinimum);
        }
    }
}
