using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Lightweight presentation for global wilderness landmarks. Positions are always projected
    /// relative to the authoritative player WPOS, so the landmarks follow settlement changes
    /// and do not accumulate large-world float coordinates.
    /// </summary>
    public static class WorldWildSitePresentationBuilder
    {
        private sealed class SiteView
        {
            public string Id;
            public int Frame;
            public double East;
            public double North;
            public double Up;
            public Transform Root;
        }

        private static readonly Dictionary<string, SiteView> Views =
            new Dictionary<string, SiteView>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material> Materials =
            new Dictionary<string, Material>(StringComparer.Ordinal);
        private static Transform _root;

        public static void Rebuild(Transform parent, WorldManifestFrame manifest)
        {
            Clear();
            if (parent == null || manifest?.WildSites == null) return;
            _root = new GameObject("WorldWildSites").transform;
            _root.SetParent(parent, false);
            unchecked
            {
                var hash = 17;
                foreach (var site in manifest.WildSites)
                {
                    if (site == null || string.IsNullOrEmpty(site.Id) || Views.ContainsKey(site.Id))
                        continue;
                    var viewRoot = new GameObject("WildSite_" + site.Id).transform;
                    viewRoot.SetParent(_root, false);
                    BuildLandmark(viewRoot, site.Type, site.Mana, site.Danger);
                    Views.Add(site.Id, new SiteView
                    {
                        Id = site.Id, Frame = site.Frame, East = site.East,
                        North = site.North, Up = site.Up, Root = viewRoot
                    });
                    hash = hash * 31 + site.Id.GetHashCode();
                }
            }
        }

        public static void Update(Transform parent, WorldManifestFrame manifest, WorldFrame frame)
        {
            if (parent == null || manifest == null || frame?.Me?.WorldPosition == null ||
                frame.Me.Position == null || !frame.Me.WorldPosition.IsValid)
            {
                if (_root != null) _root.gameObject.SetActive(false);
                return;
            }

            if (_root == null || _root.parent != parent || Views.Count != (manifest.WildSites?.Count ?? 0))
                Rebuild(parent, manifest);
            if (_root == null) return;
            _root.gameObject.SetActive(true);

            var origin = frame.Me.WorldPosition;
            var scene = frame.Me.Position;
            _root.position = new Vector3((float)scene.X, 0f, (float)scene.Y);
            foreach (var view in Views.Values)
            {
                if (view.Root == null || view.Frame != origin.Frame)
                {
                    if (view.Root != null) view.Root.gameObject.SetActive(false);
                    continue;
                }

                var east = view.East - origin.East;
                var north = view.North - origin.North;
                var up = view.Up - origin.Up;
                var distanceSquared = east * east + north * north;
                var visible = distanceSquared <= 1100d * 1100d &&
                    Math.Abs(east) < 1000000d && Math.Abs(north) < 1000000d && Math.Abs(up) < 1000000d;
                view.Root.gameObject.SetActive(visible);
                if (visible)
                    view.Root.localPosition = new Vector3((float)east, (float)up, (float)north);
            }
        }

        private static void BuildLandmark(Transform root, string type, double mana, double danger)
        {
            var stone = MaterialFor("stone", new Color(0.39f, 0.37f, 0.32f));
            var dark = MaterialFor("dark", new Color(0.075f, 0.065f, 0.052f));
            var wood = MaterialFor("wood", new Color(0.28f, 0.19f, 0.11f));
            var green = MaterialFor("green", new Color(0.19f, 0.29f, 0.14f));
            var water = MaterialFor("water", new Color(0.12f, 0.36f, 0.43f, 0.82f));
            var glow = MaterialFor("glow", Color.Lerp(new Color(0.18f, 0.38f, 0.58f),
                new Color(0.56f, 0.40f, 0.76f), (float)Math.Max(0d, Math.Min(1d, (mana - 0.1d) / 2.9d))));

            switch (type)
            {
                case "cave":
                    Part(root, PrimitiveType.Cube, "CaveRockL", new Vector3(-2.1f, 1.2f, 0), new Vector3(1.4f, 2.4f, 2.2f), stone);
                    Part(root, PrimitiveType.Cube, "CaveRockR", new Vector3(2.1f, 1.2f, 0), new Vector3(1.4f, 2.4f, 2.2f), stone);
                    Part(root, PrimitiveType.Cube, "CaveLintel", new Vector3(0, 2.35f, 0), new Vector3(3.2f, 0.8f, 2.2f), stone);
                    Part(root, PrimitiveType.Cube, "CaveDarkness", new Vector3(0, 0.9f, 0.72f), new Vector3(2.1f, 1.8f, 0.12f), dark);
                    break;
                case "tower":
                    Part(root, PrimitiveType.Cylinder, "TowerBody", new Vector3(0, 4f, 0), new Vector3(3.2f, 4f, 3.2f), stone);
                    Part(root, PrimitiveType.Cylinder, "TowerCrown", new Vector3(0, 8.1f, 0), new Vector3(4.1f, 0.55f, 4.1f), wood);
                    Part(root, PrimitiveType.Cylinder, "TowerRoof", new Vector3(0, 9.35f, 0), new Vector3(3.7f, 1.1f, 3.7f), wood);
                    break;
                case "ruins":
                    Part(root, PrimitiveType.Cube, "RuinWallA", new Vector3(-2.4f, 1.3f, 0), new Vector3(1f, 2.6f, 5f), stone);
                    Part(root, PrimitiveType.Cube, "RuinWallB", new Vector3(1.4f, 1f, -1.8f), new Vector3(4.8f, 2f, 0.8f), stone);
                    Part(root, PrimitiveType.Cube, "RuinPillar", new Vector3(2.8f, 1.8f, 2f), new Vector3(0.9f, 3.6f, 0.9f), stone);
                    Part(root, PrimitiveType.Cube, "RuinLintel", new Vector3(-0.2f, 3.1f, -1.8f), new Vector3(4.5f, 0.7f, 0.9f), stone);
                    break;
                case "grove":
                    for (var i = 0; i < 5; i++)
                    {
                        var angle = i * Mathf.PI * 2f / 5f;
                        var x = Mathf.Cos(angle) * 3.2f;
                        var z = Mathf.Sin(angle) * 3.2f;
                        Part(root, PrimitiveType.Cylinder, "GroveTrunk" + i, new Vector3(x, 2f, z),
                            new Vector3(0.55f, 2f, 0.55f), wood);
                        Part(root, PrimitiveType.Sphere, "GroveCanopy" + i, new Vector3(x, 4.2f, z),
                            new Vector3(2.6f, 2.7f, 2.6f), green);
                    }
                    break;
                case "spring":
                    Part(root, PrimitiveType.Cylinder, "SpringRim", new Vector3(0, 0.22f, 0),
                        new Vector3(3.8f, 0.35f, 3.8f), stone);
                    Part(root, PrimitiveType.Cylinder, "SpringWater", new Vector3(0, 0.38f, 0),
                        new Vector3(3.1f, 0.08f, 3.1f), water);
                    Part(root, PrimitiveType.Sphere, "SpringGlow", new Vector3(0, 0.8f, 0),
                        new Vector3(0.45f, 0.65f, 0.45f), glow);
                    break;
                default:
                    Part(root, PrimitiveType.Cube, "UnknownWildSite", Vector3.up, Vector3.one * 2f, stone);
                    break;
            }

            if (danger > 0.65d)
                Part(root, PrimitiveType.Cube, "DangerMarker", new Vector3(0, 0.35f, -3.2f),
                    new Vector3(0.45f, 0.7f, 0.12f), MaterialFor("danger", new Color(0.55f, 0.12f, 0.08f)));
        }

        private static void Part(Transform parent, PrimitiveType shape, string name,
            Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static Material MaterialFor(string key, Color color)
        {
            if (Materials.TryGetValue(key, out var material) && material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { color = color };
            Materials[key] = material;
            return material;
        }

        public static void Clear()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            Views.Clear();
        }
    }
}
