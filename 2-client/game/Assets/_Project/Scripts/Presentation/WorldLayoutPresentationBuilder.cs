using System;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine.Rendering;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Renders the server-owned Layout v2 footprint and countryside. Coordinates are metres, +X east, +Z north.</summary>
    public static class WorldLayoutPresentationBuilder
    {
        private static Material _ground;
        private static Material _water;
        private static Material _timber;
        private static Material _roof;
        private static Material _field;
        private static Material _forest;
        private static Material _trunk;
        private static Material _road;
        private static Material _straw;
        private static Material _scarecrowCloth;
        private static Material _grass;
        private static Material _stone;
        private static readonly System.Collections.Generic.List<UnityEngine.Object> _generatedAssets =
            new System.Collections.Generic.List<UnityEngine.Object>();

        public static void Build(Transform root, WorldLayoutFrame layout)
        {
            if (root == null || layout == null || layout.Version != 2 || root.Find("ServerLayoutV2") != null) return;
            var world = new GameObject("ServerLayoutV2").transform;
            world.SetParent(root, false);
            _ground = MakeTexturedMaterial("Layout ground • meadow", new Color(0.36f, 0.43f, 0.25f), 0, 10f, 10f);
            _water = MakeTexturedMaterial("Layout river • current", new Color(0.17f, 0.36f, 0.43f), 5, 2f, 18f);
            _timber = MakeTexturedMaterial("Layout timber • weathered boards", new Color(0.30f, 0.20f, 0.13f), 2, 2f, 2f);
            _roof = MakeTexturedMaterial("Layout thatch • straw courses", new Color(0.24f, 0.20f, 0.14f), 3, 2f, 3f);
            _field = MakeTexturedMaterial("Layout field • rough soil", new Color(0.57f, 0.57f, 0.30f), 1, 8f, 8f);
            _forest = MakeTexturedMaterial("Layout woodland • foliage", new Color(0.13f, 0.25f, 0.15f), 0, 5f, 5f);
            _trunk = MakeTexturedMaterial("Layout trunks • bark", new Color(0.25f, 0.16f, 0.10f), 2, 1f, 3f);
            _road = MakeTexturedMaterial("Layout country roads • packed dirt", new Color(0.38f, 0.30f, 0.22f), 1, 2f, 12f);
            _straw = MakeTexturedMaterial("Farm straw • dry stalks", new Color(0.69f, 0.53f, 0.24f), 3, 2f, 2f);
            _scarecrowCloth = MakeTexturedMaterial("Scarecrow faded cloth", new Color(0.42f, 0.30f, 0.20f), 2, 1f, 1f);
            _grass = MakeTexturedMaterial("Meadow grass • muted olive", new Color(0.39f, 0.48f, 0.25f), 0, 1f, 1f);
            _stone = MakeTexturedMaterial("Building stone • weathered masonry", new Color(0.38f, 0.37f, 0.32f), 4, 1.5f, 2f);

            // Water and bridge are placed from the exact server geometry.
            var river = Create("River • server layout", PrimitiveType.Cube, world,
                new Vector3(0f, 0.025f, (float)((layout.River.Y0 + layout.River.Y1) * 0.5)),
                new Vector3((float)(layout.WorldHalf * 2), 0.05f, (float)(layout.River.Y1 - layout.River.Y0)), _water, false);
            var bridgeWidth = (float)(layout.River.BridgeX1 - layout.River.BridgeX0);
            Create("Bridge • server layout", PrimitiveType.Cube, world,
                new Vector3((float)((layout.River.BridgeX0 + layout.River.BridgeX1) * 0.5), 0.12f,
                    (float)((layout.River.Y0 + layout.River.Y1) * 0.5)),
                new Vector3(bridgeWidth, 0.24f, (float)(layout.River.Y1 - layout.River.Y0) + 0.8f), _timber, false);
            BuildRiverSurface(world, layout);
            BuildRiverBankDetails(world, layout);

            if (layout.Buildings != null)
            {
                foreach (var b in layout.Buildings)
                {
                    if (b == null) continue;
                    var width = (float)(b.X1 - b.X0);
                    var depth = (float)(b.Y1 - b.Y0);
                    var collision = new GameObject("Server collision • " + b.Kind);
                    collision.transform.SetParent(world, false);
                    collision.transform.localPosition = new Vector3(
                        (float)((b.X0 + b.X1) * 0.5), 2.35f, (float)((b.Y0 + b.Y1) * 0.5));
                    collision.layer = 2; // Same obstruction layer used by the presentation camera.
                    var collider = collision.AddComponent<BoxCollider>();
                    collider.size = new Vector3(width, 4.7f, depth);

                    // Match visible mass to the authoritative obstruction footprint.
                    // Every server-blocked building must have a matching visible mass.
                    // Legacy house/granary meshes are intentionally retained as detail inside
                    // these footprint-sized shells.
                    BuildFootprintBuilding(world, b, width, depth);
                }
            }

            // Broad, readable agricultural and woodland zones establish the scale of the 300 m world.
            Create("West forest floor", PrimitiveType.Cube, world, new Vector3(-108f, 0.025f, 0f),
                new Vector3(78f, 0.12f, 280f), _forest, false);
            Create("East grain country", PrimitiveType.Cube, world, new Vector3(103f, 0.025f, 10f),
                new Vector3(88f, 0.10f, 260f), _field, false);
            Create("North meadow", PrimitiveType.Cube, world, new Vector3(8f, 0.025f, 112f),
                new Vector3(115f, 0.10f, 64f), _field, false);

            // Long crop strips make the eastern agricultural zone read as cultivated land.
            // They are presentation-only: no colliders, gameplay harvest state or navigation data.
            BuildCropRows(world);
            BuildMeadowGrass(world);
            BuildFarmProps(world);
            BuildWoodcutterCamp(world);

            // The same roads connect the server POIs: woods, fields, mine and river bridge.
            if (layout.Pois.TryGetValue("woods", out var woods))
                AddRoad(world, Vector2.zero, new Vector2((float)woods.X, (float)woods.Y), 3.2f);
            if (layout.Pois.TryGetValue("garden", out var garden))
                AddRoad(world, Vector2.zero, new Vector2((float)garden.X, (float)garden.Y), 3.0f);
            if (layout.Pois.TryGetValue("mine", out var mine) && garden != null)
                AddRoad(world, new Vector2((float)garden.X, (float)garden.Y), new Vector2((float)mine.X, (float)mine.Y), 2.5f);
            AddRoad(world, Vector2.zero, new Vector2(20f, 0f), 2.8f);
            AddRoad(world, new Vector2(20f, 0f), new Vector2(20f, -60f), 2.8f);
            var bridgeCenter = new Vector2(
                (float)((layout.River.BridgeX0 + layout.River.BridgeX1) * 0.5),
                (float)((layout.River.Y0 + layout.River.Y1) * 0.5));
            AddRoad(world, new Vector2(20f, -60f), bridgeCenter, 2.8f);
            AddRoad(world, bridgeCenter, new Vector2(bridgeCenter.x, -110f), 2.8f);
            AddRoad(world, new Vector2(20f, 0f), new Vector2(20f, (float)layout.WorldHalf - 10f), 2.8f);
            var random = new System.Random(417);
            var trunkVertices = new System.Collections.Generic.List<Vector3>(3000);
            var trunkTriangles = new System.Collections.Generic.List<int>(6000);
            var canopyVertices = new System.Collections.Generic.List<Vector3>(12000);
            var canopyTriangles = new System.Collections.Generic.List<int>(24000);
            for (var i = 0; i < 72; i++)
            {
                var x = -145f + (float)random.NextDouble() * 70f;
                var z = -142f + (float)random.NextDouble() * 284f;
                if (Math.Abs(z + 84f) < 9f || Math.Abs(x) < 3f && z > -50f && z < 50f) continue;
                var height = 4f + (float)random.NextDouble() * 5f;
                var radius = 1.65f + (float)random.NextDouble() * 1.05f;
                AddLowPolyTree(trunkVertices, trunkTriangles, canopyVertices, canopyTriangles,
                    new Vector3(x, 0f, z), height, radius, random);
            }
            CreateCombinedMesh(world, "Far woodland • combined trunks", trunkVertices, trunkTriangles, _trunk);
            CreateCombinedMesh(world, "Far woodland • low-poly canopies", canopyVertices, canopyTriangles, _forest);
        }

        private static void AddLowPolyTree(
            System.Collections.Generic.List<Vector3> trunkVertices,
            System.Collections.Generic.List<int> trunkTriangles,
            System.Collections.Generic.List<Vector3> canopyVertices,
            System.Collections.Generic.List<int> canopyTriangles,
            Vector3 position, float height, float radius, System.Random random)
        {
            // Six-sided tapered trunk, with three irregular foliage tiers instead of a single sphere.
            var trunkBase = trunkVertices.Count;
            const int trunkSides = 6;
            var trunkRadius = 0.20f + (float)random.NextDouble() * 0.10f;
            for (var ring = 0; ring < 2; ring++)
            for (var side = 0; side < trunkSides; side++)
            {
                var angle = side * Mathf.PI * 2f / trunkSides;
                var r = trunkRadius * (ring == 0 ? 1f : 0.72f);
                trunkVertices.Add(position + new Vector3(Mathf.Cos(angle) * r,
                    ring == 0 ? 0.05f : height * 0.72f,
                    Mathf.Sin(angle) * r));
            }
            for (var side = 0; side < trunkSides; side++)
            {
                var next = (side + 1) % trunkSides;
                trunkTriangles.Add(trunkBase + side);
                trunkTriangles.Add(trunkBase + trunkSides + next);
                trunkTriangles.Add(trunkBase + next);
                trunkTriangles.Add(trunkBase + side);
                trunkTriangles.Add(trunkBase + trunkSides + side);
                trunkTriangles.Add(trunkBase + trunkSides + next);
            }

            var tiers = 3;
            for (var tier = 0; tier < tiers; tier++)
            {
                var tierBase = canopyVertices.Count;
                const int sides = 7;
                var tierY = height * (0.38f + tier * 0.19f);
                var tierHeight = height * (0.50f - tier * 0.055f);
                var tierRadius = radius * (1f - tier * 0.22f);
                var rotation = (float)random.NextDouble() * Mathf.PI * 2f;
                var wobble = 0.10f + (float)random.NextDouble() * 0.12f;
                for (var ring = 0; ring < 2; ring++)
                for (var side = 0; side < sides; side++)
                {
                    var angle = rotation + side * Mathf.PI * 2f / sides;
                    var irregular = 1f + Mathf.Sin(side * 2.17f + tier) * wobble;
                    var r = tierRadius * (ring == 0 ? irregular : 0.08f);
                    canopyVertices.Add(position + new Vector3(Mathf.Cos(angle) * r,
                        tierY + (ring == 0 ? 0f : tierHeight),
                        Mathf.Sin(angle) * r));
                }
                for (var side = 0; side < sides; side++)
                {
                    var next = (side + 1) % sides;
                    canopyTriangles.Add(tierBase + side);
                    canopyTriangles.Add(tierBase + sides + next);
                    canopyTriangles.Add(tierBase + next);
                    canopyTriangles.Add(tierBase + side);
                    canopyTriangles.Add(tierBase + sides + side);
                    canopyTriangles.Add(tierBase + sides + next);
                }
            }
        }

        private static void CreateCombinedMesh(Transform parent, string objectName,
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles, Material material)
        {
            if (vertices.Count == 0 || triangles.Count == 0) return;
            var mesh = new Mesh { name = objectName };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _generatedAssets.Add(mesh);
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void BuildRiverSurface(Transform parent, WorldLayoutFrame layout)
        {
            const int xSegments = 60;
            const int zSegments = 4;
            var xMin = (float)-layout.WorldHalf;
            var xMax = (float)layout.WorldHalf;
            var zMin = (float)layout.River.Y0;
            var zMax = (float)layout.River.Y1;
            var vertices = new Vector3[(xSegments + 1) * (zSegments + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[xSegments * zSegments * 6];
            for (var z = 0; z <= zSegments; z++)
            for (var x = 0; x <= xSegments; x++)
            {
                var px = Mathf.Lerp(xMin, xMax, x / (float)xSegments);
                var pz = Mathf.Lerp(zMin, zMax, z / (float)zSegments);
                var wave = Mathf.Sin(px * 0.12f + pz * 0.42f) * 0.009f
                    + Mathf.Sin(px * 0.047f - pz * 0.73f) * 0.006f;
                var index = z * (xSegments + 1) + x;
                vertices[index] = new Vector3(px, 0.056f + wave, pz);
                uv[index] = new Vector2(x / (float)xSegments, z / (float)zSegments);
            }
            var t = 0;
            for (var z = 0; z < zSegments; z++)
            for (var x = 0; x < xSegments; x++)
            {
                var a = z * (xSegments + 1) + x;
                var b = a + 1;
                var d = a + xSegments + 1;
                var c = d + 1;
                // Up-facing triangles.
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }
            var mesh = new Mesh { name = "River surface • subtle low-poly current" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var water = new GameObject("River surface • rippled overlay");
            water.transform.SetParent(parent, false);
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = water.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void BuildRiverBankDetails(Transform parent, WorldLayoutFrame layout)
        {
            var random = new System.Random(6137);
            var vertices = new System.Collections.Generic.List<Vector3>(1800);
            var triangles = new System.Collections.Generic.List<int>(2700);
            var northBank = (float)layout.River.Y1 + 0.55f;
            var southBank = (float)layout.River.Y0 - 0.55f;
            for (var x = -74f; x <= 74f; x += 3.8f)
            {
                var px = x + ((float)random.NextDouble() - 0.5f) * 1.4f;
                if (px > (float)layout.River.BridgeX0 - 4f && px < (float)layout.River.BridgeX1 + 4f) continue;
                if (Mathf.Abs(px - 13.45f) < 5f) continue; // leave the fishing landing open
                for (var bank = 0; bank < 2; bank++)
                {
                    var pz = bank == 0 ? northBank : southBank;
                    var center = new Vector3(px, 0.08f, pz + ((float)random.NextDouble() - 0.5f) * 0.7f);
                    for (var blade = 0; blade < 3; blade++)
                    {
                        var angle = blade * Mathf.PI / 3f + (float)random.NextDouble() * 0.45f;
                        AddGrassBlade(vertices, triangles, center,
                            0.42f + (float)random.NextDouble() * 0.48f,
                            0.045f + (float)random.NextDouble() * 0.035f, angle);
                    }
                }
            }
            CreateCombinedMesh(parent, "River banks • combined reeds", vertices, triangles, _grass);
        }

        private static void BuildMeadowGrass(Transform parent)
        {
            // One combined low-poly mesh dresses the open north meadow without adding colliders.
            // Keep the central northbound road corridor clear for players and NPC navigation.
            var random = new System.Random(90210);
            var vertices = new System.Collections.Generic.List<Vector3>(9000);
            var triangles = new System.Collections.Generic.List<int>(13500);
            for (var i = 0; i < 1050; i++)
            {
                var x = -48f + (float)random.NextDouble() * 113f;
                var z = 82f + (float)random.NextDouble() * 59f;
                if (Mathf.Abs(x - 20f) < 4.5f) continue;
                var height = 0.20f + (float)random.NextDouble() * 0.38f;
                var width = 0.045f + (float)random.NextDouble() * 0.045f;
                var center = new Vector3(x, 0.075f, z);
                AddGrassBlade(vertices, triangles, center, height, width, (float)random.NextDouble() * 0.8f);
                AddGrassBlade(vertices, triangles, center, height * 0.82f, width,
                    Mathf.PI * 0.5f + (float)random.NextDouble() * 0.8f);
            }
            CreateCombinedMesh(parent, "North meadow • combined grass tufts", vertices, triangles, _grass);
        }

        private static void AddGrassBlade(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles,
            Vector3 center, float height, float width, float angle)
        {
            var start = vertices.Count;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var lean = new Vector3(-direction.z, 0f, direction.x) * (width * 0.65f)
                + direction * (width * 0.45f);
            var half = direction * width;
            vertices.Add(center - half);
            vertices.Add(center + half);
            vertices.Add(center + lean + Vector3.up * height);
            // Double-sided grass planes are visible from both sides.
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        private static void BuildCropRows(Transform parent)
        {
            var random = new System.Random(73191);
            var vertices = new System.Collections.Generic.List<Vector3>(12000);
            var triangles = new System.Collections.Generic.List<int>(18000);

            // The field occupies the east side of the current 300 x 300 m layout.
            // Leave a broad buffer around the river and keep all crop geometry inside world bounds.
            const float firstRowX = 68f;
            const float lastRowX = 140f;
            const float rowSpacing = 4.8f;
            for (var x = firstRowX; x <= lastRowX; x += rowSpacing)
            {
                AddCropSegment(parent, vertices, triangles, random, x, -137f, -98f);
                AddCropSegment(parent, vertices, triangles, random, x, -68f, 137f);
            }

            var mesh = new Mesh { name = "Layout v2 • cultivated grain rows" };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var crops = new GameObject("Cultivated grain • combined low-poly rows");
            crops.transform.SetParent(parent, false);
            crops.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = crops.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _field;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void AddCropSegment(Transform parent,
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles,
            System.Random random, float x, float zStart, float zEnd)
        {
            var length = zEnd - zStart;
            if (length <= 0f) return;

            // Slightly raised, dark earth ridges create visible alternating furrows.
            var ridge = Create("Cultivated furrow", PrimitiveType.Cube, parent,
                new Vector3(x, 0.085f, (zStart + zEnd) * 0.5f),
                new Vector3(0.72f, 0.10f, length), _trunk, false);
            ridge.isStatic = true;

            for (var z = zStart + 1.8f; z < zEnd - 1f; z += 3.1f)
            {
                var center = new Vector3(x + ((float)random.NextDouble() - 0.5f) * 0.34f,
                    0.16f, z + ((float)random.NextDouble() - 0.5f) * 0.8f);
                var height = 0.38f + (float)random.NextDouble() * 0.38f;
                var width = 0.10f + (float)random.NextDouble() * 0.07f;
                AddCropBillboard(vertices, triangles, center, height, width, true);
                AddCropBillboard(vertices, triangles, center, height * 0.92f, width, false);
            }
        }

        private static void AddCropBillboard(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles,
            Vector3 center, float height, float width, bool alongX)
        {
            var start = vertices.Count;
            var half = alongX ? new Vector3(width, 0f, 0f) : new Vector3(0f, 0f, width);
            var lean = alongX ? new Vector3(width * 0.18f, 0f, width * 0.12f)
                              : new Vector3(-width * 0.12f, 0f, width * 0.18f);
            vertices.Add(center - half);
            vertices.Add(center + half);
            vertices.Add(center + lean + Vector3.up * height);
            vertices.Add(center + lean * 0.35f + Vector3.up * (height * 0.55f));

            // Double-sided quads remain visible from both sides of the field rows.
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start);
            triangles.Add(start + 2); triangles.Add(start + 3); triangles.Add(start);
        }

        private static void BuildFarmProps(Transform parent)
        {
            // Small, non-interactive dressing props. They deliberately have no colliders:
            // server Layout v2 remains the only source of gameplay obstruction.
            var scarecrow = new GameObject("Farm scarecrow • presentation only").transform;
            scarecrow.SetParent(parent, false);
            scarecrow.localPosition = new Vector3(116f, 0f, 42f);

            Create("Scarecrow oak stake", PrimitiveType.Cylinder, scarecrow,
                new Vector3(0f, 1.05f, 0f), new Vector3(0.09f, 1.05f, 0.09f), _trunk, false);
            var crossbar = Create("Scarecrow crossbar", PrimitiveType.Cylinder, scarecrow,
                new Vector3(0f, 1.55f, 0f), new Vector3(0.065f, 0.62f, 0.065f), _trunk, false);
            crossbar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Create("Scarecrow worn tunic", PrimitiveType.Capsule, scarecrow,
                new Vector3(0f, 1.18f, 0f), new Vector3(0.34f, 0.43f, 0.20f), _scarecrowCloth, false);
            Create("Scarecrow sack head", PrimitiveType.Sphere, scarecrow,
                new Vector3(0f, 1.86f, 0f), new Vector3(0.31f, 0.34f, 0.28f), _straw, false);
            Create("Scarecrow hat brim", PrimitiveType.Cylinder, scarecrow,
                new Vector3(0f, 2.04f, 0f), new Vector3(0.27f, 0.035f, 0.27f), _trunk, false);
            Create("Scarecrow hat crown", PrimitiveType.Cylinder, scarecrow,
                new Vector3(0f, 2.15f, 0f), new Vector3(0.16f, 0.11f, 0.16f), _trunk, false);

            BuildHayBale(parent, new Vector3(136f, 0.47f, 105f), 0.92f);
            BuildHayBale(parent, new Vector3(139f, 0.47f, 108f), 0.78f);
            BuildHayBale(parent, new Vector3(134f, 0.47f, 111f), 0.72f);
        }

        private static void BuildHayBale(Transform parent, Vector3 position, float scale)
        {
            var bale = Create("Field hay bale • low-poly", PrimitiveType.Cylinder, parent,
                position, new Vector3(0.52f * scale, 0.46f * scale, 0.52f * scale), _straw, false);
            bale.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Two dark twine bands make the silhouette legible without adding separate meshes.
            for (var side = -1; side <= 1; side += 2)
            {
                var band = Create("Hay bale binding", PrimitiveType.Cube, bale.transform,
                    new Vector3(side * 0.17f, 0f, 0f),
                    new Vector3(0.035f, 0.94f, 0.94f), _trunk, false);
            }
        }


        private static void BuildWoodcutterCamp(Transform parent)
        {
            // A small woodcutter's work spot beside the western woods road.
            // All pieces are presentation-only; movement and action validation remain server-owned.
            var camp = new GameObject("Woodcutter camp • presentation only").transform;
            camp.SetParent(parent, false);
            camp.localPosition = new Vector3(-82f, 0f, 24f);

            Create("Chopping block", PrimitiveType.Cylinder, camp,
                new Vector3(0f, 0.34f, 0f), new Vector3(0.46f, 0.34f, 0.46f), _trunk, false);
            Create("Fresh split log", PrimitiveType.Cylinder, camp,
                new Vector3(0f, 0.73f, 0f), new Vector3(0.17f, 0.20f, 0.17f), _straw, false);

            // Neatly stacked firewood: three lower logs, two above, one at the crown.
            for (var row = 0; row < 3; row++)
            {
                var count = 3 - row;
                for (var i = 0; i < count; i++)
                {
                    var x = (i - (count - 1) * 0.5f) * 0.43f;
                    var z = 1.35f + row * 0.34f;
                    CreateCampLog(camp, new Vector3(x, 0.24f + row * 0.31f, z),
                        1.35f - row * 0.12f, 0.15f - row * 0.015f);
                }
            }

            Create("Firewood support left", PrimitiveType.Cylinder, camp,
                new Vector3(-0.78f, 0.18f, 1.35f), new Vector3(0.10f, 0.18f, 0.10f), _trunk, false);
            Create("Firewood support right", PrimitiveType.Cylinder, camp,
                new Vector3(0.78f, 0.18f, 1.35f), new Vector3(0.10f, 0.18f, 0.10f), _trunk, false);
        }

        private static void CreateCampLog(Transform parent, Vector3 position, float length, float radius)
        {
            var log = Create("Stacked firewood log", PrimitiveType.Cylinder, parent,
                position, new Vector3(radius, length * 0.5f, radius), _trunk, false);
            log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static void BuildFootprintBuilding(Transform parent, WorldRectFrame building,
            float width, float depth)
        {
            var center = new Vector3((float)((building.X0 + building.X1) * 0.5), 0f,
                (float)((building.Y0 + building.Y1) * 0.5));
            var root = new GameObject("Footprint facade • " + building.Kind).transform;
            root.SetParent(parent, false);
            root.localPosition = center;

            var isMarket = building.Kind == "market";
            var wallHeight = isMarket ? 2.8f : 3.8f;
            var wall = _timber;
            var roof = _roof;
            Create("Building plinth", PrimitiveType.Cube, root,
                new Vector3(0f, 0.18f, 0f), new Vector3(width, 0.36f, depth), _trunk, false);

            // Back and side walls. The south-facing entrance stays open so the settlement
            // remains readable from the main approach; server collision remains authoritative.
            Create("Rear timber wall", PrimitiveType.Cube, root,
                new Vector3(0f, wallHeight * 0.5f, depth * 0.5f - 0.12f),
                new Vector3(width - 0.35f, wallHeight, 0.24f), wall, false);
            Create("West timber wall", PrimitiveType.Cube, root,
                new Vector3(-width * 0.5f + 0.12f, wallHeight * 0.5f, 0f),
                new Vector3(0.24f, wallHeight, depth - 0.25f), wall, false);
            Create("East timber wall", PrimitiveType.Cube, root,
                new Vector3(width * 0.5f - 0.12f, wallHeight * 0.5f, 0f),
                new Vector3(0.24f, wallHeight, depth - 0.25f), wall, false);

            var opening = isMarket ? width * 0.28f : width * 0.34f;
            var sideWidth = (width - opening) * 0.5f;
            Create("South facade left", PrimitiveType.Cube, root,
                new Vector3(-width * 0.5f + sideWidth * 0.5f, wallHeight * 0.5f, -depth * 0.5f + 0.12f),
                new Vector3(sideWidth, wallHeight, 0.24f), wall, false);
            Create("South facade right", PrimitiveType.Cube, root,
                new Vector3(width * 0.5f - sideWidth * 0.5f, wallHeight * 0.5f, -depth * 0.5f + 0.12f),
                new Vector3(sideWidth, wallHeight, 0.24f), wall, false);

            for (var side = -1; side <= 1; side += 2)
            {
                Create("Facade timber post", PrimitiveType.Cube, root,
                    new Vector3(side * (opening * 0.5f), wallHeight * 0.5f, -depth * 0.5f + 0.12f),
                    new Vector3(0.34f, wallHeight + 0.15f, 0.34f), _trunk, false);
            }

            // Exposed structural framing breaks up the large flat wall planes.
            for (var y = 0; y < 2; y++)
            {
                var beamY = 0.72f + y * 1.65f;
                Create("Front horizontal timber beam", PrimitiveType.Cube, root,
                    new Vector3(0f, beamY, -depth * 0.5f + 0.02f),
                    new Vector3(width - 0.12f, 0.16f, 0.32f), _trunk, false);
                Create("Rear horizontal timber beam", PrimitiveType.Cube, root,
                    new Vector3(0f, beamY, depth * 0.5f - 0.02f),
                    new Vector3(width - 0.12f, 0.16f, 0.32f), _trunk, false);
            }

            // A dark recessed door and small framed windows give each footprint a readable facade.
            var doorWidth = Mathf.Min(1.25f, opening * 0.52f);
            Create("Shadowed doorway", PrimitiveType.Cube, root,
                new Vector3(0f, 1.05f, -depth * 0.5f - 0.015f),
                new Vector3(doorWidth, 2.05f, 0.08f), _trunk, false);
            Create("Oak door leaf", PrimitiveType.Cube, root,
                new Vector3(0f, 1.02f, -depth * 0.5f - 0.07f),
                new Vector3(doorWidth - 0.12f, 1.88f, 0.055f), _timber, false);
            Create("Door iron latch", PrimitiveType.Sphere, root,
                new Vector3(doorWidth * 0.32f, 1.02f, -depth * 0.5f - 0.12f),
                new Vector3(0.09f, 0.09f, 0.05f), _trunk, false);

            for (var side = -1; side <= 1; side += 2)
            {
                var windowX = side * width * 0.31f;
                var window = Create("Window dark recess", PrimitiveType.Cube, root,
                    new Vector3(windowX, 1.95f, -depth * 0.5f - 0.015f),
                    new Vector3(0.82f, 0.92f, 0.07f), _trunk, false);
                Create("Window shutter", PrimitiveType.Cube, root,
                    new Vector3(windowX, 1.95f, -depth * 0.5f - 0.065f),
                    new Vector3(0.66f, 0.76f, 0.05f), _roof, false);
                Create("Window crossbar", PrimitiveType.Cube, root,
                    new Vector3(windowX, 1.95f, -depth * 0.5f - 0.105f),
                    new Vector3(0.72f, 0.07f, 0.04f), _trunk, false);
            }

            // A broad two-slope roof reads at village scale and is not just a small stall canopy.
            var slopeWidth = width * 0.56f;
            var roofA = Create("Roof slope west", PrimitiveType.Cube, root,
                new Vector3(-width * 0.25f, wallHeight + 0.45f, 0f),
                new Vector3(slopeWidth, 0.42f, depth + 0.7f), roof, false);
            roofA.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            var roofB = Create("Roof slope east", PrimitiveType.Cube, root,
                new Vector3(width * 0.25f, wallHeight + 0.45f, 0f),
                new Vector3(slopeWidth, 0.42f, depth + 0.7f), roof, false);
            roofB.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);

            // Close the triangular roof ends so the building reads as a complete timber house.
            CreateGableEnd(root, width, depth, wallHeight, wallHeight + width * 0.1335f, -1f);
            CreateGableEnd(root, width, depth, wallHeight, wallHeight + width * 0.1335f, 1f);
            var ridge = Create("Roof ridge timber", PrimitiveType.Cylinder, root,
                new Vector3(0f, wallHeight + width * 0.1335f, 0f),
                new Vector3(0.13f, depth * 0.62f, 0.13f), _trunk, false);
            ridge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            if (building.Kind == "tavern" || building.Kind == "smithy")
            {
                var chimney = Create("Stone chimney stack", PrimitiveType.Cube, root,
                    new Vector3(-width * 0.28f, wallHeight + 0.92f, depth * 0.18f),
                    new Vector3(0.78f, 1.85f, 0.82f), _stone, false);
                Create("Chimney crown", PrimitiveType.Cube, root,
                    new Vector3(-width * 0.28f, wallHeight + 1.88f, depth * 0.18f),
                    new Vector3(0.98f, 0.16f, 1.02f), _trunk, false);
            }
        }

        private static void CreateGableEnd(Transform parent, float width, float depth,
            float wallHeight, float peakHeight, float side)
        {
            var z = side * (depth * 0.5f - 0.10f);
            var mesh = new Mesh { name = "Low-poly timber gable" };
            mesh.vertices = new[]
            {
                new Vector3(-width * 0.5f + 0.12f, wallHeight, z),
                new Vector3(width * 0.5f - 0.12f, wallHeight, z),
                new Vector3(0f, peakHeight, z)
            };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 0 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _generatedAssets.Add(mesh);
            var gable = new GameObject("Timber gable end");
            gable.transform.SetParent(parent, false);
            gable.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gable.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _timber;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            // Dark half-timber braces form a visible truss beneath the roof peak.
            Create("Gable king post", PrimitiveType.Cube, parent,
                new Vector3(0f, wallHeight + (peakHeight - wallHeight) * 0.48f, z - side * 0.04f),
                new Vector3(0.13f, peakHeight - wallHeight, 0.12f), _trunk, false);
        }

        private static void AddRoad(Transform parent, Vector2 start, Vector2 end, float width)
        {
            var delta = end - start;
            var center = (start + end) * 0.5f;
            var rotation = Quaternion.Euler(0f, Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 0f);
            var road = Create("Country road • packed earth", PrimitiveType.Cube, parent,
                new Vector3(center.x, 0.105f, center.y),
                new Vector3(width, 0.06f, delta.magnitude), _road, false);
            road.transform.rotation = rotation;

            // Shallow wheel ruts and a raised grassy verge add scale and catch warm side-light.
            for (var side = -1; side <= 1; side += 2)
            {
                var rut = Create("Worn cart rut", PrimitiveType.Cube, parent,
                    new Vector3(center.x, 0.139f, center.y),
                    new Vector3(0.16f, 0.012f, delta.magnitude * 0.96f),
                    _trunk, false);
                rut.transform.rotation = rotation;
                rut.transform.localPosition += rotation * Vector3.right * (side * width * 0.23f);
            }
            for (var side = -1; side <= 1; side += 2)
            {
                var verge = Create("Road grassy shoulder", PrimitiveType.Cube, parent,
                    new Vector3(center.x, 0.075f, center.y),
                    new Vector3(0.42f, 0.025f, delta.magnitude * 0.98f),
                    _forest, false);
                verge.transform.rotation = rotation;
                verge.transform.localPosition += rotation * Vector3.right * (side * (width * 0.5f + 0.18f));
            }
        }

        private static Material MakeTexturedMaterial(string name, Color tint, int pattern, float tilingX, float tilingY)
        {
            var material = MakeMaterial(name, tint);
            var texture = MakeSurfaceTexture(name + " • 64px", pattern);
            _generatedAssets.Add(material);
            _generatedAssets.Add(texture);
            material.mainTexture = texture;
            material.mainTextureScale = new Vector2(tilingX, tilingY);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", pattern == 5 ? 0.58f : 0.12f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", pattern == 5 ? 0.08f : 0f);
            return material;
        }

        private static Texture2D MakeSurfaceTexture(string textureName, int pattern)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var random = new System.Random(100 + pattern * 37);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var noise = (float)random.NextDouble() * 0.12f - 0.06f;
                var shade = 0.92f + noise;
                switch (pattern)
                {
                    case 0: // meadow and foliage: irregular flecks
                        if ((x * 17 + y * 29) % 41 == 0) shade -= 0.22f;
                        if ((x + y * 3) % 53 == 0) shade += 0.12f;
                        break;
                    case 1: // packed earth and field soil
                        if ((x * 11 + y * 7) % 37 < 3) shade -= 0.20f;
                        if ((x + y * 3) % 53 == 0) shade += 0.10f;
                        break;
                    case 2: // timber grain and knots
                        if (y % 13 < 2) shade -= 0.20f;
                        if ((x - y * 2 + 64) % 47 < 3) shade -= 0.08f;
                        if ((x % 29 < 4) && (y % 31 < 5)) shade += 0.10f;
                        break;
                    case 3: // overlapping thatch courses
                        if (y % 9 < 2) shade -= 0.24f;
                        if (((x + (y / 9) * 11) % 19) < 2) shade -= 0.12f;
                        break;
                    case 4: // irregular masonry blocks and mortar joints
                        if (y % 16 < 2 || (x + (y / 16) * 9) % 21 < 2) shade -= 0.28f;
                        if ((x * 13 + y * 5) % 67 == 0) shade += 0.10f;
                        break;
                    case 5: // river ripples and broken highlights
                        if ((x * 7 + y * 3) % 29 < 3) shade += 0.18f;
                        if ((x + y * 11) % 47 < 2) shade -= 0.10f;
                        break;
                }
                shade = Mathf.Clamp(shade, 0.45f, 1.08f);
                texture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
            }
            texture.Apply();
            return texture;
        }

        public static void Cleanup()
        {
            for (var i = 0; i < _generatedAssets.Count; i++)
            {
                var asset = _generatedAssets[i];
                if (asset != null) UnityEngine.Object.Destroy(asset);
            }
            _generatedAssets.Clear();
            _ground = null;
            _water = null;
            _timber = null;
            _roof = null;
            _field = null;
            _forest = null;
            _trunk = null;
            _road = null;
            _straw = null;
            _scarecrowCloth = null;
            _grass = null;
            _stone = null;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };
            return material;
        }

        private static GameObject Create(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale, Material material, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            if (!keepCollider)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);
            }
            return go;
        }
    }
}