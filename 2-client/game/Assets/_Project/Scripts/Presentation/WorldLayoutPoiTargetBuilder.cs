using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Projects server-authored interaction descriptors (or legacy server POIs) into invisible E targets.
    /// These targets are input volumes only: they never create world entities or perform gameplay locally.
    /// </summary>
    public static class WorldLayoutPoiTargetBuilder
    {
        public static void Build(Transform root, WorldLayoutFrame layout)
        {
            if (root == null || layout == null)
                return;

            // New server-authored descriptors take precedence over the legacy POI intent map.
            // Their coordinates may represent a counter, workstation, bench or interior object.
            if (layout.Interactions != null)
            {
                foreach (var descriptor in layout.Interactions)
                {
                    if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Id) ||
                        !IsFinite(descriptor.X) || !IsFinite(descriptor.Y))
                        continue;

                    var target = new GameObject("Server interaction target • " + descriptor.Id);
                    target.transform.SetParent(root, false);
                    target.transform.localPosition = new Vector3((float)descriptor.X, 0.9f, (float)descriptor.Y);
                    var hitArea = target.AddComponent<SphereCollider>();
                    hitArea.radius = Mathf.Clamp(IsFinite(descriptor.Radius) && descriptor.Radius > 0d
                        ? (float)descriptor.Radius : 0.9f, 0.35f, 3.5f);
                    hitArea.isTrigger = true;
                    var poi = target.AddComponent<WorldPoi>();
                    var targetPoiId = string.IsNullOrWhiteSpace(descriptor.TargetId)
                        ? descriptor.Id : descriptor.TargetId;
                    var targetZoneKind = targetPoiId == "woods" ? ResolveWoodsZoneKind(layout) : null;
                    if (WorldLayoutPoiInteractionPolicy.TryResolve(targetPoiId, targetZoneKind, out var mapped))
                    {
                        // Prefer the already-supported op:"act"/goal path for known village work.
                        // The generic op:"interact" descriptor is not yet guaranteed to have a
                        // server handler on every deployed v1 server.
                        poi.Initialize(
                            string.IsNullOrWhiteSpace(descriptor.Title) ? mapped.Title : descriptor.Title,
                            mapped.Goal, null, Color.clear, mapped.ActionIntent, null, descriptor.Hint,
                            descriptor.Available, descriptor.Reason, descriptor.TargetType, descriptor.TargetId);
                    }
                    else
                    {
                        // Unknown server descriptors retain their explicit interaction ID;
                        // they are never translated into a guessed client-side activity.
                        poi.Initialize(
                            string.IsNullOrWhiteSpace(descriptor.Title) ? "Взаимодействие" : descriptor.Title,
                            null, null, Color.clear, null, descriptor.Id, descriptor.Hint,
                            descriptor.Available, descriptor.Reason, descriptor.TargetType, descriptor.TargetId);
                    }
                }
                return;
            }

            // Compatibility path for older servers: coordinates still come from server Layout POIs.
            if (layout.Pois == null) return;
            var woodsZoneKind = ResolveWoodsZoneKind(layout);
            var ids = WorldLayoutPoiInteractionPolicy.SupportedIds;
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (!layout.Pois.TryGetValue(id, out var point) || point == null ||
                    !IsFinite(point.X) || !IsFinite(point.Y) || layout.IsBlocked(point.X, point.Y))
                    continue;

                var zoneKind = id == "woods" ? woodsZoneKind : null;
                if (!WorldLayoutPoiInteractionPolicy.TryResolve(id, zoneKind, out var interaction))
                    continue;

                var target = new GameObject("E interaction target • " + id);
                target.transform.SetParent(root, false);
                target.transform.localPosition = new Vector3((float)point.X, 0.9f, (float)point.Y);

                var hitArea = target.AddComponent<SphereCollider>();
                hitArea.radius = 0.85f;
                // Interaction volumes must never become invisible physical obstacles.
                // WorldInteractionProbe explicitly includes triggers and filters unrelated ones.
                hitArea.isTrigger = true;

                var poi = target.AddComponent<WorldPoi>();
                poi.Initialize(interaction.Title, interaction.Goal, null, Color.clear, interaction.ActionIntent);
            }
        }

        private static string ResolveWoodsZoneKind(WorldLayoutFrame layout)
        {
            if (layout == null || layout.Pois == null || layout.Zones == null ||
                !layout.Pois.TryGetValue("woods", out var point) || point == null)
                return null;

            string result = null;
            foreach (var zone in layout.Zones)
            {
                if (zone == null || (zone.Kind != "forest" && zone.Kind != "copse") ||
                    point.X < zone.X0 || point.X > zone.X1 ||
                    point.Y < zone.Y0 || point.Y > zone.Y1)
                    continue;

                if (result != null && result != zone.Kind) return null;
                result = zone.Kind;
            }
            return result;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
