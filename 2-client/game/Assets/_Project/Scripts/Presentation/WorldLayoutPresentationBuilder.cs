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

        public static void Build(Transform root, WorldLayoutFrame layout)
        {
            if (root == null || layout == null || layout.Version != 2 || root.Find("ServerLayoutV2") != null) return;
            var world = new GameObject("ServerLayoutV2").transform;
            world.SetParent(root, false);
            _ground = MakeMaterial("Layout ground", new Color(0.36f, 0.43f, 0.25f));
            _water = MakeMaterial("Layout river", new Color(0.17f, 0.36f, 0.43f));
            _timber = MakeMaterial("Layout timber", new Color(0.30f, 0.20f, 0.13f));
            _roof = MakeMaterial("Layout thatch", new Color(0.24f, 0.20f, 0.14f));
            _field = MakeMaterial("Layout field", new Color(0.57f, 0.57f, 0.30f));
            _forest = MakeMaterial("Layout woodland", new Color(0.13f, 0.25f, 0.15f));
            _trunk = MakeMaterial("Layout trunks", new Color(0.25f, 0.16f, 0.10f));
            _road = MakeMaterial("Layout country roads", new Color(0.38f, 0.30f, 0.22f));
            _straw = MakeMaterial("Farm straw", new Color(0.69f, 0.53f, 0.24f));
            _scarecrowCloth = MakeMaterial("Scarecrow faded cloth", new Color(0.42f, 0.30f, 0.20f));

            // Water and bridge are placed from the exact server geometry.
            var river = Create("River • server layout", PrimitiveType.Cube, world,
                new Vector3(0f, 0.025f, (float)((layout.River.Y0 + layout.River.Y1) * 0.5)),
                new Vector3((float)(layout.WorldHalf * 2), 0.05f, (float)(layout.River.Y1 - layout.River.Y0)), _water, false);
            var bridgeWidth = (float)(layout.River.BridgeX1 - layout.River.BridgeX0);
            Create("Bridge • server layout", PrimitiveType.Cube, world,
                new Vector3((float)((layout.River.BridgeX0 + layout.River.BridgeX1) * 0.5), 0.12f,
                    (float)((layout.River.Y0 + layout.River.Y1) * 0.5)),
                new Vector3(bridgeWidth, 0.24f, (float)(layout.River.Y1 - layout.River.Y0) + 0.8f), _timber, false);

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
            BuildFarmProps(world);

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
            for (var i = 0; i < 72; i++)
            {
                var x = -145f + (float)random.NextDouble() * 70f;
                var z = -142f + (float)random.NextDouble() * 284f;
                if (Math.Abs(z + 84f) < 9f || Math.Abs(x) < 3f && z > -50f && z < 50f) continue;
                var height = 4f + (float)random.NextDouble() * 5f;
                var trunk = Create("Far woodland trunk", PrimitiveType.Cylinder, world,
                    new Vector3(x, height * 0.28f, z), new Vector3(0.24f, height * 0.28f, 0.24f), _trunk, false);
                var crown = Create("Far woodland crown", PrimitiveType.Sphere, world,
                    new Vector3(x, height * 0.82f, z), new Vector3(2.2f, height * 0.55f, 2.2f), _forest, false);
            }
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

            // A broad two-slope roof reads at village scale and is not just a small stall canopy.
            var slopeWidth = width * 0.56f;
            var roofA = Create("Roof slope west", PrimitiveType.Cube, root,
                new Vector3(-width * 0.25f, wallHeight + 0.45f, 0f),
                new Vector3(slopeWidth, 0.42f, depth + 0.7f), roof, false);
            roofA.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
            var roofB = Create("Roof slope east", PrimitiveType.Cube, root,
                new Vector3(width * 0.25f, wallHeight + 0.45f, 0f),
                new Vector3(slopeWidth, 0.42f, depth + 0.7f), roof, false);
            roofB.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
        }

        private static void AddRoad(Transform parent, Vector2 start, Vector2 end, float width)
        {
            var delta = end - start;
            var center = (start + end) * 0.5f;
            var road = Create("Country road • POI link", PrimitiveType.Cube, parent,
                new Vector3(center.x, 0.105f, center.y),
                new Vector3(width, 0.06f, delta.magnitude), _road, false);
            road.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 0f);
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