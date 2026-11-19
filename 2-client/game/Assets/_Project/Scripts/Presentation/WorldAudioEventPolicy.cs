using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Maps result-gate outcomes to one-shot presentation sounds.</summary>
    public static class WorldAudioEventPolicy
    {
        public static bool ShouldPlayFootstep(float horizontalDistanceSquared, bool mounted)
        {
            if (mounted) return false;
            return !float.IsNaN(horizontalDistanceSquared) &&
                !float.IsInfinity(horizontalDistanceSquared) &&
                horizontalDistanceSquared >= 0.0004f;
        }

        public static bool ShouldPlayMagic(string label, bool accepted)
        {
            if (!accepted || string.IsNullOrEmpty(label)) return false;
            const string worldPrefix = "Действие мира:";
            if (!label.StartsWith(worldPrefix, StringComparison.Ordinal)) return false;
            var action = label.Substring(worldPrefix.Length).Trim();
            return string.Equals(action, "cast", StringComparison.Ordinal) ||
                string.Equals(action, "weave", StringComparison.Ordinal);
        }
    }
}
