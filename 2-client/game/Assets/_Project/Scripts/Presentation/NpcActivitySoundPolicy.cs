namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Small presentation-only sound categories derived from stable server act_code values.
    /// No localized prose or client-inferred goal is used as an audio trigger.
    /// </summary>
    public enum NpcActivitySoundKind
    {
        None = 0,
        Woodcut,
        Forge,
        Fishing,
        Fieldwork,
        Mining,
        Hearth,
        Drinking,
        Herding
    }

    public static class NpcActivitySoundPolicy
    {
        public static NpcActivitySoundKind FromServerCode(string activityCode)
        {
            switch (activityCode)
            {
                case "chop_wood":
                    return NpcActivitySoundKind.Woodcut;
                case "smith_forge":
                    return NpcActivitySoundKind.Forge;
                case "fish_river":
                    return NpcActivitySoundKind.Fishing;
                case "farm_sow":
                case "farm_reap":
                case "farm_tend":
                case "forage_herbs":
                    return NpcActivitySoundKind.Fieldwork;
                case "mine_ore":
                    return NpcActivitySoundKind.Mining;
                case "cook_hearth":
                case "brew_remedy":
                    return NpcActivitySoundKind.Hearth;
                case "drink_tavern":
                    return NpcActivitySoundKind.Drinking;
                case "herd_barn":
                case "herd_graze":
                    return NpcActivitySoundKind.Herding;
                default:
                    // Empty, unknown, idle and explicitly blocked activity codes are silent.
                    return NpcActivitySoundKind.None;
            }
        }
    }
}
