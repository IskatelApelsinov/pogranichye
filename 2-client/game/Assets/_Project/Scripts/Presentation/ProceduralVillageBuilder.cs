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
        private static Material _beerMaterial;
        private static Material _foamMaterial;
        private static Material _fireMaterial;
        private static Material _metalMaterial;
        private static Material _clothMaterial;
        private static Material _produceMaterial;
        private static Material _waterMaterial;
        private static Texture2D _groundTexture;
        private static Texture2D _pathTexture;
        private static Texture2D _woodTexture;
        private static Texture2D _roofTexture;
        private static Texture2D _stoneTexture;
        private static Texture2D _waterTexture;
        private static Mesh _creekReedMesh;

        public static void Build(Transform root, string worldSeed = "tutorial-preview")
        {
            if (root == null || root.Find("PrototypeVillage") != null) return;

            var village = new GameObject("PrototypeVillage").transform;
            village.SetParent(root, false);
            var ambientMotion = village.gameObject.AddComponent<VillageAmbientMotion>();

            _groundMaterial = CreateMaterial("Ground • moss", Color.white);
            _groundTexture = MakeGroundTexture();
            _groundMaterial.mainTexture = _groundTexture;
            _groundMaterial.mainTextureScale = new Vector2(40, 40);
            _pathTexture = MakeSurfaceTexture("Packed earth texture", 1);
            _pathMaterial = CreateMaterial("Path • packed earth", new Color(0.48f, 0.38f, 0.27f));
            _pathMaterial.mainTexture = _pathTexture;
            _pathMaterial.mainTextureScale = new Vector2(3, 8);
            _woodTexture = MakeSurfaceTexture("Weathered timber texture", 2);
            _woodMaterial = CreateMaterial("Wood • warm timber", new Color(0.30f, 0.19f, 0.12f));
            _woodMaterial.mainTexture = _woodTexture;
            _woodMaterial.mainTextureScale = new Vector2(2, 2);
            _roofTexture = MakeSurfaceTexture("Thatched roof texture", 3);
            _roofMaterial = CreateMaterial("Roof • dark thatch", new Color(0.22f, 0.20f, 0.16f));
            _roofMaterial.mainTexture = _roofTexture;
            _roofMaterial.mainTextureScale = new Vector2(2, 2);
            _stoneTexture = MakeSurfaceTexture("Rough stone texture", 4);
            _stoneMaterial = CreateMaterial("Stone", new Color(0.39f, 0.39f, 0.35f));
            _stoneMaterial.mainTexture = _stoneTexture;
            _stoneMaterial.mainTextureScale = new Vector2(2, 2);
            _leafMaterial = CreateMaterial("Leaves", new Color(0.20f, 0.34f, 0.19f));
            _grassMaterial = CreateMaterial("Grass", new Color(0.46f, 0.53f, 0.29f));
            _beerMaterial = CreateMaterial("Beer amber", new Color(0.55f, 0.25f, 0.06f));
            _foamMaterial = CreateMaterial("Beer foam", new Color(0.92f, 0.84f, 0.62f));
            _fireMaterial = CreateMaterial("Fire glow", new Color(1f, 0.36f, 0.08f));
            _metalMaterial = CreateMaterial("Forged iron", new Color(0.20f, 0.22f, 0.23f));
            _clothMaterial = CreateMaterial("Market canvas", new Color(0.58f, 0.35f, 0.22f));
            _produceMaterial = CreateMaterial("Harvest produce", new Color(0.66f, 0.43f, 0.18f));
            _waterMaterial = CreateMaterial("Creek water", new Color(0.20f, 0.39f, 0.43f));
            _waterTexture = MakeSurfaceTexture("Creek current texture", 5);
            _waterMaterial.mainTexture = _waterTexture;
            _waterMaterial.mainTextureScale = new Vector2(1.5f, 8f);

            var ground = CreatePrimitive("Meadow", PrimitiveType.Plane, village, Vector3.zero,
                new Vector3(6, 1, 6), _groundMaterial);
            ground.isStatic = true;

            CreatePrimitive("Village path", PrimitiveType.Cube, village, new Vector3(0, 0.025f, 0),
                new Vector3(2.1f, 0.05f, 24), _pathMaterial);
            CreatePrimitive("Cross path", PrimitiveType.Cube, village, new Vector3(0, 0.03f, 0),
                new Vector3(22, 0.055f, 1.7f), _pathMaterial);

            BuildHouse(village, "North cottage", new Vector3(-6, 0, 7), 0.2f);
            BuildHouse(village, "East cottage", new Vector3(7, 0, 4), -0.25f);
            BuildHouse(village, "West cottage", new Vector3(-8, 0, -5), 0.35f);
            BuildTavern(village, new Vector3(1.5f, 0, 2.5f));
            BuildBlacksmith(village, new Vector3(9f, 0, -4.5f));
            BuildMarket(village, new Vector3(-4.5f, 0, -1.5f));
            BuildGarden(village, new Vector3(6f, 0, -9f));
            BuildWell(village, new Vector3(3.2f, 0, -2.4f));
            BuildFence(village, new Vector3(0, 0, -13), 12, 0.5f);
            BuildBarrel(village, new Vector3(4.1f, 0, 7.0f), 0.85f);
            BuildBarrel(village, new Vector3(4.8f, 0, 7.0f), 0.72f);
            BuildWoodPile(village, new Vector3(-8.2f, 0, -2.4f));
            BuildHandCart(village, new Vector3(-6.2f, 0, -7.4f));
            BuildCreekAndFootbridge(village);
            BuildCreekBankVegetation(village);
            BuildDistantTreeLine(village);
            BuildOutskirts(village);
            BuildPracticeStations(village);
            BuildVillageLife(village, ambientMotion);

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
            CreateCameraObstacle(house, "Camera collision • cottage", Vector3.zero, new Vector3(3.5f, 2.7f, 2.9f));

            // Stone plinth lifts the timber frame clear of the damp ground.
            CreatePrimitive("Stone foundation", PrimitiveType.Cube, house, new Vector3(0, 0.20f, 0),
                new Vector3(3.55f, 0.40f, 2.95f), _stoneMaterial);
            CreatePrimitive("Timber walls", PrimitiveType.Cube, house, new Vector3(0, 1.15f, 0),
                new Vector3(3.4f, 2.3f, 2.8f), _woodMaterial);
            // Exposed framing, corner posts and a lower wall rail break up the large flat wall planes.
            for (var x = -1; x <= 1; x += 2)
            {
                CreatePrimitive("Front corner post", PrimitiveType.Cube, house,
                    new Vector3(x * 1.58f, 1.18f, -1.43f), new Vector3(0.18f, 2.25f, 0.16f), _roofMaterial);
                CreatePrimitive("Rear corner post", PrimitiveType.Cube, house,
                    new Vector3(x * 1.58f, 1.18f, 1.43f), new Vector3(0.18f, 2.25f, 0.16f), _roofMaterial);
            }
            CreatePrimitive("Front lower rail", PrimitiveType.Cube, house,
                new Vector3(0, 0.48f, -1.44f), new Vector3(3.2f, 0.13f, 0.13f), _roofMaterial);
            CreatePrimitive("Front upper rail", PrimitiveType.Cube, house,
                new Vector3(0, 2.02f, -1.44f), new Vector3(3.2f, 0.13f, 0.13f), _roofMaterial);
            var leftRoof = CreatePrimitive("Thatched roof left", PrimitiveType.Cube, house,
                new Vector3(-0.92f, 2.55f, 0), new Vector3(2.25f, 0.24f, 3.25f), _roofMaterial);
            leftRoof.transform.localRotation = Quaternion.Euler(0, 0, -25f);
            var rightRoof = CreatePrimitive("Thatched roof right", PrimitiveType.Cube, house,
                new Vector3(0.92f, 2.55f, 0), new Vector3(2.25f, 0.24f, 3.25f), _roofMaterial);
            rightRoof.transform.localRotation = Quaternion.Euler(0, 0, 25f);
            CreatePrimitive("Door", PrimitiveType.Cube, house, new Vector3(0, 0.72f, -1.415f),
                new Vector3(0.75f, 1.45f, 0.08f), _roofMaterial);
            for (var x = -1; x <= 1; x += 2)
            {
                var windowX = x * 1.05f;
                CreatePrimitive("Window frame", PrimitiveType.Cube, house,
                    new Vector3(windowX, 1.45f, -1.49f), new Vector3(0.58f, 0.66f, 0.10f), _roofMaterial);
                CreatePrimitive("Window dark opening", PrimitiveType.Cube, house,
                    new Vector3(windowX, 1.45f, -1.55f), new Vector3(0.40f, 0.48f, 0.035f), _metalMaterial);
                CreatePrimitive("Window crossbar", PrimitiveType.Cube, house,
                    new Vector3(windowX, 1.45f, -1.58f), new Vector3(0.42f, 0.045f, 0.04f), _woodMaterial);
                for (var shutterSide = -1; shutterSide <= 1; shutterSide += 2)
                    CreatePrimitive("Window shutter", PrimitiveType.Cube, house,
                        new Vector3(windowX + shutterSide * 0.34f, 1.45f, -1.50f),
                        new Vector3(0.12f, 0.58f, 0.12f), _woodMaterial);
            }
            CreatePrimitive("Chimney", PrimitiveType.Cube, house, new Vector3(1.0f, 3.0f, 0.45f),
                new Vector3(0.45f, 1.1f, 0.45f), _stoneMaterial);
            CreatePrimitive("Chimney cap", PrimitiveType.Cube, house,
                new Vector3(1.0f, 3.58f, 0.45f), new Vector3(0.62f, 0.12f, 0.62f), _roofMaterial);
            var houseLightObject = new GameObject("Cottage warm interior light");
            houseLightObject.transform.SetParent(house, false);
            houseLightObject.transform.localPosition = new Vector3(0f, 1.75f, -0.45f);
            var houseLight = houseLightObject.AddComponent<Light>();
            houseLight.type = LightType.Point;
            houseLight.range = 4.5f;
            houseLight.intensity = 0.35f;
            houseLight.color = new Color(1f, 0.58f, 0.30f);
            var villageAmbient = root.GetComponent<VillageAmbientMotion>();
            if (villageAmbient != null) villageAmbient.RegisterLight(houseLight, 0.06f, 1.6f);
            CreatePrimitive("Roof ridge", PrimitiveType.Cube, house, new Vector3(0, 3.02f, 0),
                new Vector3(0.18f, 0.18f, 3.35f), _woodMaterial);
            CreatePrimitive("Front timber beam", PrimitiveType.Cube, house, new Vector3(0, 2.05f, -1.44f),
                new Vector3(3.35f, 0.13f, 0.12f), _roofMaterial);
            CreatePrimitive("Door handle", PrimitiveType.Sphere, house, new Vector3(0.23f, 0.72f, -1.48f),
                new Vector3(0.07f, 0.07f, 0.04f), _metalMaterial);
        }

        private static void BuildTavern(Transform root, Vector3 position)
        {
            var tavern = new GameObject("Tutorial Tavern").transform;
            tavern.SetParent(root, false);
            tavern.position = position;
            CreateCameraObstacle(tavern, "Camera collision • back wall", new Vector3(0, 1.35f, 1.95f), new Vector3(5.2f, 2.7f, 0.18f));
            CreateCameraObstacle(tavern, "Camera collision • left wall", new Vector3(-2.52f, 1.35f, 0), new Vector3(0.18f, 2.7f, 4.0f));
            CreateCameraObstacle(tavern, "Camera collision • right wall", new Vector3(2.52f, 1.35f, 0), new Vector3(0.18f, 2.7f, 4.0f));

            // Open-front shell keeps the interior readable from the village square.
            CreatePrimitive("Tavern floor", PrimitiveType.Cube, tavern,
                new Vector3(0, 0.08f, 0), new Vector3(5.0f, 0.16f, 4.0f), _pathMaterial);
            CreatePrimitive("Tavern back wall", PrimitiveType.Cube, tavern,
                new Vector3(0, 1.35f, 1.95f), new Vector3(5.2f, 2.7f, 0.18f), _woodMaterial);
            CreatePrimitive("Tavern left wall", PrimitiveType.Cube, tavern,
                new Vector3(-2.52f, 1.35f, 0), new Vector3(0.18f, 2.7f, 4.0f), _woodMaterial);
            CreatePrimitive("Tavern right wall", PrimitiveType.Cube, tavern,
                new Vector3(2.52f, 1.35f, 0), new Vector3(0.18f, 2.7f, 4.0f), _woodMaterial);
            CreatePrimitive("Tavern front left", PrimitiveType.Cube, tavern,
                new Vector3(-2.0f, 1.15f, -1.95f), new Vector3(1.2f, 2.3f, 0.18f), _woodMaterial);
            CreatePrimitive("Tavern front right", PrimitiveType.Cube, tavern,
                new Vector3(2.0f, 1.15f, -1.95f), new Vector3(1.2f, 2.3f, 0.18f), _woodMaterial);
            CreatePrimitive("Tavern front lintel", PrimitiveType.Cube, tavern,
                new Vector3(0, 2.45f, -1.95f), new Vector3(2.8f, 0.35f, 0.18f), _woodMaterial);

            var roofA = CreatePrimitive("Tavern roof left", PrimitiveType.Cube, tavern,
                new Vector3(-1.35f, 2.95f, 0), new Vector3(3.1f, 0.3f, 4.8f), _roofMaterial);
            roofA.transform.localRotation = Quaternion.Euler(0, 0, -27f);
            var roofB = CreatePrimitive("Tavern roof right", PrimitiveType.Cube, tavern,
                new Vector3(1.35f, 2.95f, 0), new Vector3(3.1f, 0.3f, 4.8f), _roofMaterial);
            roofB.transform.localRotation = Quaternion.Euler(0, 0, 27f);

            // The door is deliberately ajar; the prototype has no collision-based movement yet.
            var door = CreatePrimitive("Tavern open door", PrimitiveType.Cube, tavern,
                new Vector3(-0.53f, 0.9f, -1.92f), new Vector3(0.9f, 1.8f, 0.12f), _roofMaterial);
            door.transform.localRotation = Quaternion.Euler(0, -32f, 0);
            CreatePrimitive("Tavern window left", PrimitiveType.Cube, tavern,
                new Vector3(-1.65f, 1.65f, -2.08f), new Vector3(0.55f, 0.65f, 0.08f), _stoneMaterial);
            CreatePrimitive("Tavern window right", PrimitiveType.Cube, tavern,
                new Vector3(1.65f, 1.65f, -2.08f), new Vector3(0.55f, 0.65f, 0.08f), _stoneMaterial);
            CreateSign(tavern, "ТАВЕРНА", new Vector3(0, 2.15f, -2.12f), 0.9f);
            CreatePrimitive("Tavern chimney", PrimitiveType.Cube, tavern,
                new Vector3(1.8f, 3.45f, 0.8f), new Vector3(0.5f, 1.2f, 0.5f), _stoneMaterial);

            // Exposed timber frame and interior wall rails give the open-front room depth.
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Tavern front frame post", PrimitiveType.Cube, tavern,
                    new Vector3(x * 2.38f, 1.35f, -1.82f), new Vector3(0.18f, 2.65f, 0.18f), _roofMaterial);
            CreatePrimitive("Tavern back crossbeam", PrimitiveType.Cube, tavern,
                new Vector3(0, 2.48f, 1.82f), new Vector3(5.0f, 0.18f, 0.18f), _roofMaterial);
            CreatePrimitive("Tavern back lower rail", PrimitiveType.Cube, tavern,
                new Vector3(0, 0.42f, 1.83f), new Vector3(5.0f, 0.12f, 0.12f), _roofMaterial);

            // Bar counter, casks, tables and stools.
            CreatePrimitive("Bar counter", PrimitiveType.Cube, tavern,
                new Vector3(0, 0.62f, 1.25f), new Vector3(3.5f, 1.15f, 0.72f), _woodMaterial);
            CreatePrimitive("Counter front lower moulding", PrimitiveType.Cube, tavern,
                new Vector3(0, 0.18f, 0.875f), new Vector3(3.42f, 0.12f, 0.08f), _roofMaterial);
            CreatePrimitive("Counter front upper moulding", PrimitiveType.Cube, tavern,
                new Vector3(0, 1.02f, 0.875f), new Vector3(3.42f, 0.10f, 0.08f), _roofMaterial);
            for (var x = -1; x <= 1; x++)
                CreatePrimitive("Counter inset panel", PrimitiveType.Cube, tavern,
                    new Vector3(x * 1.02f, 0.61f, 0.86f), new Vector3(0.88f, 0.62f, 0.045f), _roofMaterial);
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Brass beer tap", PrimitiveType.Cylinder, tavern,
                    new Vector3(x * 0.72f, 1.42f, 1.02f), new Vector3(0.045f, 0.20f, 0.045f), _metalMaterial);
            CreatePrimitive("Bar counter top", PrimitiveType.Cube, tavern,
                new Vector3(0, 1.23f, 1.20f), new Vector3(3.75f, 0.12f, 0.9f), _roofMaterial);
            CreatePrimitive("Back bar shelf", PrimitiveType.Cube, tavern,
                new Vector3(0, 1.95f, 1.78f), new Vector3(3.5f, 0.10f, 0.42f), _woodMaterial);
            for (var i = 0; i < 5; i++)
                CreatePrimitive("Shelf bottle", PrimitiveType.Cylinder, tavern,
                    new Vector3(-1.35f + i * 0.675f, 2.18f, 1.72f), new Vector3(0.09f, 0.18f, 0.09f), _beerMaterial);
            for (var i = 0; i < 3; i++)
            {
                var x = -1.1f + i * 1.1f;
                CreatePrimitive("Beer cask", PrimitiveType.Cylinder, tavern,
                    new Vector3(x, 0.48f, 1.72f), new Vector3(0.32f, 0.48f, 0.32f), _woodMaterial);
                CreateBeerMug(tavern, new Vector3(x, 1.34f, 0.92f));
            }

            BuildTavernTable(tavern, new Vector3(-1.15f, 0, -0.25f));
            BuildTavernTable(tavern, new Vector3(1.25f, 0, -0.15f));
            CreatePrimitive("Fireplace hearth", PrimitiveType.Cube, tavern,
                new Vector3(-2.15f, 0.45f, 1.0f), new Vector3(0.55f, 0.9f, 0.7f), _stoneMaterial);
            CreatePrimitive("Fire glow", PrimitiveType.Sphere, tavern,
                new Vector3(-2.15f, 0.78f, 0.58f), new Vector3(0.22f, 0.30f, 0.12f), _fireMaterial);
            var warmLightObject = new GameObject("Tavern warm light");
            warmLightObject.transform.SetParent(tavern, false);
            warmLightObject.transform.localPosition = new Vector3(0, 2.15f, 0.2f);
            var warmLight = warmLightObject.AddComponent<Light>();
            warmLight.type = LightType.Point;
            warmLight.range = 9f;
            warmLight.intensity = 1.6f;
            warmLight.color = new Color(1f, 0.66f, 0.36f);
            var ambient = root.GetComponent<VillageAmbientMotion>();
            if (ambient != null) ambient.RegisterLight(warmLight, 0.08f, 2.0f);

            CreatePoiMarker(root, "Таверна • поговорить", new Vector3(1.1f, 0.08f, -2.0f),
                new Color(0.75f, 0.48f, 0.22f), "socialize");
        }

        private static void BuildVillageLife(Transform root, VillageAmbientMotion ambient)
        {
            // A readable signpost at the crossroads makes the settlement feel navigable.
            var signpost = new GameObject("Crossroads signpost").transform;
            signpost.SetParent(root, false);
            signpost.position = new Vector3(2.8f, 0, -3.8f);
            CreatePrimitive("Signpost upright", PrimitiveType.Cylinder, signpost,
                new Vector3(0, 1.45f, 0), new Vector3(0.09f, 1.45f, 0.09f), _woodMaterial);
            CreateSign(signpost, "КОРЧМА  ←", new Vector3(-0.58f, 2.25f, -0.07f), 0.52f);
            CreateSign(signpost, "КУЗНЕЦ  →", new Vector3(0.55f, 1.92f, -0.07f), 0.48f);

            // Warm lanterns break up the long empty road and provide a visual path at dusk.
            BuildStreetLantern(root, ambient, new Vector3(-2.1f, 0, -8.4f));
            BuildStreetLantern(root, ambient, new Vector3(2.1f, 0, 8.4f));
            BuildStreetLantern(root, ambient, new Vector3(-9.8f, 0, -1.8f));

            // Everyday objects: a place to sit, draw water, wash clothes and unload goods.
            BuildVillageBench(root, new Vector3(5.4f, 0, -5.4f));
            BuildLaundryLine(root, ambient, new Vector3(-10.0f, 0, 10.0f));
            BuildMarketCrates(root, new Vector3(-6.2f, 0, -2.6f));
            BuildHitchingPost(root, new Vector3(5.0f, 0, 4.2f));
            BuildGardenFence(root, new Vector3(6.0f, 0, -9.0f));
        }

        private static void BuildStreetLantern(Transform root, VillageAmbientMotion ambient, Vector3 position)
        {
            var post = new GameObject("Roadside lantern post").transform;
            post.SetParent(root, false);
            post.position = position;
            CreatePrimitive("Lantern post", PrimitiveType.Cylinder, post,
                new Vector3(0, 1.65f, 0), new Vector3(0.09f, 1.65f, 0.09f), _woodMaterial);
            CreatePrimitive("Lantern arm", PrimitiveType.Cube, post,
                new Vector3(0.28f, 3.05f, 0), new Vector3(0.62f, 0.08f, 0.08f), _woodMaterial);
            CreatePrimitive("Lantern roof", PrimitiveType.Cube, post,
                new Vector3(0.55f, 2.98f, 0), new Vector3(0.48f, 0.10f, 0.42f), _roofMaterial);
            var glow = CreatePrimitive("Lantern warm glass", PrimitiveType.Sphere, post,
                new Vector3(0.55f, 2.72f, 0), new Vector3(0.22f, 0.30f, 0.20f), _fireMaterial);
            var lightObject = new GameObject("Lantern light");
            lightObject.transform.SetParent(post, false);
            lightObject.transform.localPosition = new Vector3(0.55f, 2.72f, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 0.8f;
            light.color = new Color(1f, 0.58f, 0.28f);
            if (ambient != null) ambient.RegisterLight(light, 0.16f, 2.8f);
        }

        private static void BuildVillageBench(Transform root, Vector3 position)
        {
            var bench = new GameObject("Village resting bench").transform;
            bench.SetParent(root, false);
            bench.position = position;
            CreatePrimitive("Bench seat", PrimitiveType.Cube, bench,
                new Vector3(0, 0.52f, 0), new Vector3(1.65f, 0.13f, 0.48f), _woodMaterial);
            CreatePrimitive("Bench back", PrimitiveType.Cube, bench,
                new Vector3(0, 0.88f, 0.20f), new Vector3(1.65f, 0.58f, 0.12f), _roofMaterial);
            for (var x = -1; x <= 1; x += 2)
            {
                CreatePrimitive("Bench leg", PrimitiveType.Cube, bench,
                    new Vector3(x * 0.62f, 0.25f, 0), new Vector3(0.12f, 0.50f, 0.12f), _roofMaterial);
                CreatePrimitive("Bench back support", PrimitiveType.Cube, bench,
                    new Vector3(x * 0.62f, 0.68f, 0.20f), new Vector3(0.10f, 0.82f, 0.10f), _roofMaterial);
            }
        }

        private static void BuildLaundryLine(Transform root, VillageAmbientMotion ambient, Vector3 position)
        {
            var laundry = new GameObject("Laundry line").transform;
            laundry.SetParent(root, false);
            laundry.position = position;
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Laundry line post", PrimitiveType.Cylinder, laundry,
                    new Vector3(x * 1.8f, 1.25f, 0), new Vector3(0.06f, 1.25f, 0.06f), _woodMaterial);
            var rope = CreatePrimitive("Laundry rope", PrimitiveType.Cylinder, laundry,
                new Vector3(0, 2.43f, 0), new Vector3(0.025f, 1.8f, 0.025f), _roofMaterial);
            rope.transform.localRotation = Quaternion.Euler(0, 0, 90);
            for (var i = 0; i < 4; i++)
            {
                var cloth = CreatePrimitive("Hanging linen", PrimitiveType.Cube, laundry,
                    new Vector3(-1.1f + i * 0.72f, 1.92f, 0),
                    new Vector3(0.58f, 0.82f, 0.045f), _clothMaterial);
                cloth.transform.localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? -1f : 1f) * 4f);
                if (ambient != null) ambient.RegisterSway(cloth.transform, 2.5f + i * 0.4f, 1.0f + i * 0.12f);
            }
        }

        private static void BuildMarketCrates(Transform root, Vector3 position)
        {
            var goods = new GameObject("Market delivery goods").transform;
            goods.SetParent(root, false);
            goods.position = position;
            for (var i = 0; i < 3; i++)
            {
                var crate = CreatePrimitive("Open produce crate", PrimitiveType.Cube, goods,
                    new Vector3(i * 0.72f, 0.28f, 0), new Vector3(0.62f, 0.52f, 0.62f), _woodMaterial);
                for (var side = -1; side <= 1; side += 2)
                    CreatePrimitive("Crate slat", PrimitiveType.Cube, crate.transform,
                        new Vector3(0, side * 0.40f, 0), new Vector3(1.05f, 0.08f, 1.05f), _roofMaterial);
                CreatePrimitive("Produce bundle", PrimitiveType.Sphere, goods,
                    new Vector3(i * 0.72f, 0.66f, 0.02f), new Vector3(0.34f, 0.24f, 0.30f), _produceMaterial);
            }
            CreatePrimitive("Grain sack", PrimitiveType.Capsule, goods,
                new Vector3(0.2f, 0.36f, 0.86f), new Vector3(0.34f, 0.38f, 0.34f), _clothMaterial);
            CreatePrimitive("Flour sack", PrimitiveType.Capsule, goods,
                new Vector3(0.92f, 0.36f, 0.86f), new Vector3(0.34f, 0.38f, 0.34f), _foamMaterial);
        }

        private static void BuildHitchingPost(Transform root, Vector3 position)
        {
            var hitch = new GameObject("Tavern hitching rail").transform;
            hitch.SetParent(root, false);
            hitch.position = position;
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Hitching post", PrimitiveType.Cylinder, hitch,
                    new Vector3(x * 0.9f, 0.68f, 0), new Vector3(0.10f, 0.68f, 0.10f), _woodMaterial);
            CreatePrimitive("Hitching rail", PrimitiveType.Cylinder, hitch,
                new Vector3(0, 1.20f, 0), new Vector3(0.08f, 0.9f, 0.08f), _woodMaterial);
            CreatePrimitive("Water trough", PrimitiveType.Cube, hitch,
                new Vector3(0, 0.26f, 0.72f), new Vector3(1.45f, 0.48f, 0.62f), _woodMaterial);
            CreatePrimitive("Trough water", PrimitiveType.Cube, hitch,
                new Vector3(0, 0.51f, 0.72f), new Vector3(1.25f, 0.025f, 0.44f), _waterMaterial);
        }

        private static void BuildGardenFence(Transform root, Vector3 position)
        {
            var fence = new GameObject("Garden low fence").transform;
            fence.SetParent(root, false);
            fence.position = position + new Vector3(0, 0, -0.5f);
            for (var i = 0; i < 5; i++)
            {
                var x = -1.8f + i * 0.9f;
                CreatePrimitive("Garden fence post", PrimitiveType.Cube, fence,
                    new Vector3(x, 0.38f, -0.15f), new Vector3(0.10f, 0.76f, 0.10f), _woodMaterial);
                CreatePrimitive("Garden fence rail", PrimitiveType.Cube, fence,
                    new Vector3(x, 0.42f, -0.15f), new Vector3(0.92f, 0.09f, 0.09f), _roofMaterial);
            }
            for (var i = 0; i < 4; i++)
            {
                var x = -1.35f + i * 0.9f;
                CreatePrimitive("Garden fence post side", PrimitiveType.Cube, fence,
                    new Vector3(-2.0f, 0.38f, x + 0.15f), new Vector3(0.10f, 0.76f, 0.10f), _woodMaterial);
            }
        }

        private static void BuildTavernTable(Transform tavern, Vector3 position)
        {
            var table = new GameObject("Tavern table").transform;
            table.SetParent(tavern, false);
            table.localPosition = position;
            CreatePrimitive("Table top", PrimitiveType.Cylinder, table,
                new Vector3(0, 0.82f, 0), new Vector3(0.72f, 0.10f, 0.72f), _woodMaterial);
            for (var i = 0; i < 3; i++)
            {
                var angle = i * Mathf.PI * 2f / 3f;
                CreatePrimitive("Table leg", PrimitiveType.Cylinder, table,
                    new Vector3(Mathf.Cos(angle) * 0.45f, 0.40f, Mathf.Sin(angle) * 0.45f),
                    new Vector3(0.07f, 0.42f, 0.07f), _roofMaterial);
                CreatePrimitive("Stool", PrimitiveType.Cylinder, table,
                    new Vector3(Mathf.Cos(angle) * 1.0f, 0.35f, Mathf.Sin(angle) * 1.0f),
                    new Vector3(0.28f, 0.08f, 0.28f), _woodMaterial);
            }
            CreateBeerMug(table, new Vector3(0.1f, 0.98f, 0.05f));
        }

        private static void CreateBeerMug(Transform parent, Vector3 position)
        {
            var mug = CreatePrimitive("Wooden beer mug", PrimitiveType.Cylinder, parent,
                position, new Vector3(0.13f, 0.17f, 0.13f), _beerMaterial);
            CreatePrimitive("Beer foam", PrimitiveType.Sphere, mug.transform,
                new Vector3(0, 0.92f, 0), new Vector3(1.0f, 0.18f, 1.0f), _foamMaterial);
            CreatePrimitive("Mug handle", PrimitiveType.Cube, mug.transform,
                new Vector3(0.95f, 0, 0), new Vector3(0.22f, 0.12f, 0.10f), _woodMaterial);
        }

        private static void BuildBlacksmith(Transform root, Vector3 position)
        {
            var smithy = new GameObject("Blacksmith workshop").transform;
            smithy.SetParent(root, false);
            smithy.position = position;
            CreateCameraObstacle(smithy, "Camera collision • smithy", new Vector3(0, 1.1f, 0), new Vector3(3.8f, 2.2f, 3f));
            CreatePrimitive("Smithy walls", PrimitiveType.Cube, smithy,
                new Vector3(0, 1.1f, 0), new Vector3(3.8f, 2.2f, 3f), _woodMaterial);
            CreatePrimitive("Smithy roof", PrimitiveType.Cube, smithy,
                new Vector3(0, 2.35f, 0), new Vector3(4.2f, 0.35f, 3.4f), _roofMaterial);
            CreatePrimitive("Forge", PrimitiveType.Cube, smithy,
                new Vector3(-1.1f, 0.65f, -1.6f), new Vector3(1.0f, 1.3f, 0.8f), _stoneMaterial);
            CreatePrimitive("Anvil", PrimitiveType.Cube, smithy,
                new Vector3(1.0f, 0.9f, -1.7f), new Vector3(0.9f, 0.25f, 0.5f), _metalMaterial);
            CreatePrimitive("Anvil horn", PrimitiveType.Cylinder, smithy,
                new Vector3(1.55f, 0.92f, -1.7f), new Vector3(0.16f, 0.12f, 0.16f), _metalMaterial);
            CreatePrimitive("Forge glow", PrimitiveType.Sphere, smithy,
                new Vector3(-1.1f, 1.02f, -1.45f), new Vector3(0.48f, 0.32f, 0.22f), _fireMaterial);
            CreateSign(smithy, "КУЗНИЦА", new Vector3(0, 1.8f, -1.58f), 0.75f);
            CreatePoiMarker(root, "Кузница • работать", position + new Vector3(0, 0.08f, -2.8f), new Color(0.55f, 0.55f, 0.58f), "work");
        }

        private static void BuildMarket(Transform root, Vector3 position)
        {
            var market = new GameObject("Village market").transform;
            market.SetParent(root, false);
            market.position = position;
            CreatePrimitive("Market stall", PrimitiveType.Cube, market,
                new Vector3(0, 1.0f, 0), new Vector3(3.2f, 0.16f, 1.4f), _woodMaterial);
            CreatePrimitive("Market canopy", PrimitiveType.Cube, market,
                new Vector3(0, 2.0f, 0), new Vector3(3.6f, 0.15f, 1.8f), _clothMaterial);
            CreatePrimitive("Canopy front valance", PrimitiveType.Cube, market,
                new Vector3(0, 1.88f, -0.88f), new Vector3(3.6f, 0.28f, 0.10f), _clothMaterial);
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Market post", PrimitiveType.Cube, market,
                    new Vector3(x * 1.45f, 1.0f, 0), new Vector3(0.12f, 2.0f, 0.12f), _woodMaterial);
            CreatePrimitive("Bread basket", PrimitiveType.Sphere, market,
                new Vector3(-0.7f, 1.28f, -0.1f), new Vector3(0.55f, 0.28f, 0.55f), _grassMaterial);
            for (var i = 0; i < 4; i++)
                CreatePrimitive("Loaf of bread", PrimitiveType.Sphere, market,
                    new Vector3(-0.9f + i * 0.18f, 1.43f, -0.12f), new Vector3(0.20f, 0.13f, 0.13f), _produceMaterial);
            CreateSign(market, "РЫНОК", new Vector3(0, 1.55f, -0.78f), 0.65f);
            CreatePoiMarker(root, "Рынок • поесть", position + new Vector3(0, 0.08f, -2f), new Color(0.72f, 0.62f, 0.30f), "eat");
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
            CreatePoiMarker(root, "Огород • работать", position + new Vector3(0, 0.08f, -2.4f), new Color(0.32f, 0.58f, 0.28f), "work");
        }


        private static void BuildBarrel(Transform root, Vector3 position, float scale)
        {
            var barrel = new GameObject("Oak barrel").transform;
            barrel.SetParent(root, false);
            barrel.position = position;
            barrel.localScale = Vector3.one * scale;
            CreatePrimitive("Barrel body", PrimitiveType.Cylinder, barrel, new Vector3(0, 0.48f, 0),
                new Vector3(0.38f, 0.48f, 0.38f), _woodMaterial);
            for (var y = 0.18f; y <= 0.78f; y += 0.48f)
                CreatePrimitive("Barrel iron hoop", PrimitiveType.Cylinder, barrel, new Vector3(0, y, 0),
                    new Vector3(0.395f, 0.035f, 0.395f), _metalMaterial);
            CreatePrimitive("Barrel lid", PrimitiveType.Cylinder, barrel, new Vector3(0, 0.97f, 0),
                new Vector3(0.36f, 0.035f, 0.36f), _woodMaterial);
        }

        private static void BuildWoodPile(Transform root, Vector3 position)
        {
            var pile = new GameObject("Stacked firewood").transform;
            pile.SetParent(root, false);
            pile.position = position;
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 4; col++)
            {
                var log = CreatePrimitive("Firewood log", PrimitiveType.Cylinder, pile,
                    new Vector3(col * 0.34f - 0.5f, 0.18f + row * 0.28f, 0),
                    new Vector3(0.13f, 0.22f, 0.13f), _woodMaterial);
                log.transform.localRotation = Quaternion.Euler(90, 0, (row % 2) * 18);
            }
        }

        private static void BuildHandCart(Transform root, Vector3 position)
        {
            var cart = new GameObject("Village handcart").transform;
            cart.SetParent(root, false);
            cart.position = position;
            CreatePrimitive("Cart bed", PrimitiveType.Cube, cart, new Vector3(0, 0.58f, 0),
                new Vector3(1.25f, 0.14f, 0.85f), _woodMaterial);
            for (var x = -1; x <= 1; x += 2)
            for (var z = -1; z <= 1; z += 2)
                CreatePrimitive("Cart side", PrimitiveType.Cube, cart, new Vector3(x * 0.58f, 0.95f, z * 0.38f),
                    new Vector3(0.10f, 0.62f, 0.85f), _woodMaterial);
            for (var x = -1; x <= 1; x += 2)
            {
                var wheel = CreatePrimitive("Cart wheel", PrimitiveType.Cylinder, cart,
                    new Vector3(x * 0.72f, 0.42f, 0), new Vector3(0.12f, 0.55f, 0.55f), _woodMaterial);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            CreatePrimitive("Cart handle", PrimitiveType.Cube, cart, new Vector3(0, 0.52f, -0.78f),
                new Vector3(0.10f, 0.10f, 1.0f), _woodMaterial);
        }

        private static void BuildCreekAndFootbridge(Transform root)
        {
            var creek = CreatePrimitive("Narrow village creek", PrimitiveType.Cube, root,
                new Vector3(15.5f, -0.08f, 0), new Vector3(2.2f, 0.12f, 32f), _waterMaterial);
            var waterMotion = creek.AddComponent<CreekWaterDrift>();
            waterMotion.Initialize(_waterMaterial, 0.018f);
            CreatePrimitive("Creek bank near", PrimitiveType.Cube, root,
                new Vector3(14.25f, 0.02f, 0), new Vector3(0.35f, 0.18f, 32f), _pathMaterial);
            CreatePrimitive("Creek bank far", PrimitiveType.Cube, root,
                new Vector3(16.75f, 0.02f, 0), new Vector3(0.35f, 0.18f, 32f), _pathMaterial);
            var bridge = new GameObject("Old plank footbridge").transform;
            bridge.SetParent(root, false);
            bridge.position = new Vector3(15.5f, 0.12f, -2.5f);
            for (var i = -2; i <= 2; i++)
                CreatePrimitive("Bridge plank", PrimitiveType.Cube, bridge,
                    new Vector3(i * 0.36f, 0, 0), new Vector3(0.34f, 0.12f, 2.7f), _woodMaterial);
            for (var x = -1; x <= 1; x += 2)
            {
                CreatePrimitive("Bridge rail post", PrimitiveType.Cube, bridge,
                    new Vector3(x * 0.9f, 0.48f, 0), new Vector3(0.12f, 0.95f, 0.12f), _woodMaterial);
                CreatePrimitive("Bridge handrail", PrimitiveType.Cube, bridge,
                    new Vector3(x * 0.9f, 0.85f, 0), new Vector3(0.12f, 0.12f, 2.7f), _woodMaterial);
            }
        }

        private static void BuildCreekBankVegetation(Transform root)
        {
            var vertices = new System.Collections.Generic.List<Vector3>(900);
            var triangles = new System.Collections.Generic.List<int>(900);
            var random = new System.Random(44017);

            // Reeds follow both banks, leaving the old footbridge approach clear.
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 15; i++)
                {
                    var z = -14.5f + i * 2.05f + (float)random.NextDouble() * 0.55f;
                    if (z > -4.4f && z < 0.4f) continue;
                    var x = 15.5f + side * (1.18f + (float)random.NextDouble() * 0.32f);
                    var clump = new Vector3(x, 0.08f, z);
                    var stalks = 3 + random.Next(3);
                    for (var stalk = 0; stalk < stalks; stalk++)
                    {
                        var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                        var height = 0.55f + (float)random.NextDouble() * 0.72f;
                        var lean = 0.10f + (float)random.NextDouble() * 0.14f;
                        var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        var tangent = new Vector3(-direction.z, 0f, direction.x) * 0.035f;
                        var basePoint = clump + direction * ((float)random.NextDouble() * 0.18f);
                        var tip = basePoint + Vector3.up * height + direction * lean;
                        var first = vertices.Count;
                        vertices.Add(basePoint - tangent);
                        vertices.Add(basePoint + tangent);
                        vertices.Add(tip);
                        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                        triangles.Add(first + 2); triangles.Add(first + 1); triangles.Add(first);
                        // A second crossed blade keeps the clump readable from either bank.
                        var cross = new Vector3(-tangent.z, 0f, tangent.x);
                        first = vertices.Count;
                        vertices.Add(basePoint - cross);
                        vertices.Add(basePoint + cross);
                        vertices.Add(tip + direction * 0.015f);
                        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                        triangles.Add(first + 2); triangles.Add(first + 1); triangles.Add(first);
                    }
                }
            }

            _creekReedMesh = new Mesh { name = "Creek bank reeds • combined mesh" };
            _creekReedMesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _creekReedMesh.SetVertices(vertices);
            _creekReedMesh.SetTriangles(triangles, 0);
            _creekReedMesh.RecalculateNormals();
            _creekReedMesh.RecalculateBounds();

            var reeds = new GameObject("Creek bank reeds");
            reeds.transform.SetParent(root, false);
            reeds.AddComponent<MeshFilter>().sharedMesh = _creekReedMesh;
            var renderer = reeds.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _leafMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void BuildDistantTreeLine(Transform root)
        {
            for (var i = 0; i < 18; i++)
            {
                var x = -20f + i * 2.35f;
                var z = 20f + Mathf.Sin(i * 0.7f) * 2.5f;
                var tree = new GameObject("Distant tree silhouette").transform;
                tree.SetParent(root, false);
                tree.position = new Vector3(x, 0, z);
                tree.localScale = Vector3.one * (1.8f + (i % 4) * 0.25f);
                CreatePrimitive("Silhouette trunk", PrimitiveType.Cylinder, tree,
                    new Vector3(0, 1.1f, 0), new Vector3(0.16f, 1.1f, 0.16f), _woodMaterial);
                CreatePrimitive("Silhouette crown", PrimitiveType.Sphere, tree,
                    new Vector3(0, 2.7f, 0), new Vector3(1.4f, 1.8f, 1.3f), _leafMaterial);
            }
        }


        private static void BuildOutskirts(Transform root)
        {
            // A small farmstead and windmill give the village a readable edge rather than an abrupt end.
            var barn = new GameObject("Outlying farm barn").transform;
            barn.SetParent(root, false);
            barn.position = new Vector3(-20f, 0f, -13f);
            CreatePrimitive("Barn stone footing", PrimitiveType.Cube, barn,
                new Vector3(0, 0.18f, 0), new Vector3(5.2f, 0.36f, 4.2f), _stoneMaterial);
            CreatePrimitive("Barn timber body", PrimitiveType.Cube, barn,
                new Vector3(0, 1.65f, 0), new Vector3(5f, 3f, 4f), _woodMaterial);
            var barnRoofA = CreatePrimitive("Barn roof slope A", PrimitiveType.Cube, barn,
                new Vector3(-1.25f, 3.35f, 0), new Vector3(2.8f, 0.22f, 4.5f), _roofMaterial);
            barnRoofA.transform.localRotation = Quaternion.Euler(0, 0, -27f);
            var barnRoofB = CreatePrimitive("Barn roof slope B", PrimitiveType.Cube, barn,
                new Vector3(1.25f, 3.35f, 0), new Vector3(2.8f, 0.22f, 4.5f), _roofMaterial);
            barnRoofB.transform.localRotation = Quaternion.Euler(0, 0, 27f);
            CreatePrimitive("Barn double door left", PrimitiveType.Cube, barn,
                new Vector3(-0.62f, 1.0f, -2.08f), new Vector3(1.18f, 2f, 0.10f), _roofMaterial);
            CreatePrimitive("Barn double door right", PrimitiveType.Cube, barn,
                new Vector3(0.62f, 1.0f, -2.08f), new Vector3(1.18f, 2f, 0.10f), _roofMaterial);
            BuildFence(root, new Vector3(-20f, 0f, -17f), 8, 0.65f);

            var mill = new GameObject("Old windmill").transform;
            mill.SetParent(root, false);
            mill.position = new Vector3(-22f, 0f, 13f);
            CreatePrimitive("Windmill stone tower", PrimitiveType.Cylinder, mill,
                new Vector3(0, 2.1f, 0), new Vector3(1.35f, 2.1f, 1.35f), _stoneMaterial);
            CreatePrimitive("Windmill wooden cap", PrimitiveType.Cylinder, mill,
                new Vector3(0, 4.45f, 0), new Vector3(1.5f, 1.15f, 1.5f), _roofMaterial);
            var rotor = new GameObject("Windmill turning sails").transform;
            rotor.SetParent(mill, false);
            rotor.localPosition = new Vector3(0, 3.85f, -1.18f);
            CreatePrimitive("Windmill hub", PrimitiveType.Sphere, rotor,
                Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f), _metalMaterial);
            for (var i = 0; i < 4; i++)
            {
                var arm = new GameObject("Windmill sail arm").transform;
                arm.SetParent(rotor, false);
                arm.localRotation = Quaternion.Euler(0, 0, i * 90f);
                CreatePrimitive("Windmill timber spar", PrimitiveType.Cube, arm,
                    new Vector3(0, 1.05f, 0), new Vector3(0.20f, 2.1f, 0.10f), _woodMaterial);
                CreatePrimitive("Windmill sail canvas", PrimitiveType.Cube, arm,
                    new Vector3(0, 1.12f, -0.02f), new Vector3(0.58f, 0.72f, 0.06f), _clothMaterial);
            }
            rotor.gameObject.AddComponent<WindmillRotor>().Initialize(8f);
        }

        private static void BuildPracticeStations(Transform root)
        {
            // Visual training props only. They become usable after movement and action contracts exist.
            var range = new GameObject("Training yard • archery and combat").transform;
            range.SetParent(root, false);
            range.position = new Vector3(-13f, 0, -3.5f);
            CreatePrimitive("Target backboard", PrimitiveType.Cube, range,
                new Vector3(0, 1.35f, 0), new Vector3(1.7f, 1.7f, 0.16f), _woodMaterial);
            var outer = CreatePrimitive("Archery target outer ring", PrimitiveType.Cylinder, range,
                new Vector3(0, 1.45f, -0.12f), new Vector3(0.72f, 0.08f, 0.72f), _foamMaterial);
            outer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var middle = CreatePrimitive("Archery target middle ring", PrimitiveType.Cylinder, range,
                new Vector3(0, 1.45f, -0.19f), new Vector3(0.46f, 0.09f, 0.46f), _fireMaterial);
            middle.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var bullseye = CreatePrimitive("Archery target bullseye", PrimitiveType.Cylinder, range,
                new Vector3(0, 1.45f, -0.25f), new Vector3(0.19f, 0.10f, 0.19f), _produceMaterial);
            bullseye.transform.localRotation = Quaternion.Euler(90, 0, 0);
            CreatePrimitive("Target support left", PrimitiveType.Cube, range,
                new Vector3(-0.62f, 0.55f, 0.05f), new Vector3(0.10f, 1.1f, 0.10f), _woodMaterial);
            CreatePrimitive("Target support right", PrimitiveType.Cube, range,
                new Vector3(0.62f, 0.55f, 0.05f), new Vector3(0.10f, 1.1f, 0.10f), _woodMaterial);
            CreatePrimitive("Practice dummy torso", PrimitiveType.Capsule, range,
                new Vector3(2.3f, 1.05f, 0.2f), new Vector3(0.58f, 0.75f, 0.42f), _produceMaterial);
            CreatePrimitive("Practice dummy head", PrimitiveType.Sphere, range,
                new Vector3(2.3f, 2.0f, 0.2f), new Vector3(0.42f, 0.42f, 0.42f), _woodMaterial);
            CreatePrimitive("Dummy crossbar", PrimitiveType.Cube, range,
                new Vector3(2.3f, 1.42f, 0.2f), new Vector3(1.25f, 0.12f, 0.12f), _woodMaterial);
            CreateSign(range, "УЧЕБНЫЙ ДВОР", new Vector3(1.0f, 2.55f, 0.35f), 0.8f);

            var bench = new GameObject("Crafting practice bench").transform;
            bench.SetParent(root, false);
            bench.position = new Vector3(-1.5f, 0, -10.2f);
            CreatePrimitive("Workbench top", PrimitiveType.Cube, bench,
                new Vector3(0, 0.95f, 0), new Vector3(2.2f, 0.18f, 0.9f), _woodMaterial);
            for (var x = -1; x <= 1; x += 2)
            for (var z = -1; z <= 1; z += 2)
                CreatePrimitive("Workbench leg", PrimitiveType.Cube, bench,
                    new Vector3(x * 0.85f, 0.45f, z * 0.30f), new Vector3(0.12f, 0.9f, 0.12f), _woodMaterial);
            CreatePrimitive("Crafting hammer handle", PrimitiveType.Cylinder, bench,
                new Vector3(-0.45f, 1.18f, 0), new Vector3(0.045f, 0.22f, 0.045f), _woodMaterial);
            CreatePrimitive("Crafting hammer head", PrimitiveType.Cube, bench,
                new Vector3(-0.45f, 1.42f, 0), new Vector3(0.28f, 0.10f, 0.12f), _metalMaterial);
            CreatePrimitive("Crafting cloth", PrimitiveType.Cube, bench,
                new Vector3(0.42f, 1.08f, 0.05f), new Vector3(0.45f, 0.035f, 0.42f), _clothMaterial);
            CreateSign(bench, "МАСТЕРСКАЯ", new Vector3(0, 1.65f, -0.48f), 0.7f);

            var dock = new GameObject("Fishing dock").transform;
            dock.SetParent(root, false);
            dock.position = new Vector3(13.45f, 0.10f, 7.0f);
            for (var i = 0; i < 5; i++)
                CreatePrimitive("Dock plank", PrimitiveType.Cube, dock,
                    new Vector3(i * 0.32f - 0.64f, 0, 0), new Vector3(0.29f, 0.12f, 2.6f), _woodMaterial);
            CreatePrimitive("Dock post near", PrimitiveType.Cylinder, dock,
                new Vector3(-0.78f, -0.2f, -1.1f), new Vector3(0.12f, 0.6f, 0.12f), _woodMaterial);
            CreatePrimitive("Dock post far", PrimitiveType.Cylinder, dock,
                new Vector3(0.78f, -0.2f, -1.1f), new Vector3(0.12f, 0.6f, 0.12f), _woodMaterial);
            CreatePrimitive("Fishing rod", PrimitiveType.Cylinder, dock,
                new Vector3(0.95f, 0.55f, 0.2f), new Vector3(0.035f, 0.95f, 0.035f), _woodMaterial);
            CreateSign(dock, "РЫБНОЕ МЕСТО", new Vector3(0, 1.1f, -1.35f), 0.8f);
        }

        private static void CreatePoiMarker(Transform root, string title, Vector3 position, Color color, string goal)
        {
            var marker = new GameObject("POI • " + title).transform;
            marker.SetParent(root, false);
            marker.position = position;
            var hitArea = marker.gameObject.AddComponent<SphereCollider>();
            hitArea.center = new Vector3(0, 0.45f, 0);
            hitArea.radius = 0.8f;
            var beacon = CreatePrimitive("Marker", PrimitiveType.Cylinder, marker,
                new Vector3(0, 0.12f, 0), new Vector3(0.24f, 0.035f, 0.24f), _stoneMaterial);
            var renderer = beacon.GetComponent<Renderer>();
            var poi = marker.gameObject.AddComponent<WorldPoi>();
            poi.Initialize(title, goal, renderer, color);
            var label = new GameObject("POI label").transform;
            label.SetParent(marker, false);
            label.localPosition = new Vector3(0, 0.75f, 0);
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
            label.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the village approach, not the wall
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
            for (var x = -1; x <= 1; x += 2)
                CreatePrimitive("Well roof support", PrimitiveType.Cube, well,
                    new Vector3(x * 0.72f, 1.25f, 0), new Vector3(0.12f, 1.7f, 0.12f), _woodMaterial);
            CreatePrimitive("Well crossbar", PrimitiveType.Cylinder, well,
                new Vector3(0, 1.82f, 0), new Vector3(0.07f, 0.82f, 0.07f), _woodMaterial);
            CreatePrimitive("Well crank handle", PrimitiveType.Cube, well,
                new Vector3(0.92f, 1.82f, 0), new Vector3(0.12f, 0.12f, 0.62f), _metalMaterial);
            CreatePrimitive("Well rope", PrimitiveType.Cylinder, well,
                new Vector3(0, 1.15f, 0), new Vector3(0.025f, 0.62f, 0.025f), _roofMaterial);
            CreatePrimitive("Well bucket", PrimitiveType.Cylinder, well,
                new Vector3(0, 0.88f, 0), new Vector3(0.22f, 0.22f, 0.22f), _woodMaterial);
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


        private static void CreateCameraObstacle(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var obstacle = new GameObject(name);
            obstacle.layer = 2; // Ignore Raycast: camera collision only, never blocks the interaction probe.
            obstacle.transform.SetParent(parent, false);
            obstacle.transform.localPosition = center;
            var box = obstacle.AddComponent<BoxCollider>();
            box.size = size;
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
            DestroyAsset(_beerMaterial);
            DestroyAsset(_foamMaterial);
            DestroyAsset(_fireMaterial);
            DestroyAsset(_metalMaterial);
            DestroyAsset(_clothMaterial);
            DestroyAsset(_produceMaterial);
            DestroyAsset(_waterMaterial);
            if (_groundTexture != null) Object.Destroy(_groundTexture);
            if (_pathTexture != null) Object.Destroy(_pathTexture);
            if (_woodTexture != null) Object.Destroy(_woodTexture);
            if (_roofTexture != null) Object.Destroy(_roofTexture);
            if (_stoneTexture != null) Object.Destroy(_stoneTexture);
            if (_waterTexture != null) Object.Destroy(_waterTexture);
            DestroyAsset(_creekReedMesh);
            _groundMaterial = null;
            _pathMaterial = null;
            _woodMaterial = null;
            _roofMaterial = null;
            _stoneMaterial = null;
            _leafMaterial = null;
            _grassMaterial = null;
            _beerMaterial = null;
            _foamMaterial = null;
            _fireMaterial = null;
            _metalMaterial = null;
            _clothMaterial = null;
            _produceMaterial = null;
            _waterMaterial = null;
            _groundTexture = null;
            _pathTexture = null;
            _woodTexture = null;
            _roofTexture = null;
            _stoneTexture = null;
            _waterTexture = null;
            _creekReedMesh = null;
        }

        private static void DestroyAsset(Object asset)
        {
            if (asset != null) Object.Destroy(asset);
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
                if (pattern == 1) // packed earth: scattered grit and compacted tracks
                {
                    if ((x * 17 + y * 29) % 41 == 0) shade -= 0.22f;
                    if ((x + y * 3) % 53 == 0) shade += 0.12f;
                }
                else if (pattern == 2) // timber: long grain with occasional knots
                {
                    if (y % 13 < 2) shade -= 0.20f;
                    if ((x - y * 2 + 64) % 47 < 3) shade -= 0.08f;
                    if ((x % 29 < 4) && (y % 31 < 5)) shade += 0.10f;
                }
                else if (pattern == 3) // thatch: overlapping straw courses
                {
                    if (y % 9 < 2) shade -= 0.24f;
                    if (((x + (y / 9) * 11) % 19) < 2) shade -= 0.12f;
                }
                else if (pattern == 4) // stone: irregular mortar joints
                {
                    if (y % 17 < 2 || (x + (y / 17) * 8) % 23 < 2) shade -= 0.26f;
                }
                else if (pattern == 5) // water: broken horizontal highlights and soft current streaks
                {
                    var ripple = (x * 7 + y * 3) % 29;
                    if (ripple < 3) shade += 0.18f;
                    if ((x + y * 11) % 47 < 2) shade -= 0.10f;
                }

                shade = Mathf.Clamp(shade, 0.45f, 1.08f);
                texture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
            }
            texture.Apply();
            return texture;
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