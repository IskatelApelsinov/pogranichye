using System;
using System.Collections.Generic;

namespace Pogranichye.Client.Networking
{
    /// <summary>Pure compatibility gates for additive Layout v1.33 plans and POI sets.</summary>
    public static class WorldLayoutCompatibilityPolicy
    {
        private static readonly string[] RuralPois = { "woods", "garden", "mine", "river" };

        public static bool IsSupportedPlan(string plan)
        {
            return string.IsNullOrEmpty(plan) || plan == "starter" || plan == "street" ||
                plan == "cross" || plan == "ring" || plan == "scatter" || plan == "grid";
        }

        public static bool HasRequiredPois(string plan, ICollection<string> poiIds)
        {
            if (poiIds == null || !poiIds.Contains("tavern") || !poiIds.Contains("well")) return false;
            if (string.Equals(plan, "grid", StringComparison.Ordinal))
                return poiIds.Contains("hall");
            foreach (var id in RuralPois)
                if (!poiIds.Contains(id)) return false;
            return true;
        }
    }
}
