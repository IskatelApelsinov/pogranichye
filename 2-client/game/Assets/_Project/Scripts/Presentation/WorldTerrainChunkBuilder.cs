using System;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Render and static collision mesh for one server-owned v1.19 world chunk.
    /// Vertices are rebased against the active settlement anchor, keeping Unity coordinates near zero.
    /// </summary>
    public static class WorldTerrainChunkBuilder
    {
        private const string RootName = "WorldTerrainChunk";
        private static Material _material;
        private static Texture2D _groundTexture;

        public static string ObjectName(string chunkId)
        {
            return RootName + "_" + (chunkId ?? string.Empty).Replace(':', '_').Replace('-', 'm');
        }

        public static void Clear(Transform root)
        {
            if (root == null) return;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null || !child.name.StartsWith(RootName + "_", StringComparison.Ordinal)) continue;
                RemoveObject(child);
            }
        }

        public static void Remove(Transform root, string chunkId)
        {
            if (root == null) return;
            var old = root.Find(ObjectName(chunkId));
            if (old != null) RemoveObject(old);
        }

        private static void RemoveObject(Transform old)
        {
            var filter = old.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            old.name += " (replaced)";
            UnityEngine.Object.Destroy(old.gameObject);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }

        private static Texture2D CreateSeamlessGroundTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = "World ground • seamless 64px grass and earth",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)size;
                var v = y / (float)size;
                // Integer-frequency harmonics repeat exactly at tile boundaries; no visible
                // texture seam appears when a terrain chunk starts a new mesh/UV island.
                var broad = Mathf.Sin((u * 3f + v * 2f) * Mathf.PI * 2f) * 0.5f +
                    Mathf.Sin((u * 2f - v * 5f) * Mathf.PI * 2f + 0.8f) * 0.28f;
                var fine = Mathf.Sin((u * 11f + v * 7f) * Mathf.PI * 2f + 1.7f) * 0.14f +
                    Mathf.Sin((u * 13f - v * 9f) * Mathf.PI * 2f + 2.4f) * 0.08f;
                var value = Mathf.Clamp01(0.54f + broad * 0.16f + fine * 0.10f);
                var soil = Mathf.Clamp01((0.28f - broad - fine) * 1.5f);
                var green = new Color(
                    Mathf.Lerp(0.22f, 0.39f, value),
                    Mathf.Lerp(0.29f, 0.43f, value),
                    Mathf.Lerp(0.15f, 0.24f, value));
                var earth = new Color(0.31f, 0.27f, 0.18f);
                var color = Color.Lerp(green, earth, soil * 0.22f);
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        public static void Apply(Transform root, TerrainChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (root == null || chunk == null || !chunk.IsValid || anchor == null) return;
            var objectName = ObjectName(chunk.Id);
            var old = root.Find(objectName);
            if (old != null)
            {
                RemoveObject(old);
            }

            var side = chunk.Side;
            var vertices = new Vector3[chunk.Heights.Count];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(side - 1) * (side - 1) * 6];
            var originX = chunk.Origin.East - anchor.East;
            var originZ = chunk.Origin.North - anchor.North;
            for (var z = 0; z < side; z++)
            for (var x = 0; x < side; x++)
            {
                var i = z * side + x;
                vertices[i] = new Vector3(
                    (float)(originX + x * chunk.StepMeters),
                    (float)(chunk.Heights[i] - anchor.Up),
                    (float)(originZ + z * chunk.StepMeters));
                // 16 m texture period divides the 256 m world chunk exactly, so UV phase
                // matches across every neighboring chunk edge, including negative indices.
                uvs[i] = new Vector2(x * chunk.StepMeters / 16f, z * chunk.StepMeters / 16f);
            }

            var t = 0;
            for (var z = 0; z < side - 1; z++)
            for (var x = 0; x < side - 1; x++)
            {
                var a = z * side + x;
                var b = a + 1;
                var c = a + side;
                var d = c + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }

            var mesh = new Mesh { name = "Terrain " + chunk.Id };
            if (vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(objectName);
            go.transform.SetParent(root, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            // One non-convex collider per bounded chunk. No per-vertex GameObjects or dynamic rigidbodies.
            // This gives future cross-settlement traversal real ground contact without changing server movement authority.
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    UnityEngine.Object.Destroy(go);
                    UnityEngine.Object.Destroy(mesh);
                    return;
                }
                _material = new Material(shader) { name = "World terrain • meadow PBR" };
                // Generated maps are optional: the client remains playable before the
                // Blender asset-generation step has been run on a developer machine.
                _groundTexture = Resources.Load<Texture2D>("WorldMaterials/Textures/meadow_soil_albedo");
                if (_groundTexture == null) _groundTexture = CreateSeamlessGroundTexture();
                _groundTexture.wrapMode = TextureWrapMode.Repeat;
                _material.mainTexture = _groundTexture;
                if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _groundTexture);
                if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", _groundTexture);
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
                else if (_material.HasProperty("_Color")) _material.color = Color.white;

                var normal = Resources.Load<Texture2D>("WorldMaterials/Textures/meadow_soil_normal");
                if (normal != null && _material.HasProperty("_BumpMap"))
                {
                    _material.SetTexture("_BumpMap", normal);
                    _material.EnableKeyword("_NORMALMAP");
                }
                var ao = Resources.Load<Texture2D>("WorldMaterials/Textures/meadow_soil_ao");
                if (ao != null && _material.HasProperty("_OcclusionMap"))
                {
                    _material.SetTexture("_OcclusionMap", ao);
                    _material.EnableKeyword("_OCCLUSIONMAP");
                }
                if (_material.HasProperty("_Metallic")) _material.SetFloat("_Metallic", 0f);
                if (_material.HasProperty("_Smoothness")) _material.SetFloat("_Smoothness", 0.16f);
            }
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            ApplyBiomeTint(renderer, chunk);
            ApplyTerrainFeatures(go.transform, chunk, anchor);
        }

        private static void ApplyBiomeTint(MeshRenderer renderer, TerrainChunkFrame chunk)
        {
            var tint = new Color(1f, 1f, 1f, 1f);
            switch ((chunk.Biome ?? string.Empty).ToLowerInvariant())
            {
                case "forest": tint = new Color(0.78f, 0.91f, 0.72f); break;
                case "mountain": tint = new Color(0.88f, 0.86f, 0.82f); break;
                case "wetland": case "swamp": tint = new Color(0.78f, 0.86f, 0.70f); break;
                case "steppe": tint = new Color(1.00f, 0.91f, 0.72f); break;
                case "taiga": tint = new Color(0.76f, 0.88f, 0.78f); break;
            }
            if (chunk.Cover != null)
            {
                var cover = chunk.Cover;
                var total = Math.Max(0.0001d, cover.Forest + cover.Grass + cover.Rock + cover.Wet);
                var rock = (float)(cover.Rock / total);
                var wet = (float)(cover.Wet / total);
                tint = Color.Lerp(tint, new Color(0.78f, 0.77f, 0.73f), Mathf.Clamp01(rock * 0.55f));
                tint = Color.Lerp(tint, new Color(0.76f, 0.82f, 0.69f), Mathf.Clamp01(wet * 0.35f));
            }
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            renderer.SetPropertyBlock(block);
        }

        private static void ApplyTerrainFeatures(Transform root, TerrainChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (chunk.Water != null)
                foreach (var water in chunk.Water)
                {
                    if (water == null || water.Points == null || water.Points.Count < 2 ||
                        !IsFinite(water.WidthMeters) || water.WidthMeters <= 0 || !IsFinite(water.WaterLevelMeters) ||
                        !AreFeaturePointsValid(water.Points)) continue;
                    // Server level is currently an estimate 0.3 m below the sampled ground. Since the
                    // heightfield has no carved river bed yet, lift only this visual ribbon back to
                    // the ground skin; it has no collider and does not alter canonical terrain.
                    var y = (float)(water.WaterLevelMeters - anchor.Up + 0.34d);
                    var mesh = BuildRibbon(water.Points, (float)water.WidthMeters, _ => y, "Water " + water.Id);
                    AddFeatureMesh(root, "Water_" + SafeName(water.Id), mesh, WaterMaterial(),
                        (float)(chunk.Origin.East - anchor.East), (float)(chunk.Origin.North - anchor.North));
                }

            if (chunk.Roads != null)
                foreach (var road in chunk.Roads)
                {
                    if (road == null || road.Points == null || road.Points.Count < 2 ||
                        !IsFinite(road.WidthMeters) || road.WidthMeters < 0 || !AreFeaturePointsValid(road.Points)) continue;
                    var width = road.WidthMeters > 0 ? (float)road.WidthMeters : 3f;
                    var mesh = BuildRibbon(road.Points, width, point =>
                    {
                        double height;
                        float slope;
                        Vector3 normal;
                        var east = chunk.Origin.East + point.X;
                        var north = chunk.Origin.North + point.Z;
                        return TerrainHeightSampler.TrySample(chunk, east, north, out height, out slope, out normal)
                            ? (float)(height - anchor.Up + 0.035d) : 0.035f;
                    }, "Road " + road.Id);
                    AddFeatureMesh(root, "Road_" + SafeName(road.Id), mesh, RoadMaterial(),
                        (float)(chunk.Origin.East - anchor.East), (float)(chunk.Origin.North - anchor.North));
                }
        }

        private static Mesh BuildRibbon(System.Collections.Generic.List<TerrainFeaturePointFrame> points,
            float width, Func<TerrainFeaturePointFrame, float> height, string name)
        {
            var vertices = new Vector3[points.Count * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(points.Count - 1) * 6];
            var half = Mathf.Max(0.25f, width) * 0.5f;
            var distance = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                var previous = points[Math.Max(0, i - 1)];
                var next = points[Math.Min(points.Count - 1, i + 1)];
                var dx = (float)(next.X - previous.X);
                var dz = (float)(next.Z - previous.Z);
                var length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length < 0.001f) { dx = 1f; dz = 0f; length = 1f; }
                var px = -dz / length * half;
                var pz = dx / length * half;
                var point = points[i];
                var y = height(point);
                vertices[i * 2] = new Vector3((float)point.X + px, y, (float)point.Z + pz);
                vertices[i * 2 + 1] = new Vector3((float)point.X - px, y, (float)point.Z - pz);
                if (i > 0)
                {
                    var prev = points[i - 1];
                    distance += Vector2.Distance(new Vector2((float)point.X, (float)point.Z),
                        new Vector2((float)prev.X, (float)prev.Z));
                }
                uvs[i * 2] = new Vector2(0f, distance / Mathf.Max(1f, width));
                uvs[i * 2 + 1] = new Vector2(1f, distance / Mathf.Max(1f, width));
            }
            var t = 0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = i * 2;
                var b = a + 1;
                var c = a + 2;
                var d = a + 3;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFeatureMesh(Transform parent, string name, Mesh mesh, Material material,
            float offsetEast, float offsetNorth)
        {
            if (mesh == null || material == null) { if (mesh != null) UnityEngine.Object.Destroy(mesh); return; }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(offsetEast, 0f, offsetNorth);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static string SafeName(string value)
        {
            return (value ?? string.Empty).Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        }

        private static Material _waterMaterial;
        private static Material _roadMaterial;

        private static Material WaterMaterial()
        {
            if (_waterMaterial != null) return _waterMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _waterMaterial = new Material(shader) { name = "World water • river", color = new Color(0.13f, 0.39f, 0.48f, 0.82f) };
            if (_waterMaterial.HasProperty("_BaseColor")) _waterMaterial.SetColor("_BaseColor", new Color(0.13f, 0.39f, 0.48f, 0.82f));
            if (_waterMaterial.HasProperty("_Surface")) _waterMaterial.SetFloat("_Surface", 1f);
            if (_waterMaterial.HasProperty("_Mode")) _waterMaterial.SetFloat("_Mode", 3f);
            if (_waterMaterial.HasProperty("_ZWrite")) _waterMaterial.SetFloat("_ZWrite", 0f);
            if (_waterMaterial.HasProperty("_SrcBlend")) _waterMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_waterMaterial.HasProperty("_DstBlend")) _waterMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _waterMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _waterMaterial.renderQueue = 3000;
            return _waterMaterial;
        }

        private static Material RoadMaterial()
        {
            if (_roadMaterial != null) return _roadMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _roadMaterial = new Material(shader) { name = "World path • packed earth", color = new Color(0.35f, 0.29f, 0.20f) };
            if (_roadMaterial.HasProperty("_BaseColor")) _roadMaterial.SetColor("_BaseColor", new Color(0.35f, 0.29f, 0.20f));
            if (_roadMaterial.HasProperty("_Smoothness")) _roadMaterial.SetFloat("_Smoothness", 0.05f);
            return _roadMaterial;
        }
    }
}
