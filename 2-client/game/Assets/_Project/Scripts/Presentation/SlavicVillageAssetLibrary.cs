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

            var fit = Mathf.Min(width / bounds.size.x, depth / bounds.size.z);
            if (float.IsNaN(fit) || float.IsInfinity(fit) || fit <= 0f)
            {
                Object.Destroy(instance);
                return false;
            }

            instance.transform.localScale = Vector3.one * fit;
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            instance.transform.localPosition = Vector3.up * (0.02f - bounds.min.y);
            return true;
        }
    }
}
