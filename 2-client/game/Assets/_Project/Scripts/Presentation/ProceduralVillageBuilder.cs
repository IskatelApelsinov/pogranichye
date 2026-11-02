using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Builds a deterministic, disposable prototype village. Geometry and textures are presentation assets,
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

        public static void Build(Transform root)
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
            BuildWell(village, new Vector3(3.2f, 0, -2.4f));
            BuildFence(village, new Vector3(0, 0, -8), 12, 0.5f);

            var random = new System.Random(1701);
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