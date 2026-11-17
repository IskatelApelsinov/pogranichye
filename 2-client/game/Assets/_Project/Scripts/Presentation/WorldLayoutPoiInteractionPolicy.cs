using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Compatibility mapping for older Layout v2 POI payloads that predate server interaction descriptors.
    /// New servers should send layout.interactions[] so titles, availability and intents stay server-authored.
    /// </summary>
    public struct WorldLayoutPoiInteraction
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Goal;
        public readonly string ActionIntent;
        public readonly string TargetType;
        public readonly string TargetId;

        public WorldLayoutPoiInteraction(string id, string title, string goal, string actionIntent)
        {
            Id = id;
            Title = title;
            Goal = goal;
            ActionIntent = actionIntent;
            TargetType = "poi";
            TargetId = id;
        }
    }

    public static class WorldLayoutPoiInteractionPolicy
    {
        public static readonly string[] SupportedIds =
        {
            "tavern", "smithy", "market", "granary", "hearth", "garden", "woods", "river", "mine"
        };

        public static bool TryResolve(string id, out WorldLayoutPoiInteraction interaction)
        {
            return TryResolve(id, null, out interaction);
        }

        public static bool TryResolve(string id, string woodsZoneKind, out WorldLayoutPoiInteraction interaction)
        {
            switch (id)
            {
                case "tavern":
                    interaction = new WorldLayoutPoiInteraction(id, "Корчма", null, null);
                    return true;
                case "smithy":
                    interaction = new WorldLayoutPoiInteraction(id, "Кузница · работать у горна", null, "forge");
                    return true;
                case "market":
                    interaction = new WorldLayoutPoiInteraction(id, "Рынок", null, null);
                    return true;
                case "granary":
                    interaction = new WorldLayoutPoiInteraction(id, "Амбар · конюшня и мастерская", null, null);
                    return true;
                case "hearth":
                    interaction = new WorldLayoutPoiInteraction(id, "Очаг · ремесло и готовка", null, null);
                    return true;
                case "garden":
                    interaction = new WorldLayoutPoiInteraction(id, "Огород · собрать урожай и травы", null, "gather");
                    return true;
                case "woods":
                    if (woodsZoneKind == "forest")
                    {
                        interaction = new WorldLayoutPoiInteraction(id, "Лес", null, "chop");
                        return true;
                    }
                    if (woodsZoneKind == "copse")
                    {
                        interaction = new WorldLayoutPoiInteraction(id, "Рощица · хворост", null, "gather_brushwood");
                        return true;
                    }
                    break;
                case "river":
                    interaction = new WorldLayoutPoiInteraction(id, "Река · ловить рыбу", null, "fish");
                    return true;
                case "mine":
                    interaction = new WorldLayoutPoiInteraction(id, "Рудник · добывать руду", null, "mine");
                    return true;
                default:
                    break;
            }

            interaction = default(WorldLayoutPoiInteraction);
            return false;
        }
    }
}
