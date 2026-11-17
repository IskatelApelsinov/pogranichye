using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Optional runtime access to the imported Slavic Medieval Village art pack.
    /// The editor installer creates Resources prefabs; if the pack is absent, callers
    /// retain their existing procedural fallback.
    /// </summary>
    public static class SlavicVillageAssetLibrary
    {
        private const string ResourceRoot = "PogranichyeArt/";

        public static GameObject Instantiate(string key, Transform parent, Vector3 localPosition,
            Quaternion localRotation, Vector3 localScale)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var prefab = Resources.Load<GameObject>(ResourceRoot + key);
            if (prefab == null) return null;

            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = key;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;
            return instance;
        }

        /// <summary>
        /// Fits a complete art-pack building into the server-owned footprint.
        /// Server collision and coordinates remain untouched; this is presentation only.
        /// </summary>
        public static bool TryBuildBuilding(Transform parent, string kind, float width, float depth)
        {
            string key;
            switch (kind)
            {
                case "tavern": key = "VillageHouse03"; break;
                case "smithy": key = "VillageHouse02"; break;
                case "granary": key = "VillageHouse01"; break;
                case "market": return false;
                default: key = "VillageHouse01"; break;
            }

            var prefab = Resources.Load<GameObject>(ResourceRoot + key);
            if (prefab == null) return false;

            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = "Slavic art • " + kind;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                Object.Destroy(instance);
                return false;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.x < 0.01f || bounds.size.z < 0.01f)
            {
                Object.Destroy(instance);
                return false;
            }

            // Source Comp prefabs are larger composition pieces, not exact plot-sized houses.
            // Leave a yard between plots and cap height independently to avoid oversized walls.
            var fit = Mathf.Min(width * 0.76f / bounds.size.x,
                depth * 0.76f / bounds.size.z, 5.6f / Mathf.Max(0.01f, bounds.size.y));
            if (float.IsNaN(fit) || float.IsInfinity(fit) || fit <= 0f)
            {
                Object.Destroy(instance);
                return false;
            }

            instance.transform.localScale = Vector3.one * fit;
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Centre the visual mass on the server footprint and compensate for offset pivots.
            var centerLocal = parent.InverseTransformPoint(bounds.center);
            var bottomLocal = parent.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
            instance.transform.localPosition += new Vector3(
                -centerLocal.x, 0.02f - bottomLocal, -centerLocal.z);

            // These imported colliders duplicate server-owned building obstruction.
            // Ignore Raycast keeps them from intercepting NPC/POI interaction probes;
            // the camera collision sphere still explicitly checks layer 2.
            var transforms = instance.GetComponentsInChildren<Transform>(true);
            foreach (var child in transforms) child.gameObject.layer = 2;
            return true;
        }
    }
}
