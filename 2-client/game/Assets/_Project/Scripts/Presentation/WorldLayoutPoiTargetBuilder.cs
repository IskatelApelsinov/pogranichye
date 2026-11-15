using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Adds only invisible, short-range E targets at server-provided generated-layout POIs.
    /// It creates no world/gameplay entity and sends no intent until the player presses E.
    /// </summary>
    public static class WorldLayoutPoiTargetBuilder
    {
        public static void Build(Transform root, WorldLayoutFrame layout)
        {
            if (root == null || layout == null || layout.Pois == null)
                return;

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
                hitArea.radius = 0.85f;
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
