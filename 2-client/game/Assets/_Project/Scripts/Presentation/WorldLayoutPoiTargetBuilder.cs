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
                    poi.Initialize(
                        string.IsNullOrWhiteSpace(descriptor.Title) ? "Взаимодействие" : descriptor.Title,
                        null, null, Color.clear, null, descriptor.Id, descriptor.Hint,
                        descriptor.Available, descriptor.Reason, descriptor.TargetType, descriptor.TargetId);
                }
                return;
            }

            // Compatibility path for older servers: coordinates still come from server Layout POIs.
            if (layout.Pois == null) return;
            var ids = WorldLayoutPoiInteractionPolicy.SupportedIds;
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (!WorldLayoutPoiInteractionPolicy.TryResolve(id, out var interaction) ||
                    !layout.Pois.TryGetValue(id, out var point) || point == null ||
                    !IsFinite(point.X) || !IsFinite(point.Y))
                    continue;

                var target = new GameObject("E interaction target • " + id);
                target.transform.SetParent(root, false);
                target.transform.localPosition = new Vector3((float)point.X, 0.9f, (float)point.Y);

                var hitArea = target.AddComponent<SphereCollider>();
                hitArea.radius = 1.25f;
                // Interaction volumes must never become invisible physical obstacles.
                // WorldInteractionProbe explicitly includes triggers and filters unrelated ones.
                hitArea.isTrigger = true;

                var poi = target.AddComponent<WorldPoi>();
                poi.Initialize(interaction.Title, interaction.Goal, null, Color.clear, interaction.ActionIntent);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
