using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Explicit allow-list mapping for server Layout v2 POIs that already have a supported
    /// v1 player intention. Unknown POIs deliberately remain non-interactive.
    /// </summary>
    public struct WorldLayoutPoiInteraction
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Goal;
        public readonly string ActionIntent;

        public WorldLayoutPoiInteraction(string id, string title, string goal, string actionIntent)
        {
            Id = id;
            Title = title;
            Goal = goal;
            ActionIntent = actionIntent;
        }
    }

    public static class WorldLayoutPoiInteractionPolicy
    {
        public static readonly string[] SupportedIds =
        {
            "tavern", "smithy", "market", "garden", "woods", "river", "mine"
        };

        public static bool TryResolve(string id, out WorldLayoutPoiInteraction interaction)
        {
            switch (id)
            {
                case "tavern":
                    interaction = new WorldLayoutPoiInteraction(id, "Корчма", "socialize", null);
                    return true;
                case "smithy":
                    interaction = new WorldLayoutPoiInteraction(id, "Кузница · работать у горна", null, "forge");
                    return true;
                case "market":
                    interaction = new WorldLayoutPoiInteraction(id, "Рынок", "eat", null);
                    return true;
                case "garden":
                    interaction = new WorldLayoutPoiInteraction(id, "Огород · собрать урожай и травы", null, "gather");
                    return true;
                case "woods":
                    interaction = new WorldLayoutPoiInteraction(id, "Лес · рубить деревья", null, "chop");
                    return true;
                case "river":
                    interaction = new WorldLayoutPoiInteraction(id, "Река · ловить рыбу", null, "fish");
                    return true;
                case "mine":
                    interaction = new WorldLayoutPoiInteraction(id, "Рудник · добывать руду", null, "mine");
                    return true;
                default:
                    interaction = default(WorldLayoutPoiInteraction);
                    return false;
            }
        }
    }
}
