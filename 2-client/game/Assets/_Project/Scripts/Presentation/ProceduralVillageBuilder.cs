using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Builds a deterministic, disposable prototype village from a stable seed. Geometry and textures are presentation assets,
    /// not authoritative world data; replace them with authored assets as the art direction matures.
    /// </summary>
    public static class ProceduralVillageBuilder
    {
        private static Material _groundMaterial;
        private static Material _pathMaterial;
        private static Material _woodMaterial;
        private static Material _roofMaterial;
        private static Material _stoneMaterial;
        private static Material _leafMaterial;
        private static Material _grassMaterial;
        private static Texture2D _groundTexture;

        public static void Build(Transform root, string worldSeed = "tutorial-preview")
        {
            if (root == null || root.Find("PrototypeVillage") != null) return;

            var village = new GameObject("PrototypeVillage").transform;
            village.SetParent(root, false);

            _groundMaterial = CreateMaterial("Ground • moss", Color.white);
            _groundTexture = MakeGroundTexture();
            _groundMaterial.mainTexture = _groundTexture;
            _groundMaterial.mainTextureScale = new Vector2(40, 40);
            _pathMaterial = CreateMaterial("Path • packed earth", new Color(0.48f, 0.38f, 0.27f));
            _woodMaterial = CreateMaterial("Wood • warm timber", new Color(0.30f, 0.19f, 0.12f));
            _roofMaterial = CreateMaterial("Roof • dark thatch", new Color(0.22f, 0.20f, 0.16f));
            _stoneMaterial = CreateMaterial("Stone", new Color(0.39f, 0.39f, 0.35f));
            _leafMaterial = CreateMaterial("Leaves", new Color(0.20f, 0.34f, 0.19f));
            _grassMaterial = CreateMaterial("Grass", new Color(0.46f, 0.53f, 0.29f));

            var ground = CreatePrimitive("Meadow", PrimitiveType.Plane, village, Vector3.zero,
                new Vector3(12, 1, 12), _groundMaterial);
            ground.isStatic = true;

            CreatePrimitive("Village path", PrimitiveType.Cube, village, new Vector3(0, 0.025f, 0),
                new Vector3(2.1f, 0.05f, 24), _pathMaterial);
            CreatePrimitive("Cross path", PrimitiveType.Cube, village, new Vector3(0, 0.03f, 0),
                new Vector3(22, 0.055f, 1.7f), _pathMaterial);

            BuildHouse(village, "North cottage", new Vector3(-6, 0, 7), 0.2f);
            BuildHouse(village, "East cottage", new Vector3(7, 0, 4), -0.25f);
            BuildHouse(village, "West cottage", new Vector3(-8, 0, -5), 0.35f);
            BuildTavern(village, new Vector3(1.5f, 0, 8f));
            BuildBlacksmith(village, new Vector3(9f, 0, -4.5f));
            BuildMarket(village, new Vector3(-4.5f, 0, -1.5f));
            BuildGarden(village, new Vector3(6f, 0, -9f));
            BuildWell(village, new Vector3(3.2f, 0, -2.4f));
            BuildFence(village, new Vector3(0, 0, -13), 12, 0.5f);

            var random = new System.Random(StableSeed(worldSeed));
            for (var i = 0; i < 24; i++)
            {
                var x = (float)(random.NextDouble() * 30 - 15);
                var z = (float)(random.NextDouble() * 30 - 15);
                if (Mathf.Abs(x) < 3f || Mathf.Abs(z) < 2.5f) continue;
                BuildTree(village, new Vector3(x, 0, z), 0.8f + (float)random.NextDouble() * 0.7f);
            }

            for (var i = 0; i < 34; i++)
            {
                var x = (float)(random.NextDouble() * 34 - 17);
                var z = (float)(random.NextDouble() * 34 - 17);
                if (Mathf.Abs(x) < 2.2f || Mathf.Abs(z) < 1.7f) continue;
                var tuft = CreatePrimitive("Grass tuft", PrimitiveType.Cube, village,
                    new Vector3(x, 0.12f, z), new Vector3(0.08f, 0.24f, 0.08f), _grassMaterial);
                tuft.transform.rotation = Quaternion.Euler(0, (float)random.NextDouble() * 180, 18);
                tuft.isStatic = true;
            }
        }

        private static void BuildHouse(Transform root, string name, Vector3 position, float yaw)
        {
            var house = new GameObject(name).transform;
            house.SetParent(root, false);
            house.position = position;
            house.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);

            CreatePrimitive("Timber walls", PrimitiveType.Cube, house, new Vector3(0, 1.15f, 0),
                new Vector3(3.4f, 2.3f, 2.8f), _woodMaterial);
            var leftRoof = CreatePrimitive("Thatched roof left", PrimitiveType.Cube, house,
                new Vector3(-0.92f, 2.55f, 0), new Vector3(2.25f, 0.24f, 3.25f), _roofMaterial);
            leftRoof.transform.localRotation = Quaternion.Euler(0, 0, -25f);
            var rightRoof = CreatePrimitive("Thatched roof right", PrimitiveType.Cube, house,
                new Vector3(0.92f, 2.55f, 0), new Vector3(2.25f, 0.24f, 3.25f), _roofMaterial);
            rightRoof.transform.localRotation = Quaternion.Euler(0, 0, 25f);
            CreatePrimitive("Door", PrimitiveType.Cube, house, new Vector3(0, 0.72f, -1.415f),
                new Vector3(0.75f, 1.45f, 0.08f), _roofMaterial);
            CreatePrimitive("Window left", PrimitiveType.Cube, house, new Vector3(-1.05f, 1.45f, -1.43f),
                new Vector3(0.48f, 0.55f, 0.08f), _stoneMaterial);
            CreatePrimitive("Window right", PrimitiveType.Cube, house, new Vector3(1.05f, 1.45f, -1.43f),
                new Vector3(0.48f, 0.55f, 0.08f), _stoneMaterial);
            CreatePrimitive("Chimney", PrimitiveType.Cube, house, new Vector3(1.0f, 3.0f, 0.45f),
                new Vector3(0.45f, 1.1f, 0.45f), _stoneMaterial);
        }

        private static void BuildTavern(Transform root, Vector3 position)
        {
            var tavern = new GameObject("Tutorial Tavern").transform;
            tavern.SetParent(root, false);
            tavern.position = position;
            CreatePrimitive("Tavern timber hall", PrimitiveType.Cube, tavern,
                new Vector3(0, 1.35f, 0), new Vector3(5.2f, 2.7f, 4.2f), _woodMaterial);
            var roofA = CreatePrimitive("Tavern roof left", PrimitiveType.Cube, tavern,
                new Vector3(-1.35f, 2.95f, 0), new Vector3(3.1f, 0.3f, 4.8f), _roofMaterial);
            roofA.transform.localRotation = Quaternion.Euler(0, 0, -27f);
            var roofB = CreatePrimitive("Tavern roof right", PrimitiveType.Cube, tavern,
                new Vector3(1.35f, 2.95f, 0), new Vector3(3.1f, 0.3f, 4.8f), _roofMaterial);
            roofB.transform.localRotation = Quaternion.Euler(0, 0, 27f);
            CreatePrimitive("Tavern door", PrimitiveType.Cube, tavern,
                new Vector3(0, 0.85f, -2.13f), new Vector3(0.95f, 1.7f, 0.1f), _roofMaterial);
            CreatePrimitive("Tavern window left", PrimitiveType.Cube, tavern,
                new Vector3(-1.65f, 1.65f, -2.14f), new Vector3(0.75f, 0.75f, 0.1f), _stoneMaterial);
            CreatePrimitive("Tavern window right", PrimitiveType.Cube, tavern,
                new Vector3(1.65f, 1.65f, -2.14f), new Vector3(0.75f, 0.75f, 0.1f), _stoneMaterial);
            CreateSign(tavern, "ТАВЕРНА", new Vector3(0, 2.15f, -2.24f), 0.9f);
            CreatePrimitive("Tavern chimney", PrimitiveType.Cube, tavern,
                new Vector3(1.8f, 3.45f, 0.8f), new Vector3(0.5f, 1.2f, 0.5f), _stoneMaterial);
            CreatePrimitive("Tavern bench", PrimitiveType.Cube, tavern,
                new Vector3(-3.4f, 0.35f, -1f), new Vector3(1.8f, 0.18f, 0.45f), _woodMaterial);
            CreatePoiMarker(root, "Таверна • поговорить", new Vector3(1.5f, 0.08f, 3.2f), new Color(0.75f, 0.48f, 0.22f));
        }

        private static void BuildBlacksmith(Transform root, Vector3 position)
        {
            var smithy = new GameObject("Blacksmith workshop").transform;
            smithy.SetParent(root, false);
            smithy.position = position;
            CreatePrimitive("Smithy walls", PrimitiveType.Cube, smithy,
                new Vector3(0, 1.1f, 0), new Vector3(3.8f, 2.2f, 3f), _woodMaterial);
            CreatePrimitive("Smithy roof", PrimitiveType.Cube, smithy,
                new Vector3(0, 2.35f, 0), new Vector3(4.2f, 0.35f, 3.4f), _roofMaterial);
            CreatePrimitive("Forge", PrimitiveType.Cube, smithy,
                new Vector3(-1.1f, 0.65f, -1.6f), new Vector3(1.0f, 1.3f, 0.8f), _stoneMaterial);
            CreatePrimitive("Anvil", PrimitiveType.Cube, smithy,
                new Vector3(1.0f, 0.9f, -1.7f), new Vector3(0.9f, 0.25f, 0.5f), _stoneMaterial);
            CreateSign(smithy, "КУЗНИЦА", new Vector3(0, 1.8f, -1.58f), 0.75f);
            CreatePoiMarker(root, "Кузница • работать", position + new Vector3(0, 0.08f, -2.8f), new Color(0.55f, 0.55f, 0.58f));
        }

        private static void BuildMarket(Transform root, Vector3 position)
        {
            var market = new GameObject("Village market").transform;
            market.SetParent(root, false);
            market.position = position;
            CreatePrimitive("Market stall", PrimitiveType.Cube, market,
                new Vector3(0, 1.0f, 0), new Vector3(3.2f, 0.16f, 1.4f), _woodMaterial);
            CreatePrimitive("Market canopy", PrimitiveType.Cube, market,
                new Vector3(0, 2.0f, 0), new Vector3(3.6f, 0.15f, 1.8f), _roofMaterial);
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Market post", PrimitiveType.Cube, market,
                    new Vector3(x * 1.45f, 1.0f, 0), new Vector3(0.12f, 2.0f, 0.12f), _woodMaterial);
            CreatePrimitive("Bread basket", PrimitiveType.Sphere, market,
                new Vector3(-0.7f, 1.28f, -0.1f), new Vector3(0.55f, 0.28f, 0.55f), _grassMaterial);
            CreateSign(market, "РЫНОК", new Vector3(0, 1.55f, -0.78f), 0.65f);
            CreatePoiMarker(root, "Рынок • поесть", position + new Vector3(0, 0.08f, -2f), new Color(0.72f, 0.62f, 0.30f));
        }

        private static void BuildGarden(Transform root, Vector3 position)
        {
            var garden = new GameObject("Village garden").transform;
            garden.SetParent(root, false);
            garden.position = position;
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 4; col++)
            {
                var x = col * 0.8f - 1.2f;
                var z = row * 0.9f;
                CreatePrimitive("Garden bed", PrimitiveType.Cube, garden,
                    new Vector3(x, 0.12f, z), new Vector3(0.62f, 0.22f, 0.68f), _pathMaterial);
                CreatePrimitive("Crop", PrimitiveType.Sphere, garden,
                    new Vector3(x, 0.42f, z), new Vector3(0.34f, 0.55f, 0.34f), _leafMaterial);
            }
            CreatePoiMarker(root, "Огород • работать", position + new Vector3(0, 0.08f, -2.4f), new Color(0.32f, 0.58f, 0.28f));
        }

        private static void CreatePoiMarker(Transform root, string title, Vector3 position, Color color)
        {
            var marker = new GameObject("POI • " + title).transform;
            marker.SetParent(root, false);
            marker.position = position;
            var beacon = CreatePrimitive("Marker", PrimitiveType.Cylinder, marker,
                new Vector3(0, 0.12f, 0), new Vector3(0.24f, 0.035f, 0.24f), _stoneMaterial);
            var renderer = beacon.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            var label = new GameObject("POI label").transform;
            label.SetParent(marker, false);
            label.localPosition = new Vector3(0, 0.75f, 0);
            label.AddComponent<BillboardLabel>();
            label.gameObject.AddComponent<BillboardLabel>();
            label.gameObject.AddComponent<BillboardLabel>();
            var text = label.gameObject.AddComponent<TextMesh>();
            text.text = title;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 34;
            text.characterSize = 0.055f;
            text.color = Color.white;
            text.richText = false;
        }

        private static void CreateSign(Transform parent, string title, Vector3 position, float width)
        {
            CreatePrimitive("Sign board", PrimitiveType.Cube, parent, position,
                new Vector3(width * 2.2f, 0.38f, 0.1f), _roofMaterial);
            var label = new GameObject("Sign text").transform;
            label.SetParent(parent, false);
            label.localPosition = position + new Vector3(0, 0, -0.07f);
            label.localRotation = Quaternion.identity;
            var text = label.gameObject.AddComponent<TextMesh>();
            text.text = title;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 40;
            text.characterSize = 0.045f;
            text.color = new Color(1f, 0.88f, 0.62f);
            text.richText = false;
        }

        private static void BuildTree(Transform root, Vector3 position, float scale)
        {
            var tree = new GameObject("Willow tree").transform;
            tree.SetParent(root, false);
            tree.position = position;
            tree.localScale = Vector3.one * scale;
            CreatePrimitive("Trunk", PrimitiveType.Cylinder, tree, new Vector3(0, 1.1f, 0),
                new Vector3(0.22f, 1.1f, 0.22f), _woodMaterial);
            CreatePrimitive("Canopy", PrimitiveType.Sphere, tree, new Vector3(0, 2.65f, 0),
                new Vector3(2.0f, 1.8f, 1.9f), _leafMaterial);
            for (var i = 0; i < 5; i++)
            {
                var angle = i * Mathf.PI * 2f / 5f;
                CreatePrimitive("Hanging foliage", PrimitiveType.Sphere, tree,
                    new Vector3(Mathf.Cos(angle) * 0.72f, 1.75f, Mathf.Sin(angle) * 0.72f),
                    new Vector3(0.45f, 1.25f, 0.45f), _leafMaterial);
            }
        }

        private static void BuildWell(Transform root, Vector3 position)
        {
            var well = new GameObject("Village well").transform;
            well.SetParent(root, false);
            well.position = position;
            CreatePrimitive("Stone ring", PrimitiveType.Cylinder, well, new Vector3(0, 0.42f, 0),
                new Vector3(1.1f, 0.42f, 1.1f), _stoneMaterial);
            CreatePrimitive("Roof support", PrimitiveType.Cube, well, new Vector3(0, 1.25f, 0),
                new Vector3(0.12f, 1.7f, 0.12f), _woodMaterial);
            CreatePrimitive("Well canopy", PrimitiveType.Cube, well, new Vector3(0, 2.1f, 0),
                new Vector3(1.7f, 0.16f, 1.25f), _roofMaterial);
        }

        private static void BuildFence(Transform root, Vector3 position, int count, float spacing)
        {
            var fence = new GameObject("Village fence").transform;
            fence.SetParent(root, false);
            fence.position = position;
            for (var i = 0; i <= count; i++)
            {
                CreatePrimitive("Fence post", PrimitiveType.Cube, fence,
                    new Vector3((i - count / 2f) * spacing, 0.55f, 0),
                    new Vector3(0.13f, 1.1f, 0.13f), _woodMaterial);
            }
            CreatePrimitive("Fence rail", PrimitiveType.Cube, fence, new Vector3(0, 0.78f, 0),
                new Vector3(count * spacing, 0.12f, 0.12f), _woodMaterial);
            CreatePrimitive("Fence rail", PrimitiveType.Cube, fence, new Vector3(0, 0.35f, 0),
                new Vector3(count * spacing, 0.12f, 0.12f), _woodMaterial);
        }

        private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            return go;
        }

        // Stable across machines/runs; unlike string.GetHashCode(), this is not runtime-randomized.
        private static int StableSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                var text = value ?? string.Empty;
                for (var i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };
            return material;
        }

        public static void Cleanup()
        {
            DestroyAsset(_groundMaterial);
            DestroyAsset(_pathMaterial);
            DestroyAsset(_woodMaterial);
            DestroyAsset(_roofMaterial);
            DestroyAsset(_stoneMaterial);
            DestroyAsset(_leafMaterial);
            DestroyAsset(_grassMaterial);
            if (_groundTexture != null) Object.Destroy(_groundTexture);
            _groundMaterial = null;
            _pathMaterial = null;
            _woodMaterial = null;
            _roofMaterial = null;
            _stoneMaterial = null;
            _leafMaterial = null;
            _grassMaterial = null;
            _groundTexture = null;
        }

        private static void DestroyAsset(Object asset)
        {
            if (asset != null) Object.Destroy(asset);
        }

        private static Texture2D MakeGroundTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Generated meadow texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            var random = new System.Random(42);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var noise = (float)random.NextDouble() * 0.10f - 0.05f;
                var fleck = ((x * 17 + y * 31) % 23 == 0) ? 0.08f : 0f;
                texture.SetPixel(x, y, new Color(
                    Mathf.Clamp01(0.34f + noise + fleck),
                    Mathf.Clamp01(0.42f + noise + fleck),
                    Mathf.Clamp01(0.25f + noise), 1f));
            }
            texture.Apply();
            return texture;
        }
    }
}