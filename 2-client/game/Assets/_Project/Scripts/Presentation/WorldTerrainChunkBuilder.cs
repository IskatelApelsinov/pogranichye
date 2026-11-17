using System;
using System.Collections.Generic;
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

        public static void Apply(Transform root, TerrainChunkFrame chunk, WorldAnchorFrame anchor,
            ICollection<TerrainChunkFrame> availableChunks = null)
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
            mesh.normals = BuildSeamAwareNormals(chunk, availableChunks);
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
                // Prefer the project's authored terrain PBR map, then the imported Grok
                // meadow starter map. The latter is synchronized into Resources/Grok by the
                // editor importer; without this lookup the streamed frontier kept using only
                // the synthetic 64px fallback even after the texture pack was imported.
                var authoredGround = Resources.Load<Texture2D>("WorldMaterials/Textures/meadow_soil_albedo");
                var grokGround = Resources.Load<Texture2D>("WorldMaterials/Textures/Grok/grass_meadow");
                var groundSource = TerrainTextureSourcePolicy.Select(authoredGround != null, grokGround != null);
                switch (groundSource)
                {
                    case TerrainTextureSourcePolicy.Source.Authored:
                        _groundTexture = authoredGround;
                        break;
                    case TerrainTextureSourcePolicy.Source.Grok:
                        _groundTexture = grokGround;
                        break;
                    default:
                        _groundTexture = CreateSeamlessGroundTexture();
                        break;
                }
                _groundTexture.wrapMode = TextureWrapMode.Repeat;
                _material.mainTexture = _groundTexture;
                if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _groundTexture);
                if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", _groundTexture);
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
                else if (_material.HasProperty("_Color")) _material.color = Color.white;

                var authoredNormal = Resources.Load<Texture2D>("WorldMaterials/Textures/meadow_soil_normal");
                var grokNormal = Resources.Load<Texture2D>("WorldMaterials/Textures/Grok/grass_meadow_n");
                // A normal map must belong to the same texture set as the selected albedo.
                // Never combine an authored albedo with an unrelated Grok normal (or vice versa).
                var hasMatchingNormal = TerrainTextureSourcePolicy.HasMatchingNormal(
                    groundSource, authoredNormal != null, grokNormal != null);
                var normal = !hasMatchingNormal ? null :
                    groundSource == TerrainTextureSourcePolicy.Source.Authored ? authoredNormal : grokNormal;
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

        private static Vector3[] BuildSeamAwareNormals(TerrainChunkFrame chunk,
            ICollection<TerrainChunkFrame> availableChunks)
        {
            var side = chunk.Side;
            var west = FindNeighbor(chunk, availableChunks, -1, 0);
            var east = FindNeighbor(chunk, availableChunks, 1, 0);
            var south = FindNeighbor(chunk, availableChunks, 0, -1);
            var north = FindNeighbor(chunk, availableChunks, 0, 1);
            var normals = new Vector3[side * side];

            for (var z = 0; z < side; z++)
            for (var x = 0; x < side; x++)
            {
                var leftX = x - 1;
                var rightX = x + 1;
                var southZ = z - 1;
                var northZ = z + 1;
                var hasLeft = TerrainSeamHeightSampler.TryGetHeight(chunk.Heights,
                    west?.Heights, east?.Heights, south?.Heights, north?.Heights,
                    side, leftX, z, out var hLeft);
                var hasRight = TerrainSeamHeightSampler.TryGetHeight(chunk.Heights,
                    west?.Heights, east?.Heights, south?.Heights, north?.Heights,
                    side, rightX, z, out var hRight);
                var hasSouth = TerrainSeamHeightSampler.TryGetHeight(chunk.Heights,
                    west?.Heights, east?.Heights, south?.Heights, north?.Heights,
                    side, x, southZ, out var hSouth);
                var hasNorth = TerrainSeamHeightSampler.TryGetHeight(chunk.Heights,
                    west?.Heights, east?.Heights, south?.Heights, north?.Heights,
                    side, x, northZ, out var hNorth);

                // If a neighbor is not streamed yet, fall back to a one-sided edge
                // derivative. Once it arrives, this chunk and its cardinal neighbors rebuild.
                if (!hasLeft) hLeft = chunk.Heights[z * side + x];
                if (!hasRight) hRight = chunk.Heights[z * side + x];
                if (!hasSouth) hSouth = chunk.Heights[z * side + x];
                if (!hasNorth) hNorth = chunk.Heights[z * side + x];

                var dxDistance = Math.Max(0.001d, (hasLeft ? 1 : 0) + (hasRight ? 1 : 0)) * chunk.StepMeters;
                var dzDistance = Math.Max(0.001d, (hasSouth ? 1 : 0) + (hasNorth ? 1 : 0)) * chunk.StepMeters;
                var dx = (hRight - hLeft) / dxDistance;
                var dz = (hNorth - hSouth) / dzDistance;
                normals[z * side + x] = new Vector3((float)-dx, 1f, (float)-dz).normalized;
            }
            return normals;
        }

        private static TerrainChunkFrame FindNeighbor(TerrainChunkFrame chunk,
            ICollection<TerrainChunkFrame> availableChunks, int offsetX, int offsetZ)
        {
            if (chunk?.Chunk == null || chunk.Chunk.Length != 2 || availableChunks == null) return null;
            var targetX = chunk.Chunk[0] + offsetX;
            var targetZ = chunk.Chunk[1] + offsetZ;
            foreach (var candidate in availableChunks)
            {
                if (candidate == null || candidate.Frame != chunk.Frame || candidate.Chunk == null ||
                    candidate.Chunk.Length != 2 || candidate.StepMeters != chunk.StepMeters ||
                    candidate.Side != chunk.Side || candidate.Heights == null ||
                    candidate.Heights.Count != chunk.Heights.Count)
                    continue;
                if (candidate.Chunk[0] == targetX && candidate.Chunk[1] == targetZ) return candidate;
            }
            return null;
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
            AddResourceMarkers(root, chunk, anchor);
            if (chunk.Water != null)
                foreach (var water in chunk.Water)
                {
                    if (water == null || water.Points == null || water.Points.Count < 2 ||
                        !IsFinite(water.WidthMeters) || water.WidthMeters <= 0 || !IsFinite(water.WaterLevelMeters) ||
                        !AreFeaturePointsValid(water.Points)) continue;
                    // Prefer explicit per-point server surface elevations when available. Legacy
                    // descriptors retain the existing visual lift over their estimated water level.
                    // This remains presentation only: no bed depth or swimming authority is inferred.
                    var mesh = BuildRibbon(water.Points, (float)water.WidthMeters,
                        point => (float)TerrainWaterSurfacePolicy.ResolveLocalHeight(
                            point.SurfaceMeters, water.WaterLevelMeters, anchor.Up, 0.34d),
                        "Water " + water.Id);
                    AddFeatureMesh(root, "Water_" + SafeName(water.Id), mesh, WaterMaterial(),
                        (float)(chunk.Origin.East - anchor.East), (float)(chunk.Origin.North - anchor.North));
                }

            if (chunk.Roads != null)
                foreach (var road in chunk.Roads)
                {
                    if (road == null || road.Points == null || road.Points.Count < 2 ||
                        !IsFinite(road.WidthMeters) || road.WidthMeters < 0 || !AreFeaturePointsValid(road.Points)) continue;
                    var width = road.WidthMeters > 0 ? (float)road.WidthMeters : 3f;
                    var mesh = BuildRibbon(road.Points, width,
                        point => SamplePathHeight(chunk, anchor, point), "Road " + road.Id);
                    var pathMaterial = string.Equals(road.Kind, "trail", StringComparison.OrdinalIgnoreCase)
                        ? TrailMaterial() : RoadMaterial();
                    var pathPrefix = string.Equals(road.Kind, "trail", StringComparison.OrdinalIgnoreCase)
                        ? "Trail_" : "Road_";
                    AddFeatureMesh(root, pathPrefix + SafeName(road.Id), mesh, pathMaterial,
                        (float)(chunk.Origin.East - anchor.East), (float)(chunk.Origin.North - anchor.North));
                }
        }

        private static float SamplePathHeight(TerrainChunkFrame chunk, WorldAnchorFrame anchor,
            TerrainFeaturePointFrame point)
        {
            if (chunk == null || anchor == null || point == null || chunk.Side < 2 ||
                chunk.Heights == null || chunk.Heights.Count < chunk.Side * chunk.Side ||
                chunk.StepMeters <= 0d)
                return 0.06f;

            // Server polylines are chunk-local. Clamp tiny boundary/rounding overshoots to the
            // last valid heightfield sample instead of dropping the ribbon to local Y=0.
            var maxX = Math.Min(chunk.SizeMeters, (chunk.Side - 1) * chunk.StepMeters);
            var maxZ = maxX;
            var localX = Math.Max(0d, Math.Min(maxX, point.X));
            var localZ = Math.Max(0d, Math.Min(maxZ, point.Z));
            var east = chunk.Origin.East + localX;
            var north = chunk.Origin.North + localZ;
            if (TerrainHeightSampler.TrySample(chunk, east, north, out var sampled, out _, out _))
                return (float)(sampled - anchor.Up + 0.06d);

            // Defensive bilinear fallback for a valid heightfield whose edge normal cannot
            // be sampled (e.g. a clipped road endpoint at the outermost grid row/column).
            var gx = localX / chunk.StepMeters;
            var gz = localZ / chunk.StepMeters;
            var x0 = Math.Max(0, Math.Min(chunk.Side - 1, (int)Math.Floor(gx)));
            var z0 = Math.Max(0, Math.Min(chunk.Side - 1, (int)Math.Floor(gz)));
            var x1 = Math.Min(chunk.Side - 1, x0 + 1);
            var z1 = Math.Min(chunk.Side - 1, z0 + 1);
            var tx = gx - x0;
            var tz = gz - z0;
            var h00 = chunk.Heights[z0 * chunk.Side + x0];
            var h10 = chunk.Heights[z0 * chunk.Side + x1];
            var h01 = chunk.Heights[z1 * chunk.Side + x0];
            var h11 = chunk.Heights[z1 * chunk.Side + x1];
            var height = (h00 * (1d - tx) + h10 * tx) * (1d - tz) +
                (h01 * (1d - tx) + h11 * tx) * tz;
            return double.IsNaN(height) || double.IsInfinity(height)
                ? 0.06f : (float)(height - anchor.Up + 0.06d);
        }

        private static Material _resourceMaterial;

        private static void AddResourceMarkers(Transform root, TerrainChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (chunk.Resources == null || chunk.Resources.Count == 0) return;
            var material = ResourceMaterial();
            if (material == null) return;
            foreach (var resource in chunk.Resources)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.Kind) ||
                    !IsFinite(resource.X) || !IsFinite(resource.Z) ||
                    resource.X < 0 || resource.X > chunk.SizeMeters ||
                    resource.Z < 0 || resource.Z > chunk.SizeMeters) continue;

                double ground;
                float slope;
                Vector3 normal;
                var east = chunk.Origin.East + resource.X;
                var north = chunk.Origin.North + resource.Z;
                if (!TerrainHeightSampler.TrySample(chunk, east, north, out ground, out slope, out normal)) continue;

                // These are deterministic visual hints, not harvestable server entities.
                // Keep every part collider-free and derive placement only from the server descriptor.
                var marker = new GameObject("ResourceHint_" + SafeName(resource.Id));
                marker.transform.SetParent(root, false);
                marker.transform.localPosition = new Vector3(
                    (float)(chunk.Origin.East - anchor.East) + (float)resource.X,
                    (float)(ground - anchor.Up),
                    (float)(chunk.Origin.North - anchor.North) + (float)resource.Z);
                var count = Mathf.Clamp(resource.Count, 1, 3);
                switch (TerrainResourceVisualPolicy.Resolve(resource.Kind))
                {
                    case TerrainResourceVisualKind.LogPile:
                        BuildLogPile(marker.transform, count, material);
                        break;
                    case TerrainResourceVisualKind.BerryBush:
                        BuildBerryBush(marker.transform, count, material);
                        break;
                    case TerrainResourceVisualKind.StoneCluster:
                        BuildStoneCluster(marker.transform, count, material);
                        break;
                    default:
                        AddResourcePart(marker.transform, PrimitiveType.Sphere, "Hint",
                            new Vector3(0f, 0.16f, 0f), new Vector3(0.28f, 0.28f, 0.28f),
                            Quaternion.identity, material, new Color(0.36f, 0.55f, 0.31f));
                        break;
                }
            }
        }

        private static void BuildLogPile(Transform parent, int count, Material material)
        {
            for (var i = 0; i < count; i++)
            {
                var side = (i - (count - 1) * 0.5f) * 0.14f;
                var height = 0.08f + (i % 2) * 0.045f;
                var rotation = Quaternion.Euler(0f, (i % 2) * 7f, 90f);
                AddResourcePart(parent, PrimitiveType.Cylinder, "Log_" + i,
                    new Vector3(0f, height, side), new Vector3(0.16f, 0.38f, 0.16f),
                    rotation, material, new Color(0.31f, 0.20f, 0.11f));
                AddResourcePart(parent, PrimitiveType.Cylinder, "LogCut_" + i,
                    new Vector3(0.38f, height, side), new Vector3(0.16f, 0.012f, 0.16f),
                    rotation, material, new Color(0.58f, 0.45f, 0.29f));
            }
        }

        private static void BuildBerryBush(Transform parent, int count, Material material)
        {
            var clumps = Mathf.Clamp(count, 1, 3);
            for (var i = 0; i < clumps; i++)
            {
                var angle = i * Mathf.PI * 2f / clumps;
                var center = new Vector3(Mathf.Cos(angle) * 0.08f, 0.13f + (i % 2) * 0.035f,
                    Mathf.Sin(angle) * 0.08f);
                AddResourcePart(parent, PrimitiveType.Sphere, "Leaves_" + i, center,
                    new Vector3(0.25f, 0.22f, 0.25f), Quaternion.identity, material,
                    i % 2 == 0 ? new Color(0.22f, 0.35f, 0.17f) : new Color(0.29f, 0.42f, 0.20f));
            }

            AddResourcePart(parent, PrimitiveType.Sphere, "Berry_0", new Vector3(-0.09f, 0.25f, -0.13f),
                new Vector3(0.065f, 0.065f, 0.065f), Quaternion.identity, material, new Color(0.52f, 0.08f, 0.07f));
            AddResourcePart(parent, PrimitiveType.Sphere, "Berry_1", new Vector3(0.10f, 0.22f, -0.11f),
                new Vector3(0.06f, 0.06f, 0.06f), Quaternion.identity, material, new Color(0.62f, 0.13f, 0.09f));
            AddResourcePart(parent, PrimitiveType.Sphere, "Berry_2", new Vector3(0.01f, 0.31f, 0.04f),
                new Vector3(0.055f, 0.055f, 0.055f), Quaternion.identity, material, new Color(0.48f, 0.07f, 0.08f));
        }

        private static void BuildStoneCluster(Transform parent, int count, Material material)
        {
            for (var i = 0; i < count; i++)
            {
                var offset = (i - (count - 1) * 0.5f) * 0.14f;
                var position = new Vector3(offset, 0.07f + (i % 2) * 0.045f, (i % 2 == 0 ? -0.045f : 0.055f));
                var scale = new Vector3(0.22f + (i % 2) * 0.04f, 0.17f + (i % 2) * 0.035f, 0.20f);
                AddResourcePart(parent, PrimitiveType.Sphere, "Stone_" + i, position, scale,
                    Quaternion.Euler(0f, i * 17f, i * 6f), material,
                    i % 2 == 0 ? new Color(0.43f, 0.44f, 0.42f) : new Color(0.34f, 0.35f, 0.34f));
            }
        }

        private static void AddResourcePart(Transform parent, PrimitiveType primitive, string name,
            Vector3 position, Vector3 scale, Quaternion rotation, Material material, Color tint)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static Material ResourceMaterial()
        {
            if (_resourceMaterial != null) return _resourceMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _resourceMaterial = new Material(shader) { name = "World resource markers", color = Color.white };
            if (_resourceMaterial.HasProperty("_BaseColor")) _resourceMaterial.SetColor("_BaseColor", Color.white);
            return _resourceMaterial;
        }

        private static bool AreFeaturePointsValid(System.Collections.Generic.List<TerrainFeaturePointFrame> points)
        {
            if (points == null || points.Count < 2) return false;
            foreach (var point in points)
                if (point == null || !IsFinite(point.X) || !IsFinite(point.Z)) return false;
            return true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

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
        private static Material _trailMaterial;

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

        private static Material TrailMaterial()
        {
            if (_trailMaterial != null) return _trailMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _trailMaterial = new Material(shader)
            {
                name = "World path • narrow foot trail",
                color = new Color(0.43f, 0.35f, 0.24f)
            };
            if (_trailMaterial.HasProperty("_BaseColor"))
                _trailMaterial.SetColor("_BaseColor", new Color(0.43f, 0.35f, 0.24f));
            if (_trailMaterial.HasProperty("_Smoothness")) _trailMaterial.SetFloat("_Smoothness", 0.02f);
            return _trailMaterial;
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
