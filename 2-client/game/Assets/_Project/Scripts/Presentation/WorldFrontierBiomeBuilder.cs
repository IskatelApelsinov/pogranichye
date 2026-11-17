using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Deterministic, presentation-only frontier vegetation. One combined mesh per material/LOD
    /// is built per server terrain chunk; no per-plant GameObjects or gameplay colliders.
    /// Placement is anchored to global integer cells so neighboring chunks and reloads agree.
    /// </summary>
    public static class WorldFrontierBiomeBuilder
    {
        private const string RootPrefix = "FrontierBiome_";
        private const int MaxTrees = 900;
        private const int MaxUnderstory = 1400;
        private const int MaxRocks = 260;
        private static readonly Dictionary<string, Palette> Palettes = new Dictionary<string, Palette>();

        private sealed class Palette
        {
            public Material Leaves;
            public Material Bark;
            public Material Rock;
        }

        private sealed class MeshData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(4096);
            public readonly List<int> Triangles = new List<int>(8192);
            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
                Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c); Triangle(a, c, d);
            }
            public Mesh ToMesh(string name)
            {
                if (Vertices.Count == 0) return null;
                var mesh = new Mesh { name = name };
                if (Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Vertices);
                mesh.SetTriangles(Triangles, 0, true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private sealed class LodMeshes
        {
            public readonly MeshData Leaves = new MeshData();
            public readonly MeshData Bark = new MeshData();
            public readonly MeshData Rock = new MeshData();
        }

        public static string ObjectName(string chunkId) =>
            RootPrefix + (chunkId ?? string.Empty).Replace(':', '_').Replace('-', 'm');

        public static void Clear(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name.StartsWith(RootPrefix, StringComparison.Ordinal))
                    RemoveObject(child);
            }
        }

        public static void Remove(Transform root, string chunkId)
        {
            if (root == null) return;
            Transform old = root.Find(ObjectName(chunkId));
            if (old != null) RemoveObject(old);
        }

        private static void RemoveObject(Transform root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                filter.sharedMesh = null;
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            }
            UnityEngine.Object.Destroy(root.gameObject);
        }

        public static void Apply(Transform parent, TerrainChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (parent == null || chunk == null || !chunk.IsValid || anchor == null) return;
            Remove(parent, chunk.Id);

            // Frames without server-provided biome descriptors remain unchanged. This builder only
            // activates when the live server has supplied biome/seed/cover data.
            if (!chunk.Seed.HasValue && string.IsNullOrEmpty(chunk.Biome) && chunk.Cover == null) return;

            string biome = NormalizeBiome(chunk.Biome);
            var cover = chunk.Cover;
            float forest = cover != null ? Clamp01(cover.Forest) : DefaultForest(biome);
            float grass = cover != null ? Clamp01(cover.Grass) : DefaultGrass(biome);
            float rock = cover != null ? Clamp01(cover.Rock) : DefaultRock(biome);
            float wet = cover != null ? Clamp01(cover.Wet) : DefaultWet(biome);
            long seed = chunk.Seed ?? StableHash(chunk.Chunk[0], chunk.Chunk[1], 17);

            var lod0 = new LodMeshes();
            var lod1 = new LodMeshes();
            var lod2 = new LodMeshes();

            // Six-metre global cells make server forest biomes read as woodland, not isolated saplings.
            // Global coordinates keep placement deterministic across chunk edges and negative coordinates.
            long minCellX = FloorDiv((long)Math.Floor(chunk.Origin.East), 6);
            long minCellZ = FloorDiv((long)Math.Floor(chunk.Origin.North), 6);
            int sideCells = Mathf.CeilToInt(chunk.SizeMeters / 6f) + 1;
            int treeCount = 0;
            int understoryCount = 0;
            int rockCount = 0;

            for (int gz = 0; gz < sideCells; gz++)
            for (int gx = 0; gx < sideCells; gx++)
            {
                long cellX = minCellX + gx;
                long cellZ = minCellZ + gz;
                float jx = Hash01(seed, cellX, cellZ, 1) * 4.0f + 1.0f;
                float jz = Hash01(seed, cellX, cellZ, 2) * 4.0f + 1.0f;
                double east = cellX * 6.0 + jx;
                double north = cellZ * 6.0 + jz;
                double localX = east - chunk.Origin.East;
                double localZ = north - chunk.Origin.North;
                if (localX < 1 || localZ < 1 || localX >= chunk.SizeMeters - 1 || localZ >= chunk.SizeMeters - 1)
                    continue;
                // Frontier vegetation must not fill a settlement footprint. Settlement coordinates
                // in TerrainChunkFrame are canonical global E/N metres.
                if (IsSettlementClear(chunk, east, north, 112.0)) continue;
                float ground;
                if (!TrySampleHeight(chunk, localX, localZ, out ground)) continue;
                if (IsFeatureClear(chunk, localX, localZ, 1.8f) || IsServerObjectClear(chunk, localX, localZ)) continue;

                float treeRoll = Hash01(seed, cellX, cellZ, 3);
                float treeChance = Mathf.Clamp01(forest * TreeDensity(biome) * (biome == "forest" ? 1.35f : 1.0f));
                if (treeCount < MaxTrees && treeRoll < treeChance)
                {
                    float scale = Mathf.Lerp(0.72f, 1.35f, Hash01(seed, cellX, cellZ, 4));
                    int species = SpeciesFor(biome, Hash01(seed, cellX, cellZ, 5));
                    AddTree(lod0, lod1, lod2, new Vector3((float)(localX + chunk.Origin.East - anchor.East),
                        ground - (float)anchor.Up, (float)(localZ + chunk.Origin.North - anchor.North)),
                        scale, species, biome);
                    treeCount++;
                }

                float underChance = Mathf.Clamp01(grass * 0.38f + forest * 0.48f + wet * 0.12f);
                if (understoryCount < MaxUnderstory &&
                    Hash01(seed, cellX, cellZ, 6) < underChance &&
                    !IsFeatureClear(chunk, localX, localZ, 2.8f))
                {
                    Vector3 p = new Vector3((float)(localX + chunk.Origin.East - anchor.East),
                        ground - (float)anchor.Up, (float)(localZ + chunk.Origin.North - anchor.North));
                    AddUnderstory(lod0, lod1, lod2, p, Hash01(seed, cellX, cellZ, 7),
                        Mathf.Lerp(0.55f, 1.35f, Hash01(seed, cellX, cellZ, 8)), biome, wet);
                    understoryCount++;
                }

                if (rockCount < MaxRocks && Hash01(seed, cellX, cellZ, 9) < rock * 0.22f)
                {
                    Vector3 p = new Vector3((float)(localX + chunk.Origin.East - anchor.East),
                        ground - (float)anchor.Up, (float)(localZ + chunk.Origin.North - anchor.North));
                    AddRock(lod0.Rock, p, Hash01(seed, cellX, cellZ, 10), 0.55f + Hash01(seed, cellX, cellZ, 11) * 1.4f);
                    AddRock(lod1.Rock, p, Hash01(seed, cellX, cellZ, 10), 0.75f + Hash01(seed, cellX, cellZ, 11));
                    rockCount++;
                }
            }

            var palette = GetPalette(biome);
            var rootGo = new GameObject(ObjectName(chunk.Id));
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.localPosition = Vector3.zero;
            var lodRenderers = new Renderer[3][];
            lodRenderers[0] = CreateLodObjects(rootGo.transform, "LOD0 • near detail", lod0, palette, chunk.Id);
            lodRenderers[1] = CreateLodObjects(rootGo.transform, "LOD1 • mid detail", lod1, palette, chunk.Id);
            lodRenderers[2] = CreateLodObjects(rootGo.transform, "LOD2 • distant silhouettes", lod2, palette, chunk.Id);
            if (lodRenderers[0].Length + lodRenderers[1].Length + lodRenderers[2].Length == 0)
            {
                UnityEngine.Object.Destroy(rootGo);
                return;
            }

            var group = rootGo.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(new[]
            {
                new LOD(0.22f, lodRenderers[0]),
                new LOD(0.075f, lodRenderers[1]),
                new LOD(0.018f, lodRenderers[2])
            });
            group.RecalculateBounds();
        }

        private static Renderer[] CreateLodObjects(Transform root, string label, LodMeshes data, Palette palette, string chunkId)
        {
            var renderers = new List<Renderer>(3);
            AddRenderer(root, label + " • canopy", data.Leaves, palette.Leaves, renderers, chunkId);
            AddRenderer(root, label + " • trunks", data.Bark, palette.Bark, renderers, chunkId);
            AddRenderer(root, label + " • stones", data.Rock, palette.Rock, renderers, chunkId);
            return renderers.ToArray();
        }

        private static void AddRenderer(Transform root, string name, MeshData data, Material material,
            List<Renderer> output, string chunkId)
        {
            if (material == null) return;
            Mesh mesh = data.ToMesh(name + " / " + chunkId);
            if (mesh == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            output.Add(renderer);
        }

        private static void AddTree(LodMeshes near, LodMeshes mid, LodMeshes far, Vector3 p,
            float scale, int species, string biome)
        {
            float height = scale * (species == 0 ? 8.0f : species == 1 ? 7.1f : 6.2f);
            float radius = scale * (species == 0 ? 2.35f : species == 1 ? 2.8f : 3.1f);
            AddCylinder(near.Bark, p, 0.18f * scale, height * 0.58f, 5);
            if (species == 0) // spruce / fir: tiered evergreen silhouette
            {
                AddCone(near.Leaves, p + Vector3.up * (height * 0.22f), radius, height * 0.52f, 7);
                AddCone(near.Leaves, p + Vector3.up * (height * 0.43f), radius * 0.78f, height * 0.48f, 7);
                AddCone(near.Leaves, p + Vector3.up * (height * 0.63f), radius * 0.56f, height * 0.42f, 7);
                AddCone(mid.Leaves, p + Vector3.up * (height * 0.24f), radius * 0.92f, height * 0.76f, 6);
            }
            else if (species == 1) // birch / aspen: narrow trunk and airy crown
            {
                AddSphere(near.Leaves, p + Vector3.up * (height * 0.78f), radius * 0.72f, height * 0.24f, 6, 5);
                AddSphere(mid.Leaves, p + Vector3.up * (height * 0.72f), radius * 0.82f, height * 0.30f, 5, 4);
            }
            else if (species == 2) // broadleaf oak / maple
            {
                AddSphere(near.Leaves, p + Vector3.up * (height * 0.70f), radius, height * 0.34f, 7, 5);
                AddSphere(near.Leaves, p + Vector3.up * (height * 0.84f), radius * 0.68f, height * 0.26f, 6, 4);
                AddSphere(mid.Leaves, p + Vector3.up * (height * 0.74f), radius * 0.92f, height * 0.40f, 5, 4);
            }
            else // willow / mountain pine: irregular hanging or compact crown
            {
                AddSphere(near.Leaves, p + Vector3.up * (height * 0.69f), radius * 0.92f, height * 0.30f, 6, 4);
                AddCone(near.Leaves, p + Vector3.up * (height * 0.48f), radius * 0.82f, height * 0.42f, 6);
                AddSphere(mid.Leaves, p + Vector3.up * (height * 0.68f), radius * 0.95f, height * 0.35f, 5, 4);
            }

            AddCylinder(mid.Bark, p, 0.14f * scale, height * 0.62f, 4);
            // Far LOD is a compact crossed silhouette rather than a full canopy mesh.
            float farHeight = height * 0.86f;
            AddCone(far.Leaves, p + Vector3.up * (farHeight * 0.28f), radius * 0.82f, farHeight * 0.70f, 5);
            AddCylinder(far.Bark, p, 0.10f * scale, height * 0.46f, 4);
        }

        private static void AddUnderstory(LodMeshes near, LodMeshes mid, LodMeshes far, Vector3 p,
            float variation, float scale, string biome, float wet)
        {
            bool marsh = IsMarsh(biome);
            float h = scale * (marsh ? 1.15f : 0.72f);
            float r = scale * (marsh ? 0.72f : 0.58f);
            if (marsh)
            {
                // Reeds: three crossed narrow blades, deliberately decorative and non-interactive.
                AddBlade(near.Leaves, p + Vector3.up * (h * 0.5f), r, h, variation * 6.28f);
                AddBlade(near.Leaves, p + Vector3.up * (h * 0.42f), r, h * 0.82f, variation * 6.28f + 1.1f);
                AddBlade(mid.Leaves, p + Vector3.up * (h * 0.45f), r * 0.85f, h * 0.8f, variation * 6.28f);
            }
            else if (IsAlpine(biome))
            {
                AddCone(near.Leaves, p + Vector3.up * (h * 0.34f), r, h * 0.72f, 5);
                AddCone(mid.Leaves, p + Vector3.up * (h * 0.34f), r * 0.9f, h * 0.7f, 4);
            }
            else if (biome == "meadow" || biome == "steppe" || biome == "coast")
            {
                // Open biomes get visible grass/reed clumps instead of a forest-like carpet of bushes.
                float bladeHeight = h * (biome == "steppe" ? 1.45f : 1.0f);
                AddBlade(near.Leaves, p + Vector3.up * (bladeHeight * 0.5f), r, bladeHeight, variation * 6.28f);
                AddBlade(near.Leaves, p + Vector3.up * (bladeHeight * 0.42f), r * 0.86f, bladeHeight * 0.84f, variation * 6.28f + 1.7f);
                AddBlade(near.Leaves, p + Vector3.up * (bladeHeight * 0.38f), r * 0.72f, bladeHeight * 0.76f, variation * 6.28f + 3.2f);
                AddBlade(mid.Leaves, p + Vector3.up * (bladeHeight * 0.42f), r * 0.9f, bladeHeight * 0.82f, variation * 6.28f);
            }
            else
            {
                AddSphere(near.Leaves, p + Vector3.up * (h * 0.46f), r, h * 0.58f, 5, 3);
                AddSphere(mid.Leaves, p + Vector3.up * (h * 0.42f), r * 0.9f, h * 0.55f, 4, 3);
                if (variation > 0.82f)
                    AddBlade(near.Leaves, p + Vector3.up * (h * 0.4f), r * 0.65f, h * 1.2f, variation * 6.28f);
            }
        }

        private static void AddRock(MeshData mesh, Vector3 p, float rotation, float scale)
        {
            float r = scale * 0.55f;
            float h = scale * 0.42f;
            Vector3 a = p + Quaternion.Euler(0, rotation * 360f, 0) * new Vector3(-r, 0, -r * 0.7f);
            Vector3 b = p + Quaternion.Euler(0, rotation * 360f, 0) * new Vector3(r, 0, -r * 0.5f);
            Vector3 c = p + Quaternion.Euler(0, rotation * 360f, 0) * new Vector3(r * 0.7f, 0, r);
            Vector3 d = p + Quaternion.Euler(0, rotation * 360f, 0) * new Vector3(-r, 0, r * 0.6f);
            Vector3 top = p + Vector3.up * h;
            mesh.Triangle(a, b, top); mesh.Triangle(b, c, top); mesh.Triangle(c, d, top); mesh.Triangle(d, a, top);
            mesh.Triangle(b, a, p); mesh.Triangle(c, b, p); mesh.Triangle(d, c, p); mesh.Triangle(a, d, p);
        }

        private static void AddCone(MeshData mesh, Vector3 p, float radius, float height, int sides)
        {
            Vector3 tip = p + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 v0 = p + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                Vector3 v1 = p + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                mesh.Triangle(v0, tip, v1);
            }
        }

        private static void AddCylinder(MeshData mesh, Vector3 p, float radius, float height, int sides)
        {
            Vector3 top = p + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 v0 = p + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                Vector3 v1 = p + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                Vector3 t0 = top + new Vector3(Mathf.Cos(a0) * radius * 0.55f, 0, Mathf.Sin(a0) * radius * 0.55f);
                Vector3 t1 = top + new Vector3(Mathf.Cos(a1) * radius * 0.55f, 0, Mathf.Sin(a1) * radius * 0.55f);
                mesh.Quad(v0, t0, t1, v1);
            }
        }

        private static void AddSphere(MeshData mesh, Vector3 p, float radius, float height, int sides, int rings)
        {
            for (int ring = 0; ring < rings; ring++)
            {
                float v0 = ring / (float)rings;
                float v1 = (ring + 1) / (float)rings;
                float y0 = Mathf.Lerp(-0.45f, 0.55f, v0) * height;
                float y1 = Mathf.Lerp(-0.45f, 0.55f, v1) * height;
                float r0 = Mathf.Sin((v0 * 0.9f + 0.05f) * Mathf.PI) * radius;
                float r1 = Mathf.Sin((v1 * 0.9f + 0.05f) * Mathf.PI) * radius;
                for (int side = 0; side < sides; side++)
                {
                    float a0 = side * Mathf.PI * 2f / sides;
                    float a1 = (side + 1) * Mathf.PI * 2f / sides;
                    Vector3 a = p + new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0);
                    Vector3 b = p + new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                    Vector3 c = p + new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
                    Vector3 d = p + new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1);
                    mesh.Quad(a, d, c, b);
                }
            }
        }

        private static void AddBlade(MeshData mesh, Vector3 p, float width, float height, float angle)
        {
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 side = new Vector3(-direction.z, 0, direction.x) * width * 0.22f;
            Vector3 tip = p + Vector3.up * height;
            Vector3 basePoint = p - Vector3.up * (height * 0.5f);
            mesh.Triangle(basePoint - side, basePoint + side, tip + direction * width * 0.18f);
        }

        private static bool TrySampleHeight(TerrainChunkFrame chunk, double x, double z, out float height)
        {
            height = 0;
            double gx = x / chunk.StepMeters;
            double gz = z / chunk.StepMeters;
            int ix = Mathf.Clamp((int)Math.Floor(gx), 0, chunk.Side - 2);
            int iz = Mathf.Clamp((int)Math.Floor(gz), 0, chunk.Side - 2);
            float tx = (float)(gx - ix);
            float tz = (float)(gz - iz);
            int i = iz * chunk.Side + ix;
            double h00 = chunk.Heights[i], h10 = chunk.Heights[i + 1];
            double h01 = chunk.Heights[i + chunk.Side], h11 = chunk.Heights[i + chunk.Side + 1];
            height = (float)Mathf.Lerp(Mathf.Lerp((float)h00, (float)h10, tx),
                Mathf.Lerp((float)h01, (float)h11, tx), tz);
            return !float.IsNaN(height) && !float.IsInfinity(height);
        }

        private static bool IsSettlementClear(TerrainChunkFrame chunk, double east, double north, double radius)
        {
            if (chunk.Settlements == null) return false;
            foreach (var settlement in chunk.Settlements)
            {
                if (settlement == null) continue;
                double dx = east - settlement.East;
                double dz = north - settlement.North;
                if (dx * dx + dz * dz < radius * radius) return true;
            }
            return false;
        }

        private static bool IsFeatureClear(TerrainChunkFrame chunk, double x, double z, float margin)
        {
            if (chunk.Roads != null)
                foreach (var road in chunk.Roads)
                    if (NearPolyline(road.Points, x, z, Math.Max(margin, road.WidthMeters * 0.5 + 1.2))) return true;
            if (chunk.Water != null)
                foreach (var water in chunk.Water)
                    if (NearPolyline(water.Points, x, z, Math.Max(margin, water.WidthMeters * 0.5 + 2.0))) return true;
            return false;
        }

        private static bool IsServerObjectClear(TerrainChunkFrame chunk, double x, double z)
        {
            // Do not scatter decorative vegetation on top of server-described resource nodes or wild landmarks.
            // These hints are sparse today; exact flora descriptors will replace local scatter when the server contract lands.
            if (chunk.Resources != null)
                foreach (var resource in chunk.Resources)
                {
                    if (resource == null) continue;
                    double dx = x - resource.X, dz = z - resource.Z;
                    if (dx * dx + dz * dz < 25.0) return true;
                }
            if (chunk.Wild != null)
                foreach (var wild in chunk.Wild)
                {
                    if (wild == null) continue;
                    double dx = x - wild.X, dz = z - wild.Z;
                    double radius = Math.Max(8.0, wild.RadiusMeters + 3.0);
                    if (dx * dx + dz * dz < radius * radius) return true;
                }
            return false;
        }

        private static bool NearPolyline(List<TerrainFeaturePointFrame> points, double x, double z, double radius)
        {
            if (points == null || points.Count == 0) return false;
            if (points.Count == 1)
            {
                double dx = x - points[0].X, dz = z - points[0].Z;
                return dx * dx + dz * dz < radius * radius;
            }
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double len2 = dx * dx + dz * dz;
                double t = len2 > 0.0001 ? Math.Max(0, Math.Min(1, ((x - a.X) * dx + (z - a.Z) * dz) / len2)) : 0;
                double px = a.X + dx * t, pz = a.Z + dz * t;
                double ox = x - px, oz = z - pz;
                if (ox * ox + oz * oz < radius * radius) return true;
            }
            return false;
        }

        private static Palette GetPalette(string biome)
        {
            Palette cached;
            if (Palettes.TryGetValue(biome, out cached)) return cached;
            Color leaves = IsMarsh(biome) ? new Color(0.31f, 0.40f, 0.22f) :
                IsAlpine(biome) ? new Color(0.28f, 0.38f, 0.25f) :
                biome == "coast" ? new Color(0.34f, 0.46f, 0.31f) :
                biome == "steppe" ? new Color(0.47f, 0.45f, 0.24f) :
                biome == "deciduous_forest" ? new Color(0.31f, 0.43f, 0.22f) :
                biome == "coniferous_forest" ? new Color(0.20f, 0.34f, 0.22f) :
                new Color(0.34f, 0.43f, 0.25f);
            var palette = new Palette
            {
                Leaves = MakeMaterial("Frontier foliage • " + biome, leaves, 0.02f),
                Bark = MakeMaterial("Frontier bark • " + biome, new Color(0.29f, 0.22f, 0.15f), 0f),
                Rock = MakeMaterial("Frontier stone • " + biome, IsAlpine(biome) ? new Color(0.43f, 0.44f, 0.42f) : new Color(0.37f, 0.36f, 0.32f), 0.04f)
            };
            Palettes.Add(biome, palette);
            return palette;
        }

        private static Material MakeMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            // Grass/reed blades are single planes; keep both sides visible in URP.
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
            return material;
        }

        private static string NormalizeBiome(string value)
        {
            string b = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            if (b.Contains("marsh") || b.Contains("swamp") || b.Contains("wetland") || b.Contains("bog")) return "marsh";
            if (b.Contains("mountain") || b.Contains("alpine") || b.Contains("highland") || b.Contains("rock") || b == "hills") return "alpine";
            if (b == "coast" || b.Contains("shore")) return "coast";
            if (b == "steppe") return "steppe";
            if (b == "plain" || b == "plains") return "meadow";
            if (b.Contains("conifer") || b.Contains("taiga") || b.Contains("pine") || b.Contains("spruce")) return "coniferous_forest";
            if (b.Contains("deciduous") || b.Contains("broadleaf") || b.Contains("oak") || b.Contains("birch")) return "deciduous_forest";
            if (b.Contains("forest") || b.Contains("wood")) return "mixed_forest";
            if (b.Contains("meadow") || b.Contains("grass") || b.Contains("plain") || b.Contains("steppe")) return "meadow";
            return string.IsNullOrEmpty(b) ? "meadow" : b;
        }

        private static int SpeciesFor(string biome, float roll)
        {
            if (biome == "coniferous_forest" || IsAlpine(biome)) return roll < 0.82f ? 0 : 3;
            if (biome == "deciduous_forest") return roll < 0.48f ? 1 : 2;
            if (IsMarsh(biome) || biome == "coast") return roll < 0.78f ? 3 : 1;
            if (biome == "steppe") return roll < 0.65f ? 1 : 3;
            return roll < 0.38f ? 0 : roll < 0.70f ? 1 : 2;
        }

        private static float TreeDensity(string biome) => IsAlpine(biome) ? 0.28f : IsMarsh(biome) ? 0.18f :
            biome == "meadow" ? 0.12f : biome == "steppe" ? 0.08f : biome == "coast" ? 0.24f : 0.82f;
        private static float DefaultForest(string biome) => IsAlpine(biome) ? 0.12f : IsMarsh(biome) ? 0.18f :
            biome == "meadow" ? 0.08f : biome == "steppe" ? 0.02f : biome == "coast" ? 0.15f : 0.62f;
        private static float DefaultGrass(string biome) => IsMarsh(biome) ? 0.72f : IsAlpine(biome) ? 0.22f :
            biome == "steppe" ? 0.85f : biome == "coast" ? 0.45f : 0.75f;
        private static float DefaultRock(string biome) => IsAlpine(biome) ? 0.72f : 0.12f;
        private static float DefaultWet(string biome) => IsMarsh(biome) ? 0.82f : 0.08f;
        private static bool IsMarsh(string biome) => biome == "marsh";
        private static bool IsAlpine(string biome) => biome == "alpine";
        private static float Clamp01(double value) => Mathf.Clamp01((float)value);
        private static long FloorDiv(long a, long b) => (long)Math.Floor(a / (double)b);

        private static long StableHash(long x, long z, long salt)
        {
            unchecked
            {
                ulong h = (ulong)x * 0x9E3779B185EBCA87UL;
                h ^= (ulong)z * 0xC2B2AE3D27D4EB4FUL;
                h ^= (ulong)salt * 0x165667B19E3779F9UL;
                h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 27; h *= 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return (long)h;
            }
        }

        private static float Hash01(long seed, long x, long z, long salt)
        {
            long h = StableHash(x ^ seed, z, salt);
            return (float)((ulong)h / (double)ulong.MaxValue);
        }
    }
}
