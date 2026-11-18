using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Maps result-gate outcomes to one-shot presentation sounds.</summary>
    public static class WorldAudioEventPolicy
    {
        public static bool ShouldPlayMagic(string label, bool accepted)
        {
            if (!accepted || string.IsNullOrEmpty(label)) return false;
            if (string.Equals(label, "Плетение", StringComparison.Ordinal)) return true;
            const string worldPrefix = "Действие мира:";
            if (!label.StartsWith(worldPrefix, StringComparison.Ordinal)) return false;
            var action = label.Substring(worldPrefix.Length).Trim();
            return string.Equals(action, "cast", StringComparison.Ordinal) ||
                string.Equals(action, "weave", StringComparison.Ordinal);
        }
    }
}
