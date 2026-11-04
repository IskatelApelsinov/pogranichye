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
        private static Material _mugWoodMaterial;
        private static Material _mugBandMaterial;
        private static Material _bottleMaterial;
        private static Material _corkMaterial;
        private static Material _labelMaterial;
        private static Material _waterMaterial;
        private static Texture2D _groundTexture;
        private static Texture2D _pathTexture;
        private static Texture2D _woodTexture;
        private static Texture2D _roofTexture;
        private static Texture2D _stoneTexture;
        private static Texture2D _waterTexture;
        private static Mesh _creekReedMesh;
        private static readonly System.Collections.Generic.List<Mesh> _generatedMeshes =
            new System.Collections.Generic.List<Mesh>();

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
            _mugWoodMaterial = CreateMaterial("Tankard • oak staves", new Color(0.38f, 0.22f, 0.12f));
            _mugBandMaterial = CreateMaterial("Tankard • iron hoops", new Color(0.16f, 0.18f, 0.19f));
            _bottleMaterial = CreateMaterial("Bottle • dark green glass", new Color(0.12f, 0.29f, 0.20f));
            _corkMaterial = CreateMaterial("Bottle • cork", new Color(0.48f, 0.31f, 0.16f));
            _labelMaterial = CreateMaterial("Bottle • parchment label", new Color(0.78f, 0.67f, 0.43f));
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
            BuildFarmsteadAndOrchard(village);
            BuildNorthernResidentialLane(village, ambientMotion);
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
                CreateBottle(tavern, new Vector3(-1.35f + i * 0.675f, 2.10f, 1.72f),
                    i % 2 == 0 ? 1f : 0.88f);
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

        private static void CreateBottle(Transform parent, Vector3 position, float scale)
        {
            var bottle = new GameObject("Low-poly green glass bottle").transform;
            bottle.SetParent(parent, false);
            bottle.localPosition = position;
            bottle.localScale = Vector3.one * scale;

            // A faceted lathed silhouette: broad body, sloped shoulder, narrow neck and lip.
            const int sides = 9;
            var profile = new[]
            {
                new Vector2(0.000f, 0.000f),
                new Vector2(0.070f, 0.000f),
                new Vector2(0.084f, 0.035f),
                new Vector2(0.084f, 0.175f),
                new Vector2(0.074f, 0.205f),
                new Vector2(0.039f, 0.250f),
                new Vector2(0.031f, 0.270f),
                new Vector2(0.031f, 0.345f),
                new Vector2(0.041f, 0.352f),
                new Vector2(0.041f, 0.370f),
                new Vector2(0.026f, 0.370f),
                new Vector2(0.000f, 0.370f)
            };
            var vertices = new Vector3[profile.Length * sides];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(profile.Length - 1) * sides * 6];
            for (var ring = 0; ring < profile.Length; ring++)
            for (var side = 0; side < sides; side++)
            {
                var angle = side * Mathf.PI * 2f / sides;
                var index = ring * sides + side;
                vertices[index] = new Vector3(Mathf.Cos(angle) * profile[ring].x,
                    profile[ring].y, Mathf.Sin(angle) * profile[ring].x);
                uvs[index] = new Vector2((float)side / sides, profile[ring].y / 0.37f);
            }

            var triangle = 0;
            for (var ring = 0; ring < profile.Length - 1; ring++)
            for (var side = 0; side < sides; side++)
            {
                var a = ring * sides + side;
                var b = ring * sides + (side + 1) % sides;
                var c = (ring + 1) * sides + (side + 1) % sides;
                var d = (ring + 1) * sides + side;
                triangles[triangle++] = a; triangles[triangle++] = d; triangles[triangle++] = b;
                triangles[triangle++] = b; triangles[triangle++] = d; triangles[triangle++] = c;
            }

            var mesh = new Mesh { name = "Nine-sided medieval bottle" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            CreateMeshPart("Bottle glass body", bottle, mesh, _bottleMaterial);

            var label = CreatePrimitive("Parchment bottle label", PrimitiveType.Cylinder, bottle,
                new Vector3(0f, 0.125f, 0f), new Vector3(0.085f, 0.006f, 0.085f), _labelMaterial);
            label.transform.localRotation = Quaternion.identity;
            CreatePrimitive("Bottle cork", PrimitiveType.Cylinder, bottle,
                new Vector3(0f, 0.378f, 0f), new Vector3(0.023f, 0.018f, 0.023f), _corkMaterial);
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
            // A small hollow, tapered low-poly tankard: open rim, visible inner wall,
            // stave-like facets, two iron hoops and a real curved handle.
            var mug = new GameObject("Oak beer tankard").transform;
            mug.SetParent(parent, false);
            mug.localPosition = position;
            mug.localScale = Vector3.one;

            var body = CreateMeshPart("Hollow oak tankard", mug, BuildTankardBodyMesh(), _mugWoodMaterial);
            body.transform.localPosition = Vector3.zero;

            for (var y = 0.055f; y <= 0.255f; y += 0.20f)
            {
                var hoop = CreateMeshPart("Forged iron tankard hoop", mug,
                    BuildTankardHoopMesh(0.127f - (0.255f - y) * 0.012f, 0.018f, 10), _mugBandMaterial);
                hoop.transform.localPosition = new Vector3(0f, y, 0f);
            }

            var handle = CreateMeshPart("Curved wooden tankard handle", mug,
                BuildTankardHandleMesh(), _mugWoodMaterial);
            // The handle mesh is authored in tankard-local coordinates and already
            // reaches into the body wall; an extra X offset would leave a visible gap.
            handle.transform.localPosition = new Vector3(0f, 0.17f, 0f);

            CreatePrimitive("Ale foam", PrimitiveType.Sphere, mug,
                new Vector3(0f, 0.305f, 0f), new Vector3(0.105f, 0.025f, 0.105f), _foamMaterial);
            for (var i = 0; i < 3; i++)
                CreatePrimitive("Foam bubble", PrimitiveType.Sphere, mug,
                    new Vector3(-0.035f + i * 0.032f, 0.318f + (i % 2) * 0.004f, -0.012f),
                    Vector3.one * 0.012f, _foamMaterial);
        }

        private static GameObject CreateMeshPart(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (mesh != null && !_generatedMeshes.Contains(mesh)) _generatedMeshes.Add(mesh);
            return go;
        }

        private static Mesh BuildTankardBodyMesh()
        {
            const int sides = 10;
            // Four radial rings form the outside, rim and inside. A centre vertex
            // closes the floor so the tankard reads as a solid vessel from above.
            var vertices = new Vector3[sides * 4 + 1];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[sides * 6 * 3 + sides * 3];
            var rings = new[] { new Vector2(0.092f, 0.012f), new Vector2(0.112f, 0.285f),
                new Vector2(0.087f, 0.285f), new Vector2(0.071f, 0.025f) };
            for (var ring = 0; ring < 4; ring++)
            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                var index = ring * sides + i;
                vertices[index] = new Vector3(Mathf.Cos(angle) * rings[ring].x,
                    rings[ring].y, Mathf.Sin(angle) * rings[ring].x);
                uvs[index] = new Vector2((float)i / sides, rings[ring].y / 0.3f);
            }

            var t = 0;
            for (var ring = 0; ring < 3; ring++)
            for (var i = 0; i < sides; i++)
            {
                var a = ring * sides + i;
                var b = ring * sides + (i + 1) % sides;
                var c = (ring + 1) * sides + (i + 1) % sides;
                var d = (ring + 1) * sides + i;
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }

            var floorCenter = sides * 4;
            vertices[floorCenter] = new Vector3(0f, rings[0].y, 0f);
            uvs[floorCenter] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < sides; i++)
            {
                // Downward-facing floor fan; the inner wall remains open above it.
                triangles[t++] = floorCenter;
                triangles[t++] = i;
                triangles[t++] = (i + 1) % sides;
            }

            var mesh = new Mesh { name = "Low-poly hollow oak tankard" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildTankardHoopMesh(float radius, float thickness, int sides)
        {
            var vertices = new Vector3[sides * 2];
            var triangles = new int[sides * 6];
            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                var x = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                vertices[i] = new Vector3(x, -thickness * 0.5f, z);
                vertices[sides + i] = new Vector3(x, thickness * 0.5f, z);
            }
            var t = 0;
            for (var i = 0; i < sides; i++)
            {
                var a = i;
                var b = (i + 1) % sides;
                var c = sides + (i + 1) % sides;
                var d = sides + i;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = d;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
            var mesh = new Mesh { name = "Tankard iron hoop" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildTankardHandleMesh()
        {
            const int pathSteps = 10;
            const int sides = 6;
            var vertices = new Vector3[(pathSteps + 1) * sides];
            var triangles = new int[pathSteps * sides * 6];
            for (var p = 0; p <= pathSteps; p++)
            {
                var angle = Mathf.Lerp(-Mathf.PI * 0.43f, Mathf.PI * 0.43f, (float)p / pathSteps);
                var center = new Vector3(0.055f + Mathf.Cos(angle) * 0.09f,
                    Mathf.Sin(angle) * 0.105f, 0f);
                var tangent = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f).normalized;
                var normal = new Vector3(tangent.y, -tangent.x, 0f);
                for (var j = 0; j < sides; j++)
                {
                    var around = j * Mathf.PI * 2f / sides;
                    vertices[p * sides + j] = center + normal * (Mathf.Cos(around) * 0.018f)
                        + Vector3.forward * (Mathf.Sin(around) * 0.018f);
                }
            }
            var t = 0;
            for (var p = 0; p < pathSteps; p++)
            for (var j = 0; j < sides; j++)
            {
                var a = p * sides + j;
                var b = p * sides + (j + 1) % sides;
                var c = (p + 1) * sides + (j + 1) % sides;
                var d = (p + 1) * sides + j;
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }
            var mesh = new Mesh { name = "Curved low-poly tankard handle" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
            // Shared low-poly baskets and produce make the stall read as a working market,
            // while reusing one bread mesh keeps the generated asset count deterministic.
            BuildMarketBasket(market, "Bread basket", new Vector3(-0.78f, 1.12f, -0.12f), 0.48f, 0.38f);
            var breadMesh = BuildBreadLoafMesh();
            for (var i = 0; i < 4; i++)
            {
                var loaf = CreateMeshPart("Low-poly market bread", market, breadMesh, _produceMaterial);
                loaf.transform.localPosition = new Vector3(-1.02f + i * 0.19f, 1.43f, -0.12f);
                loaf.transform.localScale = new Vector3(1f, 0.78f, 0.72f);
                loaf.transform.localRotation = Quaternion.Euler(0f, (i - 1.5f) * 9f, 0f);
            }

            BuildMarketBasket(market, "Vegetable basket • roots", new Vector3(0.48f, 1.12f, -0.12f), 0.42f, 0.34f);
            for (var i = 0; i < 4; i++)
            {
                var angle = i * 90f * Mathf.Deg2Rad;
                var carrot = CreatePrimitive("Market carrot", PrimitiveType.Cylinder, market,
                    new Vector3(0.48f + Mathf.Cos(angle) * 0.16f, 1.39f, -0.12f + Mathf.Sin(angle) * 0.14f),
                    new Vector3(0.075f, 0.22f, 0.075f), _produceMaterial);
                carrot.transform.localRotation = Quaternion.Euler(0f, 0f, 18f + i * 7f);
                CreatePrimitive("Carrot greens", PrimitiveType.Cube, market,
                    carrot.transform.localPosition + new Vector3(0f, 0.22f, 0f),
                    new Vector3(0.12f, 0.035f, 0.12f), _leafMaterial);
            }

            BuildMarketBasket(market, "Vegetable basket • cabbages", new Vector3(1.12f, 1.12f, -0.12f), 0.38f, 0.32f);
            for (var i = 0; i < 3; i++)
            {
                var cabbage = CreatePrimitive("Market cabbage", PrimitiveType.Sphere, market,
                    new Vector3(0.94f + i * 0.18f, 1.40f, -0.12f + (i % 2) * 0.10f),
                    new Vector3(0.22f, 0.18f, 0.22f), _leafMaterial);
                cabbage.transform.localRotation = Quaternion.Euler(0f, i * 37f, 18f);
            }

            // Extra stock sits beside the stall, leaving the counter readable and walkable.
            BuildMarketCrate(market, "Apple crate", new Vector3(1.88f, 0.04f, 0.42f), new Color(0.58f, 0.20f, 0.12f));
            BuildMarketCrate(market, "Root vegetable crate", new Vector3(-1.88f, 0.04f, 0.42f), new Color(0.72f, 0.43f, 0.16f));
            CreateSign(market, "РЫНОК", new Vector3(0, 1.55f, -0.78f), 0.65f);
            CreatePoiMarker(root, "Рынок • поесть", position + new Vector3(0, 0.08f, -2f), new Color(0.72f, 0.62f, 0.30f), "eat");
        }

        private static void BuildMarketCrate(Transform parent, string name, Vector3 position, Color produceTint)
        {
            var crate = new GameObject(name).transform;
            crate.SetParent(parent, false);
            crate.localPosition = position;

            const float width = 0.62f;
            const float depth = 0.52f;
            CreatePrimitive("Crate floor", PrimitiveType.Cube, crate,
                new Vector3(0f, 0.12f, 0f), new Vector3(width, 0.08f, depth), _woodMaterial);
            for (var side = -1; side <= 1; side += 2)
            {
                CreatePrimitive("Crate long side", PrimitiveType.Cube, crate,
                    new Vector3(0f, 0.27f, side * depth * 0.46f), new Vector3(width, 0.25f, 0.055f), _roofMaterial);
                CreatePrimitive("Crate end", PrimitiveType.Cube, crate,
                    new Vector3(side * width * 0.46f, 0.27f, 0f), new Vector3(0.055f, 0.25f, depth), _woodMaterial);
            }
            for (var i = 0; i < 3; i++)
            {
                CreatePrimitive("Crate front slat", PrimitiveType.Cube, crate,
                    new Vector3(0f, 0.17f + i * 0.09f, -depth * 0.50f),
                    new Vector3(width * 0.88f, 0.035f, 0.025f), _woodMaterial);
            }

            var fruitMaterial = new Material(_produceMaterial);
            fruitMaterial.color = produceTint;
            _generatedMaterials.Add(fruitMaterial);
            for (var i = 0; i < 5; i++)
            {
                var x = -0.20f + (i % 3) * 0.20f;
                var z = -0.12f + (i / 3) * 0.20f;
                var fruit = CreatePrimitive(name == "Apple crate" ? "Market apple" : "Market root vegetable",
                    PrimitiveType.Sphere, crate, new Vector3(x, 0.34f + (i % 2) * 0.035f, z),
                    name == "Apple crate" ? new Vector3(0.15f, 0.14f, 0.15f) : new Vector3(0.17f, 0.12f, 0.13f),
                    fruitMaterial);
                fruit.transform.localRotation = Quaternion.Euler(i * 17f, i * 31f, i * 11f);
            }
        }

        private static void BuildMarketBasket(Transform parent, string name, Vector3 position, float width, float depth)
        {
            var basket = new GameObject(name).transform;
            basket.SetParent(parent, false);
            basket.localPosition = position;

            // Low, open wicker tub: a dark inner base, faceted side staves and a raised rim.
            CreatePrimitive("Basket inner base", PrimitiveType.Cylinder, basket,
                new Vector3(0f, 0.08f, 0f), new Vector3(width * 0.78f, 0.045f, depth * 0.78f), _roofMaterial);
            CreatePrimitive("Basket bottom", PrimitiveType.Cylinder, basket,
                new Vector3(0f, 0.025f, 0f), new Vector3(width * 0.82f, 0.035f, depth * 0.82f), _woodMaterial);
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Mathf.PI * 2f / 12f;
                var stave = CreatePrimitive("Woven basket stave", PrimitiveType.Cube, basket,
                    new Vector3(Mathf.Cos(angle) * width * 0.40f, 0.16f, Mathf.Sin(angle) * depth * 0.40f),
                    new Vector3(0.045f, 0.25f, 0.045f), i % 2 == 0 ? _woodMaterial : _roofMaterial);
                stave.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
            }
            CreatePrimitive("Basket rim", PrimitiveType.Cylinder, basket,
                new Vector3(0f, 0.285f, 0f), new Vector3(width * 0.92f, 0.035f, depth * 0.92f), _woodMaterial);
            CreatePrimitive("Basket lower binding", PrimitiveType.Cylinder, basket,
                new Vector3(0f, 0.075f, 0f), new Vector3(width * 0.84f, 0.018f, depth * 0.84f), _roofMaterial);
        }

        private static Mesh BuildBreadLoafMesh()
        {
            // A compact, faceted oval loaf with a domed crown and tapered ends.
            const int sides = 8;
            var profile = new[]
            {
                new Vector2(-0.17f, 0.015f), new Vector2(-0.14f, 0.075f),
                new Vector2(-0.07f, 0.115f), new Vector2(0.07f, 0.115f),
                new Vector2(0.14f, 0.075f), new Vector2(0.17f, 0.015f)
            };
            var vertices = new Vector3[profile.Length * sides + 2];
            var triangles = new int[(profile.Length - 1) * sides * 6 + sides * 6];
            for (var ring = 0; ring < profile.Length; ring++)
            for (var side = 0; side < sides; side++)
            {
                var angle = side * Mathf.PI * 2f / sides;
                vertices[ring * sides + side] = new Vector3(profile[ring].x,
                    Mathf.Cos(angle) * profile[ring].y + 0.075f,
                    Mathf.Sin(angle) * profile[ring].y);
            }

            var t = 0;
            for (var ring = 0; ring < profile.Length - 1; ring++)
            for (var side = 0; side < sides; side++)
            {
                var a = ring * sides + side;
                var b = ring * sides + (side + 1) % sides;
                var c = (ring + 1) * sides + (side + 1) % sides;
                var d = (ring + 1) * sides + side;
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }

            var leftCap = profile.Length * sides;
            var rightCap = leftCap + 1;
            vertices[leftCap] = new Vector3(profile[0].x, 0.075f, 0f);
            vertices[rightCap] = new Vector3(profile[profile.Length - 1].x, 0.075f, 0f);
            for (var side = 0; side < sides; side++)
            {
                triangles[t++] = leftCap;
                triangles[t++] = (side + 1) % sides;
                triangles[t++] = side;
                var last = (profile.Length - 1) * sides;
                triangles[t++] = rightCap;
                triangles[t++] = last + side;
                triangles[t++] = last + (side + 1) % sides;
            }

            var mesh = new Mesh { name = "Eight-sided artisan bread loaf" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

            CreateMeshPart("Bulging faceted oak barrel", barrel, BuildBarrelBodyMesh(), _woodMaterial);
            foreach (var y in new[] { 0.16f, 0.48f, 0.80f })
            {
                var hoop = CreateMeshPart("Forged iron barrel hoop", barrel,
                    BuildBarrelHoopMesh(y), _metalMaterial);
                hoop.transform.localPosition = Vector3.zero;
            }
            CreatePrimitive("Barrel lid", PrimitiveType.Cylinder, barrel,
                new Vector3(0, 0.965f, 0), new Vector3(0.33f, 0.025f, 0.33f), _woodMaterial);
            for (var stave = 0; stave < 8; stave++)
            {
                var angle = stave * 45f;
                var seam = CreatePrimitive("Barrel stave seam", PrimitiveType.Cube, barrel,
                    new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * 0.385f, 0.49f,
                        Mathf.Cos(angle * Mathf.Deg2Rad) * 0.385f),
                    new Vector3(0.012f, 0.78f, 0.025f), _roofMaterial);
                seam.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            }
        }

        private static Mesh BuildBarrelBodyMesh()
        {
            const int sides = 10;
            var profile = new[] { new Vector2(0.31f, 0.02f), new Vector2(0.37f, 0.16f),
                new Vector2(0.405f, 0.48f), new Vector2(0.37f, 0.80f), new Vector2(0.31f, 0.94f) };
            var vertices = new Vector3[profile.Length * sides + 2];
            var triangles = new int[(profile.Length - 1) * sides * 6 + sides * 6];
            for (var ring = 0; ring < profile.Length; ring++)
            for (var side = 0; side < sides; side++)
            {
                var angle = side * Mathf.PI * 2f / sides;
                vertices[ring * sides + side] = new Vector3(
                    Mathf.Cos(angle) * profile[ring].x, profile[ring].y,
                    Mathf.Sin(angle) * profile[ring].x);
            }

            var t = 0;
            for (var ring = 0; ring < profile.Length - 1; ring++)
            for (var side = 0; side < sides; side++)
            {
                var a = ring * sides + side;
                var b = ring * sides + (side + 1) % sides;
                var c = (ring + 1) * sides + (side + 1) % sides;
                var d = (ring + 1) * sides + side;
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }

            var bottomCenter = profile.Length * sides;
            var topCenter = bottomCenter + 1;
            vertices[bottomCenter] = Vector3.up * profile[0].y;
            vertices[topCenter] = Vector3.up * profile[profile.Length - 1].y;
            for (var side = 0; side < sides; side++)
            {
                triangles[t++] = bottomCenter;
                triangles[t++] = (side + 1) % sides;
                triangles[t++] = side;
                var top = (profile.Length - 1) * sides;
                triangles[t++] = topCenter;
                triangles[t++] = top + side;
                triangles[t++] = top + (side + 1) % sides;
            }

            var mesh = new Mesh { name = "Ten-sided bulging oak barrel" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildBarrelHoopMesh(float y)
        {
            const int sides = 10;
            const int crossSides = 4;
            const float majorRadius = 0.397f;
            const float minorRadius = 0.018f;
            var vertices = new Vector3[sides * crossSides];
            var triangles = new int[sides * crossSides * 6];
            for (var i = 0; i < sides; i++)
            for (var j = 0; j < crossSides; j++)
            {
                var around = i * Mathf.PI * 2f / sides;
                var tube = j * Mathf.PI * 2f / crossSides;
                var radius = majorRadius + Mathf.Cos(tube) * minorRadius;
                vertices[i * crossSides + j] = new Vector3(
                    Mathf.Cos(around) * radius, y + Mathf.Sin(tube) * minorRadius,
                    Mathf.Sin(around) * radius);
            }

            var t = 0;
            for (var i = 0; i < sides; i++)
            for (var j = 0; j < crossSides; j++)
            {
                var a = i * crossSides + j;
                var b = ((i + 1) % sides) * crossSides + j;
                var c = ((i + 1) % sides) * crossSides + (j + 1) % crossSides;
                var d = i * crossSides + (j + 1) % crossSides;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = d;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }

            var mesh = new Mesh { name = "Faceted iron barrel hoop" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

        private static void BuildFarmsteadAndOrchard(Transform root)
        {
            // A small cultivated field extends the village economy beyond the central garden.
            // All props are visual-only and deliberately use the existing ground plane.
            var field = new GameObject("Outlying crop field").transform;
            field.SetParent(root, false);
            field.position = new Vector3(12.5f, 0f, -14.5f);

            CreatePrimitive("Field soil patch", PrimitiveType.Cube, field,
                new Vector3(0f, 0.035f, 0f), new Vector3(7.8f, 0.07f, 5.6f), _pathMaterial);
            for (var row = 0; row < 5; row++)
            {
                var z = -2.0f + row * 1.0f;
                CreatePrimitive("Raised crop ridge", PrimitiveType.Cube, field,
                    new Vector3(0f, 0.13f, z), new Vector3(6.8f, 0.16f, 0.52f), _groundMaterial);
                for (var plant = 0; plant < 9; plant++)
                {
                    var x = -3.0f + plant * 0.75f;
                    var height = 0.38f + ((row + plant) % 3) * 0.08f;
                    var crop = CreatePrimitive("Field crop", PrimitiveType.Sphere, field,
                        new Vector3(x, 0.30f + height * 0.42f, z),
                        new Vector3(0.25f, height, 0.25f), _leafMaterial);
                    crop.transform.localRotation = Quaternion.Euler(0f, (row * 31 + plant * 17) % 360, 0f);
                    if ((row + plant) % 4 == 0)
                        CreatePrimitive("Ripened crop head", PrimitiveType.Sphere, field,
                            new Vector3(x + 0.05f, 0.55f + height * 0.55f, z),
                            new Vector3(0.16f, 0.12f, 0.16f), _produceMaterial);
                }
            }

            // A simple scarecrow gives the field a human trace without adding a fake NPC.
            var scarecrow = new GameObject("Field scarecrow").transform;
            scarecrow.SetParent(field, false);
            scarecrow.localPosition = new Vector3(3.45f, 0f, 1.95f);
            CreatePrimitive("Scarecrow post", PrimitiveType.Cylinder, scarecrow,
                new Vector3(0f, 1.25f, 0f), new Vector3(0.07f, 1.25f, 0.07f), _woodMaterial);
            CreatePrimitive("Scarecrow arms", PrimitiveType.Cube, scarecrow,
                new Vector3(0f, 1.72f, 0f), new Vector3(1.15f, 0.09f, 0.09f), _woodMaterial);
            CreatePrimitive("Scarecrow tunic", PrimitiveType.Cube, scarecrow,
                new Vector3(0f, 1.28f, 0f), new Vector3(0.48f, 0.62f, 0.20f), _clothMaterial);
            CreatePrimitive("Scarecrow head", PrimitiveType.Sphere, scarecrow,
                new Vector3(0f, 2.02f, 0f), new Vector3(0.34f, 0.34f, 0.30f), _produceMaterial);
            CreatePrimitive("Scarecrow hat brim", PrimitiveType.Cylinder, scarecrow,
                new Vector3(0f, 2.22f, 0f), new Vector3(0.38f, 0.045f, 0.38f), _roofMaterial);

            // Harvest storage: straw bales and a small covered seed rack.
            var hay = new GameObject("Harvest hay stack").transform;
            hay.SetParent(root, false);
            hay.position = new Vector3(8.0f, 0f, -17.5f);
            for (var layer = 0; layer < 2; layer++)
            for (var bale = 0; bale < 3 - layer; bale++)
            {
                var block = CreatePrimitive("Straw bale", PrimitiveType.Cube, hay,
                    new Vector3((bale - 1) * 0.72f + layer * 0.34f, 0.38f + layer * 0.72f, 0f),
                    new Vector3(0.68f, 0.68f, 0.62f), _produceMaterial);
                block.transform.localRotation = Quaternion.Euler(0f, (bale % 2) * 4f, 0f);
            }
            CreatePrimitive("Seed rack shelf", PrimitiveType.Cube, hay,
                new Vector3(1.55f, 0.92f, 0.15f), new Vector3(1.0f, 0.10f, 0.55f), _woodMaterial);
            for (var i = 0; i < 3; i++)
                CreatePrimitive("Seed sack", PrimitiveType.Capsule, hay,
                    new Vector3(1.25f + i * 0.30f, 1.22f, 0.15f),
                    new Vector3(0.22f, 0.24f, 0.22f), _clothMaterial);

            // A modest fruit orchard on the outer edge, with irregular spacing and low-poly crowns.
            var orchard = new GameObject("Village fruit orchard").transform;
            orchard.SetParent(root, false);
            orchard.position = new Vector3(14.0f, 0f, 14.0f);
            var orchardPositions = new[]
            {
                new Vector3(-2.0f, 0f, -1.5f), new Vector3(0.2f, 0f, -1.9f),
                new Vector3(2.2f, 0f, -1.1f), new Vector3(-1.3f, 0f, 1.0f),
                new Vector3(1.1f, 0f, 1.4f)
            };
            for (var i = 0; i < orchardPositions.Length; i++)
            {
                var tree = new GameObject("Fruit tree").transform;
                tree.SetParent(orchard, false);
                tree.localPosition = orchardPositions[i];
                var scale = 0.88f + (i % 3) * 0.10f;
                tree.localScale = Vector3.one * scale;
                CreatePrimitive("Orchard trunk", PrimitiveType.Cylinder, tree,
                    new Vector3(0f, 1.0f, 0f), new Vector3(0.16f, 1.0f, 0.16f), _woodMaterial);
                CreatePrimitive("Fruit canopy", PrimitiveType.Sphere, tree,
                    new Vector3(0f, 2.35f, 0f), new Vector3(1.55f, 1.35f, 1.45f), _leafMaterial);
                for (var fruit = 0; fruit < 4; fruit++)
                {
                    var angle = fruit * Mathf.PI * 0.5f + i * 0.37f;
                    CreatePrimitive("Orchard fruit", PrimitiveType.Sphere, tree,
                        new Vector3(Mathf.Cos(angle) * 0.62f, 1.95f + (fruit % 2) * 0.35f,
                            Mathf.Sin(angle) * 0.62f),
                        new Vector3(0.15f, 0.15f, 0.15f), _produceMaterial);
                }
            }
            BuildFence(orchard, new Vector3(0f, 0f, 3.0f), 7, 0.72f);
            CreatePrimitive("Orchard harvest basket", PrimitiveType.Cylinder, orchard,
                new Vector3(2.8f, 0.24f, 2.2f), new Vector3(0.32f, 0.24f, 0.32f), _woodMaterial);
            for (var i = 0; i < 3; i++)
                CreatePrimitive("Basket fruit", PrimitiveType.Sphere, orchard,
                    new Vector3(2.65f + i * 0.16f, 0.48f, 2.2f),
                    new Vector3(0.16f, 0.14f, 0.16f), _produceMaterial);
        }


        private static void BuildNorthernResidentialLane(Transform root, VillageAmbientMotion ambient)
        {
            // A residential spur grows the settlement northward while staying inside the current
            // 30 m server scene boundary. These are visual buildings only until shared navigation
            // and collision footprints are introduced by the versioned settlement layout.
            var westLane = CreatePrimitive("North lane • west branch", PrimitiveType.Cube, root,
                new Vector3(-4.2f, 0.028f, 13.0f), new Vector3(1.65f, 0.055f, 12.5f), _pathMaterial);
            westLane.transform.rotation = Quaternion.Euler(0f, -28f, 0f);
            var eastLane = CreatePrimitive("North lane • east branch", PrimitiveType.Cube, root,
                new Vector3(4.7f, 0.03f, 14.1f), new Vector3(1.55f, 0.06f, 11.0f), _pathMaterial);
            eastLane.transform.rotation = Quaternion.Euler(0f, 24f, 0f);

            // Three modest homes create a readable second residential row rather than a ring of
            // identical buildings around the central square.
            BuildHouse(root, "Northwest cottage", new Vector3(-12.0f, 0f, 14.0f), -0.18f);
            BuildHouse(root, "North lane cottage", new Vector3(-3.0f, 0f, 21.0f), 0.12f);
            BuildHouse(root, "Northeast cottage", new Vector3(9.0f, 0f, 20.0f), -0.22f);

            // Small traces of everyday domestic life around the new homes.
            BuildLaundryLine(root, ambient, new Vector3(-9.0f, 0f, 12.8f));
            BuildWoodPile(root, new Vector3(-14.1f, 0f, 11.4f));
            BuildHandCart(root, new Vector3(-1.0f, 0f, 17.0f));
            BuildBarrel(root, new Vector3(10.8f, 0f, 18.0f), 0.78f);

            var kitchenGarden = new GameObject("North cottage kitchen garden").transform;
            kitchenGarden.SetParent(root, false);
            kitchenGarden.position = new Vector3(11.8f, 0f, 20.0f);
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 4; col++)
            {
                var x = -1.0f + col * 0.62f;
                var z = -0.8f + row * 0.62f;
                CreatePrimitive("Kitchen garden bed", PrimitiveType.Cube, kitchenGarden,
                    new Vector3(x, 0.10f, z), new Vector3(0.48f, 0.18f, 0.46f), _pathMaterial);
                var plant = CreatePrimitive("Kitchen garden herb", PrimitiveType.Sphere, kitchenGarden,
                    new Vector3(x, 0.34f, z), new Vector3(0.20f, 0.34f, 0.20f), _leafMaterial);
                plant.transform.localRotation = Quaternion.Euler(0f, (row * 37 + col * 23) % 360, 0f);
            }
            BuildFence(kitchenGarden, new Vector3(0f, 0f, 1.45f), 5, 0.52f);

            // A shared hearth is placed at the server's current hearth POI (4, 16), giving resting
            // residents a visible destination without inventing a new gameplay interaction.
            var hearth = new GameObject("North common hearth").transform;
            hearth.SetParent(root, false);
            hearth.position = new Vector3(4.0f, 0f, 16.0f);
            CreatePrimitive("Hearth stone ring", PrimitiveType.Cylinder, hearth,
                new Vector3(0f, 0.16f, 0f), new Vector3(0.82f, 0.16f, 0.82f), _stoneMaterial);
            CreatePrimitive("Hearth embers", PrimitiveType.Sphere, hearth,
                new Vector3(0f, 0.27f, 0f), new Vector3(0.48f, 0.12f, 0.48f), _fireMaterial);
            for (var i = 0; i < 3; i++)
            {
                var angle = i * Mathf.PI * 2f / 3f;
                var bench = CreatePrimitive("Hearth log seat", PrimitiveType.Cylinder, hearth,
                    new Vector3(Mathf.Cos(angle) * 1.55f, 0.24f, Mathf.Sin(angle) * 1.55f),
                    new Vector3(0.18f, 0.18f, 0.72f), _woodMaterial);
                bench.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 90f);
            }
            CreateSign(hearth, "ОБЩИЙ ОЧАГ", new Vector3(0f, 1.85f, -1.0f), 0.72f);
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
            DestroyAsset(_mugWoodMaterial);
            DestroyAsset(_mugBandMaterial);
            DestroyAsset(_bottleMaterial);
            DestroyAsset(_corkMaterial);
            DestroyAsset(_labelMaterial);
            DestroyAsset(_waterMaterial);
            for (var i = 0; i < _generatedMeshes.Count; i++)
                DestroyAsset(_generatedMeshes[i]);
            _generatedMeshes.Clear();
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
            _mugWoodMaterial = null;
            _mugBandMaterial = null;
            _bottleMaterial = null;
            _corkMaterial = null;
            _labelMaterial = null;
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