using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Builds one collider-free low-poly model per server gather node. Models use shared
    /// cached meshes; node IDs, positions and availability always come from server frames.
    /// </summary>
    public static class WorldGatherNodePresentationBuilder
    {
        private const string MarkerPrefix = "GatherNode_";
        private static readonly Dictionary<string, Mesh> Templates =
            new Dictionary<string, Mesh>(StringComparer.Ordinal);
        private static Material _material;

        public static void Apply(Transform terrainRoot, TerrainChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (terrainRoot == null || chunk == null || !chunk.IsValid || anchor == null ||
                chunk.Gather == null || chunk.Gather.Count == 0)
                return;

            var material = GetMaterial();
            if (material == null) return;

            foreach (var node in chunk.Gather)
            {
                if (node == null || !TerrainGatherNodeVisualPolicy.IsRenderable(
                    node.Id, node.Kind, node.X, node.Z, node.Remaining, chunk.SizeMeters))
                    continue;

                double ground;
                float slope;
                Vector3 normal;
                var east = chunk.Origin.East + node.X;
                var north = chunk.Origin.North + node.Z;
                if (!TerrainHeightSampler.TrySample(chunk, east, north, out ground, out slope, out normal))
                    continue;

                var model = GetTemplate(node.Kind);
                if (model == null) continue;

                var marker = new GameObject(ObjectName(node.Id));
                marker.transform.SetParent(terrainRoot, false);
                marker.transform.localPosition = new Vector3(
                    (float)(chunk.Origin.East - anchor.East + node.X),
                    (float)(ground - anchor.Up),
                    (float)(chunk.Origin.North - anchor.North + node.Z));
                marker.transform.localRotation = Quaternion.Euler(0f, StableYaw(node.Id), 0f);

                var filter = marker.AddComponent<MeshFilter>();
                filter.sharedMesh = model;
                var renderer = marker.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var tint = ColorFor(node.Kind);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                // Keep depleted nodes in the hierarchy so a nearby authoritative snapshot can
                // reveal them again after server-side regrowth without rebuilding the terrain chunk.
                marker.SetActive(TerrainGatherNodeVisualPolicy.ShouldBeVisible(node.Remaining));
            }
        }

        public static void Refresh(Transform terrainRoot, string chunkId, IList<GatherNodeFrame> nearbyNodes)
        {
            if (terrainRoot == null || string.IsNullOrWhiteSpace(chunkId) ||
                nearbyNodes == null || nearbyNodes.Count == 0)
                return;

            var chunkRoot = terrainRoot.Find(WorldTerrainChunkBuilder.ObjectName(chunkId));
            if (chunkRoot == null) return;

            foreach (var node in nearbyNodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id)) continue;
                var marker = chunkRoot.Find(ObjectName(node.Id));
                if (marker != null)
                    marker.gameObject.SetActive(TerrainGatherNodeVisualPolicy.ShouldBeVisible(node.Remaining));
            }
        }

        private static string ObjectName(string id)
        {
            return MarkerPrefix + (id ?? string.Empty).Replace(':', '_')
                .Replace('/', '_').Replace('\\', '_');
        }

        private static Mesh GetTemplate(string kind)
        {
            if (Templates.TryGetValue(kind, out var cached) && cached != null)
                return cached;
            var created = BuildTemplate(kind);
            if (created != null) Templates[kind] = created;
            return created;
        }

        private static Mesh BuildTemplate(string kind)
        {
            var root = new GameObject("GatherTemplate_" + kind);
            root.hideFlags = HideFlags.HideAndDontSave;
            var filters = new List<MeshFilter>();

            switch (kind)
            {
                case "herb":
                    AddPart(root.transform, filters, PrimitiveType.Cylinder, "Stem",
                        new Vector3(0f, 0.13f, 0f), new Vector3(0.018f, 0.13f, 0.018f), Quaternion.identity);
                    for (var i = 0; i < 5; i++)
                    {
                        var angle = i * 72f;
                        var radians = angle * Mathf.Deg2Rad;
                        AddPart(root.transform, filters, PrimitiveType.Capsule, "Leaf_" + i,
                            new Vector3(Mathf.Cos(radians) * 0.075f, 0.17f + (i % 2) * 0.035f,
                                Mathf.Sin(radians) * 0.075f),
                            new Vector3(0.032f, 0.105f, 0.025f),
                            Quaternion.Euler(0f, angle, (i % 2 == 0 ? -35f : 35f)));
                    }
                    break;

                case "mushroom":
                    AddPart(root.transform, filters, PrimitiveType.Cylinder, "Stem",
                        new Vector3(0f, 0.095f, 0f), new Vector3(0.055f, 0.10f, 0.055f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Cap",
                        new Vector3(0f, 0.235f, 0f), new Vector3(0.18f, 0.095f, 0.18f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Spot_0",
                        new Vector3(-0.075f, 0.295f, -0.045f), new Vector3(0.025f, 0.012f, 0.025f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Spot_1",
                        new Vector3(0.065f, 0.30f, 0.055f), new Vector3(0.022f, 0.012f, 0.022f), Quaternion.identity);
                    break;

                case "berries":
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Bush_Centre",
                        new Vector3(0f, 0.16f, 0f), new Vector3(0.25f, 0.19f, 0.23f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Bush_Left",
                        new Vector3(-0.13f, 0.12f, 0.035f), new Vector3(0.16f, 0.14f, 0.16f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Bush_Right",
                        new Vector3(0.13f, 0.13f, -0.035f), new Vector3(0.16f, 0.15f, 0.16f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Berry_0",
                        new Vector3(-0.10f, 0.28f, -0.12f), new Vector3(0.035f, 0.035f, 0.035f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Berry_1",
                        new Vector3(0.09f, 0.25f, -0.13f), new Vector3(0.032f, 0.032f, 0.032f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Berry_2",
                        new Vector3(0.015f, 0.30f, 0.08f), new Vector3(0.03f, 0.03f, 0.03f), Quaternion.identity);
                    break;

                case "hive":
                    AddPart(root.transform, filters, PrimitiveType.Cylinder, "Branch",
                        new Vector3(0f, 0.68f, 0f), new Vector3(0.035f, 0.31f, 0.035f),
                        Quaternion.Euler(0f, 0f, 90f));
                    AddPart(root.transform, filters, PrimitiveType.Capsule, "HiveBody",
                        new Vector3(0f, 0.34f, 0f), new Vector3(0.15f, 0.25f, 0.15f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "HiveTop",
                        new Vector3(0f, 0.57f, 0f), new Vector3(0.12f, 0.07f, 0.12f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Entrance",
                        new Vector3(0f, 0.32f, -0.145f), new Vector3(0.025f, 0.025f, 0.012f), Quaternion.identity);
                    break;

                case "deadwood":
                    AddPart(root.transform, filters, PrimitiveType.Cylinder, "Log_0",
                        new Vector3(0f, 0.075f, 0f), new Vector3(0.075f, 0.31f, 0.075f),
                        Quaternion.Euler(0f, 0f, 90f));
                    AddPart(root.transform, filters, PrimitiveType.Cylinder, "Log_1",
                        new Vector3(-0.055f, 0.11f, 0.105f), new Vector3(0.06f, 0.22f, 0.06f),
                        Quaternion.Euler(0f, 0f, 76f));
                    break;

                case "nest":
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * 45f;
                        var radians = angle * Mathf.Deg2Rad;
                        AddPart(root.transform, filters, PrimitiveType.Capsule, "Twig_" + i,
                            new Vector3(Mathf.Cos(radians) * 0.12f, 0.075f, Mathf.Sin(radians) * 0.12f),
                            new Vector3(0.018f, 0.13f, 0.018f),
                            Quaternion.Euler(90f, 0f, angle));
                    }
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Egg_0",
                        new Vector3(-0.035f, 0.11f, 0f), new Vector3(0.045f, 0.06f, 0.04f), Quaternion.identity);
                    AddPart(root.transform, filters, PrimitiveType.Sphere, "Egg_1",
                        new Vector3(0.04f, 0.11f, 0.025f), new Vector3(0.04f, 0.055f, 0.04f), Quaternion.identity);
                    break;

                case "flax":
                    for (var i = 0; i < 4; i++)
                    {
                        var x = (i - 1.5f) * 0.065f;
                        AddPart(root.transform, filters, PrimitiveType.Cylinder, "Stem_" + i,
                            new Vector3(x, 0.19f, (i % 2) * 0.035f),
                            new Vector3(0.014f, 0.19f, 0.014f), Quaternion.identity);
                        AddPart(root.transform, filters, PrimitiveType.Capsule, "Leaf_" + i,
                            new Vector3(x + 0.035f, 0.19f, (i % 2) * 0.035f),
                            new Vector3(0.018f, 0.10f, 0.018f), Quaternion.Euler(0f, i * 35f, -38f));
                        AddPart(root.transform, filters, PrimitiveType.Sphere, "Flower_" + i,
                            new Vector3(x, 0.39f, (i % 2) * 0.035f),
                            new Vector3(0.04f, 0.035f, 0.04f), Quaternion.identity);
                    }
                    break;
            }

            if (filters.Count == 0)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            var combine = new CombineInstance[filters.Count];
            for (var i = 0; i < filters.Count; i++)
            {
                combine[i] = new CombineInstance
                {
                    mesh = filters[i].sharedMesh,
                    transform = filters[i].transform.localToWorldMatrix
                };
            }

            var mesh = new Mesh { name = "Gather node • " + kind, hideFlags = HideFlags.DontSave };
            mesh.CombineMeshes(combine, true, true, false);
            mesh.RecalculateBounds();
            UnityEngine.Object.Destroy(root);
            return mesh;
        }

        private static void AddPart(Transform root, List<MeshFilter> filters, PrimitiveType primitive,
            string name, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.hideFlags = HideFlags.HideAndDontSave;
            part.transform.SetParent(root, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localRotation = rotation;
            var collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
            var filter = part.GetComponent<MeshFilter>();
            if (filter != null) filters.Add(filter);
        }

        private static Material GetMaterial()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _material = new Material(shader) { name = "Server gatherable flora • low-poly" };
            if (_material.HasProperty("_Metallic")) _material.SetFloat("_Metallic", 0f);
            if (_material.HasProperty("_Smoothness")) _material.SetFloat("_Smoothness", 0.18f);
            return _material;
        }

        private static Color ColorFor(string kind)
        {
            switch (kind)
            {
                case "herb": return new Color(0.30f, 0.46f, 0.20f);
                case "mushroom": return new Color(0.62f, 0.47f, 0.34f);
                case "berries": return new Color(0.24f, 0.39f, 0.17f);
                case "hive": return new Color(0.62f, 0.45f, 0.22f);
                case "deadwood": return new Color(0.31f, 0.21f, 0.13f);
                case "nest": return new Color(0.56f, 0.43f, 0.28f);
                case "flax": return new Color(0.35f, 0.49f, 0.31f);
                default: return Color.white;
            }
        }

        private static float StableYaw(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in id ?? string.Empty)
                    hash = (hash ^ ch) * 16777619;
                return hash % 360u;
            }
        }
    }
}
