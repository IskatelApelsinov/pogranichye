namespace Pogranichye.Client
{
    public enum CharacterMenuHotkeySection
    {
        None = 0,
        Inventory,
        Journal,
        Map
    }

    /// <summary>
    /// Shared keyboard bindings and deterministic resolution for direct character-menu access.
    /// Kept free of Unity dependencies so the binding contract can be regression-tested in CI.
    /// </summary>
    public static class CharacterMenuHotkeyPolicy
    {
        public const string InventoryBinding = "<Keyboard>/i";
        public const string JournalBinding = "<Keyboard>/j";
        public const string MapBinding = "<Keyboard>/m";
        public const string FishingBinding = "<Keyboard>/k";

        public static CharacterMenuHotkeySection Resolve(
            bool inventoryPressed,
            bool journalPressed,
            bool mapPressed)
        {
            // A single deterministic winner avoids frame-dependent tab switching when keys overlap.
            if (inventoryPressed) return CharacterMenuHotkeySection.Inventory;
            if (journalPressed) return CharacterMenuHotkeySection.Journal;
            if (mapPressed) return CharacterMenuHotkeySection.Map;
            return CharacterMenuHotkeySection.None;
        }
    }
}
