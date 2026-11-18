namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Maps stable server act_code values to visual pose categories.
    /// Unknown values deliberately resolve to neutral idle; goal text and prose are never parsed.
    /// </summary>
    public static class ActivityAnimationPolicy
    {
        public static string Resolve(string activityCode)
        {
            switch (activityCode)
            {
                case "meal": return "eat";
                case "sleep":
                case "rest_idle": return "rest";
                case "drink_tavern": return "drink";
                case "visit_neighbors": return "social";
                case "fish_river": return "fish";
                case "fisher_mend": return "mend_net";
                case "chop_wood": return "chop";
                case "farm_sow":
                case "farm_reap":
                case "farm_tend": return "farm";
                case "farm_mend": return "mend_gear";
                case "farm_nosow":
                case "farm_noreap": return "wait";
                case "smith_forge": return "forge";
                case "mine_ore": return "mine";
                case "mine_spent":
                case "hunter_empty": return "wait";
                case "hunt_wild": return "hunt";
                case "cook_hearth": return "cook";
                case "brew_remedy": return "brew";
                case "forage_herbs": return "forage";
                case "weave": return "weave";
                case "weaver_wait": return "wait";
                case "herd_barn":
                case "herd_graze": return "herd";
                case "keep_tavern": return "tavern";
                case "study_mana": return "study";
                case "smith_cold": return "wait";
                // v1.33 city trades and crafts.
                case "trade_stall": return "trade";
                case "guard_patrol": return "work";
                case "carpentry": return "carpentry";
                case "tanning": return "tanning";
                case "pottery": return "pottery";
                case "bake_bread": return "cook";
                case "scribe_desk": return "study";
                case "jewel_work": return "forge";
                case "work": return "work";
                default: return string.Empty;
            }
        }
    }
}
