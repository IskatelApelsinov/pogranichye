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
            public double Ground;
            public string Type;
            public double Mana;
            public double Danger;
            public Transform Root;
        }

        private static readonly Dictionary<string, SiteView> Views =
            new Dictionary<string, SiteView>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material> Materials =
            new Dictionary<string, Material>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SiteView> NearbyViews =
            new Dictionary<string, SiteView>(StringComparer.Ordinal);
        private static Material _activationMaterial;
        private const float SiteActivationRadius = 25f;
        private const float NearbyVisibleRadius = 420f;
        private static Transform _root;
        private static WorldManifestFrame _manifest;

        public static void Rebuild(Transform parent, WorldManifestFrame manifest)
        {
            Clear();
            if (parent == null) return;
            _manifest = manifest;
            _root = new GameObject("WorldWildSites").transform;
            _root.SetParent(parent, false);
            if (manifest.WildSites == null) return;
            foreach (var site in manifest.WildSites)
            {
                if (site == null || string.IsNullOrEmpty(site.Id) || Views.ContainsKey(site.Id))
                    continue;
                Views.Add(site.Id, new SiteView
                {
                    Id = site.Id, Frame = site.Frame, East = site.East,
                    North = site.North, Up = site.Up, Ground = site.Ground, Type = site.Type,
                    Mana = site.Mana, Danger = site.Danger
                });
            }
        }

        public static void Update(Transform parent, WorldManifestFrame manifest, WorldFrame frame)
        {
            if (parent == null || frame?.Me?.WorldPosition == null ||
                frame.Me.Position == null || !frame.Me.WorldPosition.IsValid)
            {
                if (_root != null) _root.gameObject.SetActive(false);
                return;
            }

            if (_root == null || _root.parent != parent || !object.ReferenceEquals(_manifest, manifest))
                Rebuild(parent, manifest);
            if (_root == null) return;
            _root.gameObject.SetActive(true);

            var origin = frame.Me.WorldPosition;
            var scene = frame.Me.Position;
            _root.position = new Vector3((float)scene.X, 0f, (float)scene.Y);
            foreach (var view in Views.Values)
            {
                if (view.Frame != origin.Frame)
                {
                    if (view.Root != null) view.Root.gameObject.SetActive(false);
                    continue;
                }

                var east = view.East - origin.East;
                var north = view.North - origin.North;
                var up = view.Ground - origin.Up;
                var distanceSquared = east * east + north * north;
                var visible = distanceSquared <= 360d * 360d &&
                    Math.Abs(east) < 1000000d && Math.Abs(north) < 1000000d && Math.Abs(up) < 1000000d;
                if (!visible)
                {
                    if (view.Root != null) view.Root.gameObject.SetActive(false);
                    continue;
                }

                if (view.Root == null)
                {
                    view.Root = new GameObject("WildSite_" + view.Id).transform;
                    view.Root.SetParent(_root, false);
                    BuildLandmark(view.Root, view.Type, view.Mana, view.Danger,
                        Math.Abs(view.Up - view.Ground));
                }
                else
                {
                    view.Root.gameObject.SetActive(true);
                }
                view.Root.localPosition = new Vector3((float)east, (float)up, (float)north);
            }
            UpdateNearbySites(parent, frame, origin, scene);
        }

        private static void UpdateNearbySites(Transform parent, WorldFrame frame,
            WorldPositionFrame origin, ScenePositionFrame scene)
        {
            var sites = frame?.Place?.Sites;
            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            if (sites != null)
            {
                foreach (var site in sites)
                {
                    if (site == null || string.IsNullOrWhiteSpace(site.Id)) continue;
                    var east = site.East - origin.East;
                    var north = site.North - origin.North;
                    var distanceSquared = east * east + north * north;
                    if (distanceSquared > NearbyVisibleRadius * NearbyVisibleRadius) continue;
                    activeIds.Add(site.Id);
                    if (!NearbyViews.TryGetValue(site.Id, out var view))
                    {
                        view = new SiteView { Id = site.Id, Type = site.Kind };
                        view.Root = new GameObject("NearbyWildPlace_" + site.Id).transform;
                        view.Root.SetParent(_root, false);
                        BuildNearbyPlace(view.Root, site.Kind);
                        NearbyViews.Add(site.Id, view);
                    }
                    view.Root.gameObject.SetActive(true);
                    var up = site.Up - origin.Up;
                    view.Root.localPosition = new Vector3((float)east, (float)up, (float)north);
                }
            }

            foreach (var pair in NearbyViews)
                if (!activeIds.Contains(pair.Key) && pair.Value.Root != null)
                    pair.Value.Root.gameObject.SetActive(false);
        }

        private static void BuildNearbyPlace(Transform root, string kind)
        {
            var wood = MaterialFor("site_wood", new Color(0.29f, 0.19f, 0.11f));
            var stone = MaterialFor("site_stone", new Color(0.39f, 0.37f, 0.33f));
            var dark = MaterialFor("site_dark", new Color(0.075f, 0.06f, 0.045f));
            var green = MaterialFor("site_green", new Color(0.17f, 0.30f, 0.12f));
            var leaf = MaterialFor("site_leaf", new Color(0.27f, 0.38f, 0.13f));
            var water = MaterialFor("site_water", new Color(0.10f, 0.34f, 0.43f, 0.82f));
            switch (kind)
            {
                case "camp":
                    Part(root, PrimitiveType.Cylinder, "ColdFirepit", new Vector3(0, 0.12f, 0), new Vector3(1.25f, 0.12f, 1.25f), stone);
                    for (var i = 0; i < 7; i++)
                    {
                        var angle = i * Mathf.PI * 2f / 7f;
                        Part(root, PrimitiveType.Sphere, "FirepitStone" + i,
                            new Vector3(Mathf.Cos(angle) * 0.72f, 0.18f, Mathf.Sin(angle) * 0.72f),
                            new Vector3(0.32f, 0.22f, 0.32f), stone);
                    }
                    Part(root, PrimitiveType.Cube, "CampLogA", new Vector3(-1.5f, 0.25f, 1.5f), new Vector3(2.1f, 0.32f, 0.38f), wood);
                    Part(root, PrimitiveType.Cube, "CampLogB", new Vector3(1.5f, 0.25f, 1.5f), new Vector3(2.1f, 0.32f, 0.38f), wood);
                    break;
                case "hut":
                    Part(root, PrimitiveType.Cube, "HutBody", new Vector3(0, 1.15f, 0), new Vector3(4.2f, 2.3f, 3.6f), wood);
                    Part(root, PrimitiveType.Cube, "HutRoof", new Vector3(0, 2.65f, 0), new Vector3(4.9f, 0.45f, 4.3f), dark);
                    Part(root, PrimitiveType.Cube, "HutDoor", new Vector3(0, 0.85f, -1.84f), new Vector3(0.9f, 1.7f, 0.12f), dark);
                    break;
                case "berry":
                    for (var i = 0; i < 7; i++)
                    {
                        var x = Mathf.Sin(i * 2.4f) * 2.3f;
                        var z = Mathf.Cos(i * 2.4f) * 2.0f;
                        Part(root, PrimitiveType.Sphere, "BerryBush" + i, new Vector3(x, 0.48f, z),
                            new Vector3(1.15f, 0.8f, 1.1f), green);
                        Part(root, PrimitiveType.Sphere, "BerryFruit" + i, new Vector3(x + 0.2f, 0.72f, z - 0.2f),
                            Vector3.one * 0.16f, MaterialFor("site_berry", new Color(0.35f, 0.08f, 0.12f)));
                    }
                    break;
                case "herbs":
                    for (var i = 0; i < 11; i++)
                    {
                        var x = Mathf.Sin(i * 1.7f) * 2.5f;
                        var z = Mathf.Cos(i * 1.7f) * 2.2f;
                        Part(root, PrimitiveType.Cone, "Herb" + i, new Vector3(x, 0.35f, z),
                            new Vector3(0.22f, 0.7f, 0.22f), leaf);
                    }
                    break;
                case "thicket":
                case "copse":
                    for (var i = 0; i < 5; i++)
                    {
                        var x = Mathf.Sin(i * 1.25f) * 3.1f;
                        var z = Mathf.Cos(i * 1.25f) * 2.8f;
                        Part(root, PrimitiveType.Cylinder, "ThicketTrunk" + i, new Vector3(x, 1.5f, z),
                            new Vector3(0.45f, 1.5f, 0.45f), wood);
                        Part(root, PrimitiveType.Sphere, "ThicketCanopy" + i, new Vector3(x, 3.5f, z),
                            new Vector3(2.4f, 2.6f, 2.4f), green);
                    }
                    break;
                case "outcrop":
                    for (var i = 0; i < 5; i++)
                        Part(root, PrimitiveType.Sphere, "OreStone" + i, new Vector3(i * 0.7f - 1.4f, 0.55f, Mathf.Sin(i) * 0.6f),
                            new Vector3(1.1f, 0.9f, 1.0f), stone);
                    break;
                case "pond":
                    Part(root, PrimitiveType.Cylinder, "PondWater", new Vector3(0, 0.06f, 0),
                        new Vector3(5.2f, 0.06f, 3.8f), water);
                    break;
                case "barrow":
                    Part(root, PrimitiveType.Sphere, "BarrowMound", new Vector3(0, 0.65f, 0),
                        new Vector3(6f, 1.35f, 5f), green);
                    Part(root, PrimitiveType.Cube, "BarrowStone", new Vector3(0, 0.55f, -1.9f),
                        new Vector3(1.3f, 1.1f, 0.25f), stone);
                    break;
                case "ruins":
                    Part(root, PrimitiveType.Cube, "RuinWallA", new Vector3(-1.8f, 1.1f, 0), new Vector3(0.65f, 2.2f, 4.5f), stone);
                    Part(root, PrimitiveType.Cube, "RuinWallB", new Vector3(1.4f, 0.8f, -1.8f), new Vector3(3.8f, 1.6f, 0.65f), stone);
                    break;
                case "den":
                    Part(root, PrimitiveType.Sphere, "DenMound", new Vector3(0, 0.55f, 0), new Vector3(4.6f, 1.1f, 4f), green);
                    Part(root, PrimitiveType.Cube, "DenMouth", new Vector3(0, 0.45f, -1.72f), new Vector3(1.25f, 0.9f, 0.16f), dark);
                    break;
                default:
                    Part(root, PrimitiveType.Cylinder, "WildPlaceMarker", new Vector3(0, 0.35f, 0),
                        new Vector3(1.3f, 0.35f, 1.3f), stone);
                    break;
            }

            // Server's current wild-place action radius is 25 m. This unobtrusive ring
            // makes the exact activation boundary visible without changing its authority.
            var ringObject = new GameObject("ActivationRadius_25m");
            ringObject.transform.SetParent(root, false);
            var line = ringObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 65;
            line.widthMultiplier = 0.16f;
            line.sharedMaterial = ActivationMaterial();
            for (var i = 0; i < 65; i++)
            {
                var angle = i * Mathf.PI * 2f / 64f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * SiteActivationRadius, 0.12f,
                    Mathf.Sin(angle) * SiteActivationRadius));
            }
        }

        private static Material ActivationMaterial()
        {
            if (_activationMaterial != null) return _activationMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ??
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _activationMaterial = new Material(shader) { color = new Color(0.92f, 0.70f, 0.28f, 0.82f) };
            return _activationMaterial;
        }

        private static void BuildLandmark(Transform root, string type, double mana, double danger, double verticalOffset)
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
                    Part(root, PrimitiveType.Cube, "CaveDarkness", new Vector3(0, -0.8f, 0.72f), new Vector3(2.1f, 1.6f, 0.12f), dark);
                    break;
                case "tower":
                    var towerHeight = Mathf.Clamp((float)verticalOffset, 12f, 35f);
                    Part(root, PrimitiveType.Cylinder, "TowerBody", new Vector3(0, towerHeight * 0.5f, 0), new Vector3(3.2f, towerHeight * 0.5f, 3.2f), stone);
                    Part(root, PrimitiveType.Cylinder, "TowerCrown", new Vector3(0, towerHeight, 0), new Vector3(4.1f, 0.55f, 4.1f), wood);
                    Part(root, PrimitiveType.Cylinder, "TowerRoof", new Vector3(0, towerHeight + 1.1f, 0), new Vector3(3.7f, 1.1f, 3.7f), wood);
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
            if (shader == null) return null;
            material = new Material(shader) { color = color };
            Materials[key] = material;
            return material;
        }

        public static void Clear()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            Views.Clear();
            foreach (var view in NearbyViews.Values)
                if (view.Root != null) UnityEngine.Object.Destroy(view.Root.gameObject);
            NearbyViews.Clear();
            if (_activationMaterial != null) UnityEngine.Object.Destroy(_activationMaterial);
            _activationMaterial = null;
            _manifest = null;
        }
    }
}
