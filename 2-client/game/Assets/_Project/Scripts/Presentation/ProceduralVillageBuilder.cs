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
        private static Material _roadRutMaterial;
        private static Material _woodMaterial;
        private static Material _roofMaterial;
        private static Material _stoneMaterial;
        private static Material _leafMaterial;
        private static Material _grassMaterial;
        private static Material _beerMaterial;
        private static Material _foamMaterial;
        private static Material _fireMaterial;
        private static Material _fireCoreMaterial;
        private static Material _metalMaterial;
        private static Material _clothMaterial;
        private static Material _produceMaterial;
        private static Material _mugWoodMaterial;
        private static Material _mugBandMaterial;
        private static Material _bottleMaterial;
        private static Material _corkMaterial;
        private static Material _labelMaterial;
        private static Material _waterMaterial;
        private static Material _smokeMaterial;
        private static Texture2D _groundTexture;
        private static Texture2D _pathTexture;
        private static Texture2D _woodTexture;
        private static Texture2D _roofTexture;
        private static Texture2D _stoneTexture;
        private static Texture2D _waterTexture;
        private static Mesh _creekReedMesh;
        private static readonly System.Collections.Generic.List<Mesh> _generatedMeshes =
            new System.Collections.Generic.List<Mesh>();
        private static readonly System.Collections.Generic.List<Material> _generatedMaterials =
            new System.Collections.Generic.List<Material>();

        public static void Build(Transform root, string worldSeed = "tutorial-preview")
        {
            if (root == null || root.Find("PrototypeVillage") != null) return;

            var village = new GameObject("PrototypeVillage").transform;
            village.SetParent(root, false);
            var ambientMotion = village.gameObject.AddComponent<VillageAmbientMotion>();

            _groundMaterial = CreateMaterial("Ground • moss", Color.white);
            _groundTexture = MakeGroundTexture();
            _groundMaterial.mainTexture = _groundTexture;
            _groundMaterial.mainTextureScale = new Vector2(200, 200);
            _pathTexture = MakeSurfaceTexture("Packed earth texture", 1);
            _pathMaterial = CreateMaterial("Path • packed earth", new Color(0.48f, 0.38f, 0.27f));
            _pathMaterial.mainTexture = _pathTexture;
            _pathMaterial.mainTextureScale = new Vector2(3, 8);
            _roadRutMaterial = CreateMaterial("Road • dark wheel-worn earth", new Color(0.31f, 0.25f, 0.19f));
            _roadRutMaterial.mainTexture = _pathTexture;
            _roadRutMaterial.mainTextureScale = new Vector2(2, 7);
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
            _fireMaterial = CreateMaterial("Fire • ember orange", new Color(1f, 0.30f, 0.045f));
            _fireCoreMaterial = CreateMaterial("Fire • hot golden core", new Color(1f, 0.72f, 0.24f));
            SetMaterialSurface(_fireMaterial, 0f, 0.32f);
            SetMaterialSurface(_fireCoreMaterial, 0f, 0.18f);
            SetMaterialEmission(_fireMaterial, new Color(1f, 0.16f, 0.025f), 1.8f);
            SetMaterialEmission(_fireCoreMaterial, new Color(1f, 0.56f, 0.12f), 2.5f);
            _metalMaterial = CreateMaterial("Forged iron", new Color(0.20f, 0.22f, 0.23f));
            _clothMaterial = CreateMaterial("Market canvas", new Color(0.58f, 0.35f, 0.22f));
            _produceMaterial = CreateMaterial("Harvest produce", new Color(0.66f, 0.43f, 0.18f));
            _mugWoodMaterial = CreateMaterial("Tankard • oak staves", new Color(0.38f, 0.22f, 0.12f));
            _mugWoodMaterial.mainTexture = _woodTexture;
            _mugWoodMaterial.mainTextureScale = new Vector2(1.0f, 1.35f);
            _mugBandMaterial = CreateMaterial("Tankard • iron hoops", new Color(0.16f, 0.18f, 0.19f));
            SetMaterialSurface(_mugWoodMaterial, 0.02f, 0.28f);
            SetMaterialSurface(_mugBandMaterial, 0.78f, 0.42f);
            SetMaterialSurface(_metalMaterial, 0.68f, 0.36f);
            SetMaterialSurface(_beerMaterial, 0.0f, 0.78f);
            SetMaterialSurface(_foamMaterial, 0.0f, 0.22f);
            _bottleMaterial = CreateMaterial("Bottle • dark green glass", new Color(0.12f, 0.29f, 0.20f));
            _corkMaterial = CreateMaterial("Bottle • cork", new Color(0.48f, 0.31f, 0.16f));
            _labelMaterial = CreateMaterial("Bottle • parchment label", new Color(0.78f, 0.67f, 0.43f));
            _waterMaterial = CreateMaterial("Creek water", new Color(0.20f, 0.39f, 0.43f));
            _waterTexture = MakeSurfaceTexture("Creek current texture", 5);
            _waterMaterial.mainTexture = _waterTexture;
            _waterMaterial.mainTextureScale = new Vector2(1.5f, 8f);

            var ground = CreatePrimitive("Meadow", PrimitiveType.Plane, village, Vector3.zero,
                new Vector3(30, 1, 30), _groundMaterial);
            ground.isStatic = true;

            CreatePrimitive("Village path", PrimitiveType.Cube, village, new Vector3(0, 0.025f, 0),
                new Vector3(2.1f, 0.05f, 24), _pathMaterial);
            CreatePrimitive("Cross path", PrimitiveType.Cube, village, new Vector3(0, 0.03f, 0),
                new Vector3(22, 0.055f, 1.7f), _pathMaterial);

            BuildHouse(village, "North cottage", new Vector3(-34, 0, -25), 0.2f);
            BuildHouse(village, "Granary", new Vector3(-2, 0, 36), -0.12f);
            BuildHouse(village, "East cottage", new Vector3(12, 0, -31), -0.25f);
            BuildHouse(village, "West cottage", new Vector3(-8, 0, -29), 0.35f);
            BuildHouse(village, "South-east cottage", new Vector3(33, 0, -24), 0.15f);
            BuildTavern(village, new Vector3(-33, 0, 20));
            BuildBlacksmith(village, new Vector3(29f, 0, 22f));
            BuildMarket(village, new Vector3(1f, 0, 11f));
            BuildGarden(village, new Vector3(70f, 0, -20f));
            BuildWell(village, new Vector3(0f, 0, -4f));
            BuildFence(village, new Vector3(0, 0, -13), 12, 0.5f);
            BuildBarrel(village, new Vector3(4.1f, 0, 7.0f), 0.85f);
            BuildBarrel(village, new Vector3(4.8f, 0, 7.0f), 0.72f);
            BuildWoodPile(village, new Vector3(-8.2f, 0, -2.4f));
            BuildHandCart(village, new Vector3(-6.2f, 0, -7.4f));
            // The server-owned south river and bridge are built from Layout v2 by WorldLayoutPresentationBuilder.
            BuildDistantTreeLine(village);
            BuildOutskirts(village);
            BuildHayBales(village, worldSeed);
            BuildFarmCart(village, worldSeed);
            BuildRuralLandscape(village, worldSeed);
            BuildFarmsteadAndOrchard(village);
            BuildNorthernResidentialLane(village, ambientMotion);
            BuildPracticeStations(village);
            CreatePoiMarker(village, "Лес • рубить дрова", new Vector3(-82f, 0.08f, 24f),
                new Color(0.43f, 0.31f, 0.19f), null, "chop");
            BuildVillageLife(village, ambientMotion);
            BuildChickenYard(village);
            BuildStreetDetails(village, worldSeed);
            BuildFirewoodYards(village, worldSeed);
            BuildYardWaterTroughs(village);
            BuildYardChoppingStations(village);
            BuildWornFootpaths(village, worldSeed);
            BuildRoadsidePuddles(village, worldSeed);

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
            house.localScale = new Vector3(3.4f, 1f, 3.4f);
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
            leftRoof.transform.localRotation = Quaternion.Euler(0, 0, 25f);
            var rightRoof = CreatePrimitive("Thatched roof right", PrimitiveType.Cube, house,
                new Vector3(0.92f, 2.55f, 0), new Vector3(2.25f, 0.24f, 3.25f), _roofMaterial);
            rightRoof.transform.localRotation = Quaternion.Euler(0, 0, -25f);
            CreatePrimitive("Door", PrimitiveType.Cube, house, new Vector3(0, 0.72f, -1.415f),
                new Vector3(0.75f, 1.45f, 0.08f), _roofMaterial);
            BuildYardApron(house, name);
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
            BuildChimneySmoke(house, new Vector3(1.0f, 3.82f, 0.45f));
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
            BuildCottageGables(house);
            CreatePrimitive("Front timber beam", PrimitiveType.Cube, house, new Vector3(0, 2.05f, -1.44f),
                new Vector3(3.35f, 0.13f, 0.12f), _roofMaterial);
            CreatePrimitive("Door handle", PrimitiveType.Sphere, house, new Vector3(0.23f, 0.72f, -1.48f),
                new Vector3(0.07f, 0.07f, 0.04f), _metalMaterial);
        }

        private static void BuildCottageGables(Transform house)
        {
            // Close the triangular ends beneath the pitched roof with narrow weathered boards.
            // The same restrained timber framing is used on both ends of every cottage.
            for (var end = -1; end <= 1; end += 2)
            {
                var faceZ = end * 1.46f;
                for (var plank = -5; plank <= 5; plank++)
                {
                    var x = plank * 0.29f;
                    var top = 3.02f - Mathf.Abs(x) * (0.72f / 1.7f);
                    var height = Mathf.Max(0.06f, top - 2.30f);
                    var board = CreatePrimitive("Cottage gable • vertical infill board", PrimitiveType.Cube, house,
                        new Vector3(x, 2.30f + height * 0.5f, faceZ),
                        new Vector3(0.27f, height, 0.045f), _woodMaterial);
                    board.isStatic = true;
                }

                // Exposed sloping trim follows the corrected roof pitch and frames the gable.
                for (var side = -1; side <= 1; side += 2)
                {
                    var trim = CreatePrimitive("Cottage gable • sloping timber trim", PrimitiveType.Cube, house,
                        new Vector3(side * 0.85f, 2.66f, faceZ + end * 0.035f),
                        new Vector3(1.94f, 0.12f, 0.085f), _roofMaterial);
                    trim.transform.localRotation = Quaternion.Euler(0f, 0f, side < 0 ? 23f : -23f);
                    trim.isStatic = true;
                }

                // A small dark loft vent gives the otherwise solid gable a useful detail.
                var vent = CreatePrimitive("Cottage gable • loft vent", PrimitiveType.Cube, house,
                    new Vector3(0f, 2.70f, faceZ + end * 0.065f),
                    new Vector3(0.34f, 0.18f, 0.035f), _metalMaterial);
                vent.isStatic = true;
                var ventLintel = CreatePrimitive("Cottage gable • vent lintel", PrimitiveType.Cube, house,
                    new Vector3(0f, 2.81f, faceZ + end * 0.085f),
                    new Vector3(0.46f, 0.055f, 0.045f), _woodMaterial);
                ventLintel.isStatic = true;
                var ventSill = CreatePrimitive("Cottage gable • vent sill", PrimitiveType.Cube, house,
                    new Vector3(0f, 2.59f, faceZ + end * 0.085f),
                    new Vector3(0.46f, 0.055f, 0.045f), _woodMaterial);
                ventSill.isStatic = true;
            }
        }

        private static void BuildYardApron(Transform house, string houseName)
        {
            // A worn earth apron links each cottage threshold to the shared footpaths.
            // The mesh is presentation-only, has no collider and is tracked for Cleanup().
            var seed = StableSeed((houseName ?? string.Empty) + ":yard-apron");
            var random = new System.Random(seed);
            const int sides = 14;
            var vertices = new Vector3[sides + 1];
            var triangles = new int[sides * 3];
            vertices[0] = new Vector3(0f, 0.012f, -2.28f);

            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                var irregularity = 0.84f + (float)random.NextDouble() * 0.30f;
                var x = Mathf.Cos(angle) * 1.48f * irregularity;
                var z = -2.28f + Mathf.Sin(angle) * 0.94f * irregularity;
                vertices[i + 1] = new Vector3(x, 0.012f, z);
            }

            // Reverse perimeter order so the fan faces upward in Unity's XZ plane.
            for (var i = 0; i < sides; i++)
            {
                var current = i + 1;
                var next = (i + 1) % sides + 1;
                var triangle = i * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = next;
                triangles[triangle + 2] = current;
            }

            var mesh = new Mesh { name = houseName + " • worn yard earth" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var apron = CreateMeshPart("Worn earth at cottage entrance", house, mesh, _pathMaterial);
            apron.isStatic = true;

            // A few flattened stones break the edge where muddy earth meets the grass.
            for (var i = 0; i < 3; i++)
            {
                var angle = (i * 2.1f) + (float)random.NextDouble() * 0.35f;
                var x = Mathf.Cos(angle) * (1.18f + (float)random.NextDouble() * 0.24f);
                var z = -2.28f + Mathf.Sin(angle) * (0.70f + (float)random.NextDouble() * 0.18f);
                var stone = CreatePrimitive("Yard edge flat stone", PrimitiveType.Sphere, house,
                    new Vector3(x, 0.045f, z),
                    new Vector3(0.20f, 0.045f, 0.15f), _stoneMaterial);
                stone.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, 0f);
                stone.isStatic = true;
            }
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
            BuildChimneySmoke(tavern, new Vector3(1.8f, 4.12f, 0.8f));

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
            CreateImportedProp(tavern, "bench_rustic", new Vector3(0.55f, 0f, -2.72f),
                Vector3.one, "Tavern entrance bench • authored low-poly");
            // Keep the fire outside the server-blocked building footprint: the current
            // Layout v2 treats the tavern as a solid obstruction, so this courtyard fire
            // remains visible and reachable from the authoritative tavern POI.
            CreatePrimitive("Tavern courtyard fire • stone ring", PrimitiveType.Cylinder, tavern,
                new Vector3(-2.1f, 0.14f, -6.8f), new Vector3(0.92f, 0.12f, 0.92f), _stoneMaterial);
            CreatePrimitive("Tavern courtyard fire • charcoal bed", PrimitiveType.Cylinder, tavern,
                new Vector3(-2.1f, 0.225f, -6.8f), new Vector3(0.62f, 0.035f, 0.62f), _roofMaterial);

            // Two crossed oak logs give the courtyard fire a readable physical base.
            var fireLogA = CreatePrimitive("Firewood • lower log", PrimitiveType.Cylinder, tavern,
                new Vector3(-2.1f, 0.28f, -6.8f), new Vector3(0.052f, 0.22f, 0.052f), _woodMaterial);
            fireLogA.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var fireLogB = CreatePrimitive("Firewood • crossed log", PrimitiveType.Cylinder, tavern,
                new Vector3(-2.1f, 0.33f, -6.77f), new Vector3(0.046f, 0.19f, 0.046f), _roofMaterial);
            fireLogB.transform.localRotation = Quaternion.Euler(0f, 28f, 90f);

            var outerFlame = CreateMeshPart("Low-poly flame • orange tongues", tavern,
                BuildFireMesh(false), _fireMaterial);
            outerFlame.transform.localPosition = new Vector3(-2.1f, 0.29f, -6.8f);
            var innerFlame = CreateMeshPart("Low-poly flame • golden core", tavern,
                BuildFireMesh(true), _fireCoreMaterial);
            innerFlame.transform.localPosition = new Vector3(-2.1f, 0.30f, -6.825f);
            for (var i = 0; i < 3; i++)
            {
                CreatePrimitive("Fire • glowing ember", PrimitiveType.Sphere, tavern,
                    new Vector3(-2.2f + i * 0.09f, 0.31f + (i % 2) * 0.025f, -6.81f),
                    new Vector3(0.035f, 0.018f, 0.025f), _fireCoreMaterial);
            }

            var warmLightObject = new GameObject("Tavern warm light");
            warmLightObject.transform.SetParent(tavern, false);
            warmLightObject.transform.localPosition = new Vector3(-2.1f, 1.65f, -6.8f);
            var warmLight = warmLightObject.AddComponent<Light>();
            warmLight.type = LightType.Point;
            warmLight.range = 9f;
            warmLight.intensity = 1.6f;
            warmLight.color = new Color(1f, 0.66f, 0.36f);
            var ambient = root.GetComponent<VillageAmbientMotion>();
            if (ambient != null)
            {
                ambient.RegisterLight(warmLight, 0.12f, 3.1f);
                ambient.RegisterSway(outerFlame.transform, 2.6f, 3.2f);
                ambient.RegisterSway(innerFlame.transform, 1.8f, 4.1f);
            }

            CreatePoiMarker(root, "Таверна • поговорить", new Vector3(-33f, 0.08f, 12f),
                new Color(0.75f, 0.48f, 0.22f), "socialize");
        }

        /// <summary>Low-density decorative smoke; never participates in world simulation or collision.</summary>
        private static void BuildChimneySmoke(Transform parent, Vector3 localPosition)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            if (_smokeMaterial == null)
            {
                _smokeMaterial = new Material(shader) { name = "Chimney smoke • soft grey" };
                var smokeColor = new Color(0.48f, 0.49f, 0.47f, 0.48f);
                if (_smokeMaterial.HasProperty("_BaseColor")) _smokeMaterial.SetColor("_BaseColor", smokeColor);
                if (_smokeMaterial.HasProperty("_Color")) _smokeMaterial.SetColor("_Color", smokeColor);
                _smokeMaterial.SetOverrideTag("RenderType", "Transparent");
                if (_smokeMaterial.HasProperty("_Surface"))
                {
                    _smokeMaterial.SetFloat("_Surface", 1f);
                    _smokeMaterial.SetFloat("_Blend", 0f);
                    _smokeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                if (_smokeMaterial.HasProperty("_ZWrite")) _smokeMaterial.SetFloat("_ZWrite", 0f);
                _smokeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _smokeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _smokeMaterial.renderQueue = 3000;
            }

            var holder = new GameObject("Chimney smoke • slow rising wisps");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            var particles = holder.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.duration = 12f;
            main.maxParticles = 10;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startColor = new Color(0.62f, 0.63f, 0.61f, 0.65f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = particles.emission;
            emission.rateOverTime = 0.65f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.10f;
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.035f, 0.035f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.025f, 0.025f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.16f;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.62f, 0.16f),
                    new GradientAlphaKey(0.42f, 0.72f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void BuildFirewoodYards(Transform root, string worldSeed)
        {
            // Small stacked firewood reserves make the cottages feel inhabited.
            // These are static presentation props only: no resources, colliders or server POIs.
            var random = new System.Random(StableSeed((worldSeed ?? string.Empty) + ":firewood-yards"));
            var yards = new[]
            {
                new Vector3(-39f, 0f, -18f),
                new Vector3(18f, 0f, -36f),
                new Vector3(39f, 0f, -17f)
            };

            for (var yardIndex = 0; yardIndex < yards.Length; yardIndex++)
            {
                var yard = new GameObject("Firewood stack • cottage yard " + (yardIndex + 1)).transform;
                yard.SetParent(root, false);
                yard.localPosition = yards[yardIndex];
                yard.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 24f - 12f, 0f);

                // Two low stone skids keep the bottom logs out of the mud.
                for (var side = -1; side <= 1; side += 2)
                {
                    var skid = CreatePrimitive("Firewood stack • stone skid", PrimitiveType.Cube, yard,
                        new Vector3(side * 0.34f, 0.075f, 0f),
                        new Vector3(0.16f, 0.15f, 1.05f), _stoneMaterial);
                    skid.isStatic = true;
                }

                // Alternating log directions create a stable, readable hand-stacked pile.
                for (var layer = 0; layer < 3; layer++)
                {
                    var across = layer == 1 ? 3 : 4;
                    for (var i = 0; i < across; i++)
                    {
                        var offset = (i - (across - 1) * 0.5f) * 0.22f;
                        var alongX = layer != 1;
                        var log = CreatePrimitive("Firewood • split log", PrimitiveType.Cylinder, yard,
                            alongX
                                ? new Vector3(0f, 0.18f + layer * 0.19f, offset)
                                : new Vector3(offset, 0.18f + layer * 0.19f, 0f),
                            alongX ? new Vector3(0.105f, 0.34f, 0.105f)
                                   : new Vector3(0.105f, 0.34f, 0.105f),
                            (i + layer + yardIndex) % 3 == 0 ? _roofMaterial : _woodMaterial);
                        log.transform.localRotation = alongX
                            ? Quaternion.Euler(0f, 0f, 90f)
                            : Quaternion.Euler(90f, 0f, 0f);
                        log.isStatic = true;
                    }
                }

                // A few short offcuts and bark pieces make the pile less perfectly uniform.
                for (var i = 0; i < 3; i++)
                {
                    var chip = CreatePrimitive("Firewood • loose offcut", PrimitiveType.Cylinder, yard,
                        new Vector3((float)random.NextDouble() * 0.65f - 0.32f,
                            0.08f + (float)random.NextDouble() * 0.07f,
                            (float)random.NextDouble() * 0.92f - 0.46f),
                        new Vector3(0.075f, 0.16f, 0.075f), _woodMaterial);
                    chip.transform.localRotation = Quaternion.Euler(90f,
                        (float)random.NextDouble() * 180f, 0f);
                    chip.isStatic = true;
                }
            }
        }

        private static void BuildYardWaterTroughs(Transform root)
        {
            // Everyday water points belong beside the outlying cottages, not on the shared road.
            // All pieces are static, collider-free presentation props; water is not a gameplay resource.
            var positions = new[]
            {
                new Vector3(-40.5f, 0f, -10.5f),
                new Vector3(40.5f, 0f, -10.5f)
            };

            for (var i = 0; i < positions.Length; i++)
            {
                var trough = new GameObject("Yard water trough • " + (i + 1)).transform;
                trough.SetParent(root, false);
                trough.localPosition = positions[i];
                trough.localRotation = Quaternion.Euler(0f, i == 0 ? -8f : 11f, 0f);

                for (var side = -1; side <= 1; side += 2)
                {
                    var foot = CreatePrimitive("Trough • stone foot", PrimitiveType.Cube, trough,
                        new Vector3(side * 0.31f, 0.075f, 0f),
                        new Vector3(0.20f, 0.15f, 0.24f), _stoneMaterial);
                    foot.isStatic = true;
                }

                var bottom = CreatePrimitive("Trough • oak bottom", PrimitiveType.Cube, trough,
                    new Vector3(0f, 0.19f, 0f), new Vector3(0.86f, 0.12f, 1.62f), _woodMaterial);
                bottom.isStatic = true;
                for (var side = -1; side <= 1; side += 2)
                {
                    var wall = CreatePrimitive("Trough • long oak side", PrimitiveType.Cube, trough,
                        new Vector3(side * 0.43f, 0.40f, 0f),
                        new Vector3(0.12f, 0.42f, 1.68f), _woodMaterial);
                    wall.isStatic = true;
                    var end = CreatePrimitive("Trough • end board", PrimitiveType.Cube, trough,
                        new Vector3(0f, 0.40f, side * 0.80f),
                        new Vector3(0.86f, 0.42f, 0.12f), _woodMaterial);
                    end.isStatic = true;
                }

                var water = CreatePrimitive("Trough • still water", PrimitiveType.Cube, trough,
                    new Vector3(0f, 0.345f, 0f), new Vector3(0.69f, 0.018f, 1.39f), _waterMaterial);
                water.isStatic = true;

                var bucket = CreatePrimitive("Trough • wooden bucket", PrimitiveType.Cylinder, trough,
                    new Vector3(0.83f, 0.24f, 0.48f), new Vector3(0.18f, 0.23f, 0.18f), _woodMaterial);
                bucket.isStatic = true;
                var bucketBand = CreatePrimitive("Trough • bucket iron band", PrimitiveType.Cylinder, trough,
                    new Vector3(0.83f, 0.20f, 0.48f), new Vector3(0.185f, 0.035f, 0.185f), _metalMaterial);
                bucketBand.isStatic = true;
                for (var handleSide = -1; handleSide <= 1; handleSide += 2)
                {
                    var handlePost = CreatePrimitive("Trough • bucket handle post", PrimitiveType.Cube, trough,
                        new Vector3(0.83f + handleSide * 0.13f, 0.39f, 0.48f),
                        new Vector3(0.025f, 0.25f, 0.025f), _metalMaterial);
                    handlePost.isStatic = true;
                }
                var handleGrip = CreatePrimitive("Trough • bucket handle grip", PrimitiveType.Cube, trough,
                    new Vector3(0.83f, 0.515f, 0.48f), new Vector3(0.285f, 0.025f, 0.035f), _metalMaterial);
                handleGrip.isStatic = true;
            }
        }

        private static void BuildYardChoppingStations(Transform root)
        {
            // A small chopping place completes the firewood routine around the cottages.
            // These are static scenery props, not an interactive resource or server work POI.
            var positions = new[]
            {
                new Vector3(-36.5f, 0f, -13.2f),
                new Vector3(36.5f, 0f, -13.2f)
            };

            for (var i = 0; i < positions.Length; i++)
            {
                var station = new GameObject("Cottage chopping place • " + (i + 1)).transform;
                station.SetParent(root, false);
                station.localPosition = positions[i];
                station.localRotation = Quaternion.Euler(0f, i == 0 ? -13f : 9f, 0f);

                var stump = CreatePrimitive("Chopping block • cut oak stump", PrimitiveType.Cylinder, station,
                    new Vector3(0f, 0.31f, 0f), new Vector3(0.34f, 0.31f, 0.34f), _woodMaterial);
                stump.isStatic = true;
                var cutTop = CreatePrimitive("Chopping block • pale cut surface", PrimitiveType.Cylinder, station,
                    new Vector3(0f, 0.625f, 0f), new Vector3(0.31f, 0.018f, 0.31f), _roofMaterial);
                cutTop.isStatic = true;

                // A split billet rests beside the stump, with its cut face visible.
                var billet = CreatePrimitive("Chopping place • split billet", PrimitiveType.Cylinder, station,
                    new Vector3(0.58f, 0.16f, 0.24f), new Vector3(0.13f, 0.25f, 0.13f), _woodMaterial);
                billet.transform.localRotation = Quaternion.Euler(90f, 0f, 90f);
                billet.isStatic = true;

                // A simple low-poly axe leans against the stump: wooden haft and iron head.
                var haft = CreatePrimitive("Chopping place • axe haft", PrimitiveType.Cylinder, station,
                    new Vector3(-0.39f, 0.59f, 0.08f), new Vector3(0.035f, 0.43f, 0.035f), _woodMaterial);
                haft.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
                haft.isStatic = true;
                var blade = CreatePrimitive("Chopping place • iron axe head", PrimitiveType.Cube, station,
                    new Vector3(-0.52f, 0.91f, 0.08f), new Vector3(0.22f, 0.16f, 0.055f), _metalMaterial);
                blade.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
                blade.isStatic = true;
            }
        }

        private static void BuildStreetDetails(Transform root, string worldSeed)
        {
            // Presentation-only street dressing. Keep the central walking line clear and avoid
            // adding colliders or changing any server POI/navigation coordinates.
            var random = new System.Random(StableSeed((worldSeed ?? string.Empty) + ":street-details"));

            // Shallow wheel-worn tracks make the packed-earth road read as a travelled surface.
            for (var side = -1; side <= 1; side += 2)
            {
                var rut = CreatePrimitive("Main road • wheel-worn track", PrimitiveType.Cube, root,
                    new Vector3(side * 0.43f, 0.057f, 0f), new Vector3(0.075f, 0.012f, 23.2f), _roofMaterial);
                rut.isStatic = true;
                var crossRut = CreatePrimitive("Crossroad • wheel-worn track", PrimitiveType.Cube, root,
                    new Vector3(0f, 0.059f, side * 0.34f), new Vector3(21.0f, 0.012f, 0.07f), _roofMaterial);
                crossRut.isStatic = true;
            }

            // Uneven gravel shoulders frame the road without narrowing its usable centre.
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 34; i++)
                {
                    var z = -11.4f + i * 0.69f + (float)random.NextDouble() * 0.28f;
                    var x = side * (1.18f + (float)random.NextDouble() * 0.42f);
                    var size = 0.08f + (float)random.NextDouble() * 0.13f;
                    var stone = CreatePrimitive("Roadside loose gravel", PrimitiveType.Sphere, root,
                        new Vector3(x, 0.075f, z),
                        new Vector3(size * 1.35f, size * 0.42f, size), _stoneMaterial);
                    stone.transform.rotation = Quaternion.Euler(
                        (float)random.NextDouble() * 18f,
                        (float)random.NextDouble() * 180f,
                        (float)random.NextDouble() * 18f);
                    stone.isStatic = true;
                }

                // Occasional grass tufts grow at the verge, never in the road itself.
                for (var i = 0; i < 13; i++)
                {
                    var z = -10.8f + i * 1.75f + (float)random.NextDouble() * 0.45f;
                    var x = side * (1.48f + (float)random.NextDouble() * 0.32f);
                    var tuft = CreatePrimitive("Roadside verge grass", PrimitiveType.Cube, root,
                        new Vector3(x, 0.16f, z),
                        new Vector3(0.07f, 0.28f + (float)random.NextDouble() * 0.12f, 0.07f),
                        _grassMaterial);
                    tuft.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f,
                        (float)random.NextDouble() * 16f - 8f);
                    tuft.isStatic = true;
                }
            }

            // The cross street gets lighter, more broken gravel rather than a second solid border.
            for (var i = 0; i < 25; i++)
            {
                var x = -10.2f + i * 0.84f + (float)random.NextDouble() * 0.25f;
                var side = random.Next(0, 2) == 0 ? -1f : 1f;
                var z = side * (1.0f + (float)random.NextDouble() * 0.52f);
                var size = 0.07f + (float)random.NextDouble() * 0.12f;
                var stone = CreatePrimitive("Cross street gravel", PrimitiveType.Sphere, root,
                    new Vector3(x, 0.073f, z),
                    new Vector3(size * 1.3f, size * 0.4f, size), _stoneMaterial);
                stone.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, 0f);
                stone.isStatic = true;
            }

            // Small muddy patches beside the road suggest rain and foot traffic; they sit below
            // the road surface and are deliberately matte, not reflective water decals.
            for (var i = 0; i < 7; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var x = side * (1.55f + (float)random.NextDouble() * 0.65f);
                var z = -8.5f + i * 2.65f;
                var patch = CreatePrimitive("Roadside damp earth", PrimitiveType.Cylinder, root,
                    new Vector3(x, 0.035f, z),
                    new Vector3(0.38f + (float)random.NextDouble() * 0.22f, 0.018f,
                        0.22f + (float)random.NextDouble() * 0.16f), _pathMaterial);
                patch.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, 0f);
                patch.isStatic = true;
            }
        }

        private static void BuildRoadsidePuddles(Transform root, string worldSeed)
        {
            // Tiny, shallow puddles collect in the wheel ruts and at the road verge.
            // They are decorative only and stay outside the clear central walking line.
            var random = new System.Random(StableSeed((worldSeed ?? string.Empty) + ":roadside-puddles"));
            var positions = new[]
            {
                new Vector3(-0.72f, 0.045f, -9.1f),
                new Vector3(0.78f, 0.045f, -6.7f),
                new Vector3(-0.86f, 0.045f, 7.9f),
                new Vector3(0.72f, 0.045f, 10.2f),
                new Vector3(-9.4f, 0.045f, -0.72f),
                new Vector3(9.7f, 0.045f, 0.68f)
            };

            for (var i = 0; i < positions.Length; i++)
            {
                var position = positions[i];
                var length = 0.34f + (float)random.NextDouble() * 0.28f;
                var width = 0.13f + (float)random.NextDouble() * 0.11f;
                var puddle = CreatePrimitive("Shallow road puddle", PrimitiveType.Cylinder, root,
                    position, new Vector3(length, 0.012f, width), _waterMaterial);
                puddle.transform.rotation = Quaternion.Euler(
                    0f, (float)random.NextDouble() * 180f, 0f);
                puddle.isStatic = true;

                // A broken muddy lip keeps the water from reading as a clean geometric decal.
                if (i % 2 == 0)
                {
                    var mud = CreatePrimitive("Puddle muddy edge", PrimitiveType.Cylinder, root,
                        new Vector3(position.x + (i % 3 == 0 ? 0.16f : -0.14f), 0.031f,
                            position.z + 0.10f),
                        new Vector3(length * 1.22f, 0.009f, width * 1.55f), _pathMaterial);
                    mud.transform.rotation = puddle.transform.rotation;
                    mud.isStatic = true;
                }
            }
        }

        private static void BuildWornFootpaths(Transform root, string worldSeed)
        {
            // Small, irregular stepping-stone paths connect the main street to lived-in places.
            // They are visual dressing only: no colliders, POIs or server navigation changes.
            var random = new System.Random(StableSeed((worldSeed ?? string.Empty) + ":footpaths"));
            BuildFootpath(root, new Vector3(0.25f, 0.06f, 5.35f),
                new Vector3(-5.55f, 0.06f, 5.35f), 9, random);
            BuildFootpath(root, new Vector3(-2.0f, 0.06f, -1.1f),
                new Vector3(-8.25f, 0.06f, -6.0f), 11, random);
            BuildFootpath(root, new Vector3(2.0f, 0.06f, 0.75f),
                new Vector3(6.55f, 0.06f, 2.65f), 8, random);
            BuildFootpath(root, new Vector3(3.0f, 0.06f, -5.4f),
                new Vector3(5.8f, 0.06f, -8.15f), 6, random);
        }

        private static void BuildFootpath(Transform root, Vector3 start, Vector3 end, int stoneCount,
            System.Random random)
        {
            var direction = end - start;
            var perpendicular = new Vector3(-direction.z, 0f, direction.x).normalized;
            var rotation = Quaternion.Euler(0f, Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg, 0f);

            for (var i = 0; i < stoneCount; i++)
            {
                var t = (i + 0.5f) / stoneCount;
                var jitter = ((float)random.NextDouble() - 0.5f) * 0.22f;
                var sideOffset = ((float)random.NextDouble() - 0.5f) * 0.26f;
                var point = Vector3.Lerp(start, end, Mathf.Clamp01(t + jitter)) +
                            perpendicular * sideOffset;
                var length = 0.30f + (float)random.NextDouble() * 0.18f;
                var width = 0.24f + (float)random.NextDouble() * 0.16f;
                var stone = CreatePrimitive("Worn footpath stepping stone", PrimitiveType.Cube, root,
                    new Vector3(point.x, point.y, point.z),
                    new Vector3(length, 0.055f, width), _stoneMaterial);
                stone.transform.rotation = rotation * Quaternion.Euler(
                    0f, (float)random.NextDouble() * 18f - 9f, 0f);
                stone.isStatic = true;
            }
        }

        private static void BuildChickenYard(Transform root)
        {
            // A tiny domestic yard adds movement and a clear everyday-life vignette.
            // It is decorative only: no server fauna, POIs, collision or gameplay resources.
            var yard = new GameObject("Chicken yard • domestic life").transform;
            yard.SetParent(root, false);
            yard.localPosition = new Vector3(12f, 0f, -7f);

            var straw = CreatePrimitive("Chicken yard • straw ground", PrimitiveType.Cylinder, yard,
                new Vector3(0f, 0.035f, 0f), new Vector3(2.25f, 0.025f, 1.75f), _pathMaterial);
            straw.transform.localRotation = Quaternion.Euler(0f, 11f, 0f);

            // Low rails mark the domestic space but remain visual dressing without colliders.
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 4; i++)
                {
                    var z = -1.9f + i * 1.27f;
                    CreatePrimitive("Chicken yard • fence post", PrimitiveType.Cube, yard,
                        new Vector3(side * 2.65f, 0.43f, z), new Vector3(0.11f, 0.86f, 0.11f), _woodMaterial);
                }
                CreatePrimitive("Chicken yard • fence rail", PrimitiveType.Cube, yard,
                    new Vector3(side * 2.65f, 0.56f, 0f), new Vector3(0.09f, 0.10f, 4.0f), _roofMaterial);
            }
            // Leave a central opening toward the lane instead of fencing across the gate.
            for (var side = -1; side <= 1; side += 2)
                CreatePrimitive("Chicken yard • front rail", PrimitiveType.Cube, yard,
                    new Vector3(side * 1.65f, 0.56f, 1.9f), new Vector3(2.0f, 0.10f, 0.09f), _roofMaterial);
            CreatePrimitive("Chicken yard • gate post", PrimitiveType.Cube, yard,
                new Vector3(-0.65f, 0.42f, 1.9f), new Vector3(0.11f, 0.84f, 0.11f), _woodMaterial);

            var coop = new GameObject("Small chicken coop").transform;
            coop.SetParent(yard, false);
            coop.localPosition = new Vector3(-1.75f, 0f, -1.25f);
            CreatePrimitive("Coop body", PrimitiveType.Cube, coop,
                new Vector3(0f, 0.53f, 0f), new Vector3(1.25f, 0.92f, 0.95f), _woodMaterial);
            CreatePrimitive("Coop roof", PrimitiveType.Cube, coop,
                new Vector3(0f, 1.12f, 0f), new Vector3(1.48f, 0.16f, 1.12f), _roofMaterial);
            CreatePrimitive("Coop entrance", PrimitiveType.Cube, coop,
                new Vector3(0f, 0.28f, 0.49f), new Vector3(0.36f, 0.52f, 0.035f), _metalMaterial);
            CreatePrimitive("Nest straw", PrimitiveType.Sphere, coop,
                new Vector3(0.38f, 0.99f, -0.12f), new Vector3(0.34f, 0.12f, 0.30f), _produceMaterial);

            CreatePrimitive("Chicken feed trough", PrimitiveType.Cube, yard,
                new Vector3(1.72f, 0.19f, 0.75f), new Vector3(1.15f, 0.30f, 0.34f), _woodMaterial);
            for (var i = 0; i < 5; i++)
                CreatePrimitive("Scattered grain", PrimitiveType.Sphere, yard,
                    new Vector3(1.35f + i * 0.17f, 0.36f, 0.53f + (i % 2) * 0.12f),
                    new Vector3(0.045f, 0.025f, 0.045f), _produceMaterial);

            BuildYardChicken(yard, new Vector3(-0.75f, 0f, 0.15f), _foamMaterial, 1701);
            BuildYardChicken(yard, new Vector3(0.65f, 0f, -0.55f), _mugWoodMaterial, 1702);
            BuildYardChicken(yard, new Vector3(1.45f, 0f, -0.95f), _foamMaterial, 1703);
        }

        private static void BuildYardChicken(Transform parent, Vector3 position, Material plumage, int seed)
        {
            var chicken = new GameObject("Village hen • ambient animal").transform;
            chicken.SetParent(parent, false);
            chicken.localPosition = position;

            var body = CreatePrimitive("Hen body", PrimitiveType.Sphere, chicken,
                new Vector3(0f, 0.43f, 0f), new Vector3(0.43f, 0.31f, 0.60f), plumage).transform;
            CreatePrimitive("Hen wing L", PrimitiveType.Sphere, body,
                new Vector3(-0.78f, 0.02f, -0.03f), new Vector3(0.42f, 0.68f, 0.78f), _clothMaterial);
            CreatePrimitive("Hen wing R", PrimitiveType.Sphere, body,
                new Vector3(0.78f, 0.02f, -0.03f), new Vector3(0.42f, 0.68f, 0.78f), _clothMaterial);
            var tail = CreatePrimitive("Hen tail", PrimitiveType.Sphere, chicken,
                new Vector3(0f, 0.59f, -0.48f), new Vector3(0.19f, 0.32f, 0.19f), plumage);
            tail.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);

            var head = new GameObject("Hen head").transform;
            head.SetParent(chicken, false);
            head.localPosition = new Vector3(0f, 0.66f, 0.30f);
            CreatePrimitive("Hen head plumage", PrimitiveType.Sphere, head,
                Vector3.zero, new Vector3(0.23f, 0.23f, 0.23f), plumage);
            CreatePrimitive("Hen beak", PrimitiveType.Cube, head,
                new Vector3(0f, -0.015f, 0.18f), new Vector3(0.09f, 0.13f, 0.10f), _produceMaterial);
            CreatePrimitive("Hen comb", PrimitiveType.Sphere, head,
                new Vector3(0f, 0.16f, -0.015f), new Vector3(0.10f, 0.15f, 0.08f), _fireMaterial);
            for (var side = -1; side <= 1; side += 2)
                CreatePrimitive("Hen eye", PrimitiveType.Sphere, head,
                    new Vector3(side * 0.105f, 0.025f, 0.12f), new Vector3(0.035f, 0.035f, 0.035f), _metalMaterial);

            var leftLeg = CreatePrimitive("Hen leg L", PrimitiveType.Cylinder, chicken,
                new Vector3(-0.13f, 0.14f, 0.04f), new Vector3(0.035f, 0.14f, 0.035f), _produceMaterial).transform;
            var rightLeg = CreatePrimitive("Hen leg R", PrimitiveType.Cylinder, chicken,
                new Vector3(0.13f, 0.14f, 0.04f), new Vector3(0.035f, 0.14f, 0.035f), _produceMaterial).transform;
            CreatePrimitive("Hen foot L", PrimitiveType.Cube, chicken,
                new Vector3(-0.13f, 0.025f, 0.11f), new Vector3(0.13f, 0.035f, 0.17f), _produceMaterial);
            CreatePrimitive("Hen foot R", PrimitiveType.Cube, chicken,
                new Vector3(0.13f, 0.025f, 0.11f), new Vector3(0.13f, 0.035f, 0.17f), _produceMaterial);

            chicken.gameObject.AddComponent<VillageChickenMotion>()
                .Initialize(body, head, leftLeg, rightLeg, seed);
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
            if (CreateImportedProp(goods, "basket_wickermarket", new Vector3(2.25f, 0f, 0.10f),
                Vector3.one, "Wicker market basket • authored low-poly") == null)
            {
                // Keep the existing crate-and-produce dressing when the optional asset is absent.
            }
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
            CreateTavernTableware(table);
        }

        private static void CreateTavernTableware(Transform table)
        {
            // Shared table dressing: a bread plate and a small bowl beside the tankard.
            CreatePrimitive("Wooden serving plate", PrimitiveType.Cylinder, table,
                new Vector3(-0.34f, 0.895f, 0.02f), new Vector3(0.34f, 0.025f, 0.29f), _roofMaterial);
            CreatePrimitive("Plate inner surface", PrimitiveType.Cylinder, table,
                new Vector3(-0.34f, 0.912f, 0.02f), new Vector3(0.27f, 0.012f, 0.23f), _woodMaterial);

            var bread = CreateMeshPart("Tavern bread loaf", table, BuildBreadLoafMesh(), _produceMaterial);
            bread.transform.localPosition = new Vector3(-0.34f, 0.94f, 0.02f);
            bread.transform.localScale = new Vector3(0.72f, 0.62f, 0.62f);
            bread.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);

            CreatePrimitive("Serving bowl", PrimitiveType.Cylinder, table,
                new Vector3(0.38f, 0.91f, 0.25f), new Vector3(0.22f, 0.055f, 0.22f), _woodMaterial);
            CreatePrimitive("Bowl contents", PrimitiveType.Sphere, table,
                new Vector3(0.38f, 0.946f, 0.25f), new Vector3(0.15f, 0.018f, 0.15f), _produceMaterial);
        }

        private static void CreateBeerMug(Transform parent, Vector3 position)
        {
            var authored = CreateImportedProp(parent, "tankard_oakbeer", position, Vector3.one,
                "Oak beer tankard • authored low-poly");
            if (authored != null) return;

            var mug = CreateTankardView(parent, "Oak beer tankard • procedural fallback");
            if (mug != null) mug.transform.localPosition = position;
        }

        private static GameObject CreateImportedProp(Transform parent, string resourceName,
            Vector3 localPosition, Vector3 localScale, string objectName)
        {
            if (parent == null || string.IsNullOrEmpty(resourceName)) return null;
            var prefab = Resources.Load<GameObject>("TavernProps/" + resourceName);
            if (prefab == null) return null;

            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = string.IsNullOrEmpty(objectName) ? prefab.name : objectName;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = localScale;
            ApplyImportedPropMaterials(instance);
            return instance;
        }

        private static void ApplyImportedPropMaterials(GameObject instance)
        {
            if (instance == null) return;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var source = materials[materialIndex];
                    if (source == null) continue;
                    var materialName = source.name.ToLowerInvariant();
                    Material replacement = null;
                    if (materialName.Contains("amber") || materialName.Contains("ale"))
                        replacement = _beerMaterial;
                    else if (materialName.Contains("foam"))
                        replacement = _foamMaterial;
                    else if (materialName.Contains("iron"))
                        replacement = _metalMaterial;
                    else if (materialName.Contains("apple") || materialName.Contains("bread"))
                        replacement = _produceMaterial;
                    else if (materialName.Contains("wicker") || materialName.Contains("oak") ||
                             materialName.Contains("timber"))
                        replacement = _woodMaterial;
                    else if (materialName.Contains("dark seam"))
                        replacement = _roofMaterial;

                    if (replacement != null)
                    {
                        materials[materialIndex] = replacement;
                        changed = true;
                    }
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// Creates the same detailed tankard mesh used by tavern props for a held-item view.
        /// Returns null until the village presentation materials have been initialized.
        /// </summary>
        public static GameObject CreateTankardView(Transform parent, string objectName)
        {
            if (parent == null || _mugWoodMaterial == null || _mugBandMaterial == null ||
                _beerMaterial == null || _foamMaterial == null) return null;

            var mug = new GameObject(string.IsNullOrEmpty(objectName) ? "Oak beer tankard" : objectName).transform;
            mug.SetParent(parent, false);
            mug.localPosition = Vector3.zero;
            mug.localScale = Vector3.one;

            CreateMeshPart("Hollow oak tankard", mug, BuildTankardBodyMesh(), _mugWoodMaterial);
            for (var y = 0.055f; y <= 0.255f; y += 0.20f)
            {
                var hoop = CreateMeshPart("Forged iron tankard hoop", mug,
                    BuildTankardHoopMesh(0.127f - (0.255f - y) * 0.012f, 0.018f, 10), _mugBandMaterial);
                hoop.transform.localPosition = new Vector3(0f, y, 0f);
            }

            var handle = CreateMeshPart("Curved wooden tankard handle", mug,
                BuildTankardHandleMesh(), _mugWoodMaterial);
            handle.transform.localPosition = new Vector3(0f, 0.17f, 0f);

            CreatePrimitive("Amber ale • liquid inside tankard", PrimitiveType.Cylinder, mug,
                new Vector3(0f, 0.261f, 0f), new Vector3(0.136f, 0.006f, 0.136f), _beerMaterial);
            CreateMeshPart("Ale foam • irregular cap", mug, BuildTankardFoamMesh(), _foamMaterial);
            for (var i = 0; i < 4; i++)
            {
                var angle = i * Mathf.PI * 0.5f + 0.28f;
                var radius = 0.055f + (i % 2) * 0.012f;
                CreatePrimitive("Foam bubble", PrimitiveType.Sphere, mug,
                    new Vector3(Mathf.Cos(angle) * radius, 0.287f + (i % 2) * 0.003f,
                        Mathf.Sin(angle) * radius),
                    new Vector3(0.014f, 0.006f, 0.014f), _foamMaterial);
            }

            return mug.gameObject;
        }

        /// <summary>
        /// Builds a small, presentation-only tool in an entity's hand from the server activity code.
        /// Returns null for neutral activities or before the village materials are initialized.
        /// </summary>
        public static GameObject CreateActivityToolView(Transform hand, string activity)
        {
            if (hand == null || _woodMaterial == null || _metalMaterial == null) return null;
            if (activity != "chop" && activity != "mine" && activity != "forge" &&
                activity != "farm" && activity != "fish" && activity != "hunt")
                return null;

            var tool = new GameObject("Held activity tool • " + activity).transform;
            tool.SetParent(hand, false);
            tool.localPosition = new Vector3(0f, -0.04f, 0.10f);
            tool.localRotation = Quaternion.identity;
            tool.localScale = Vector3.one * 3f;

            // These objects move with the hand, so they must remain dynamic (not marked static).
            CreatePrimitive("Tool • wooden haft", PrimitiveType.Cylinder, tool,
                new Vector3(0f, 0.20f, 0f), new Vector3(0.022f, 0.22f, 0.022f), _woodMaterial);

            switch (activity)
            {
                case "chop":
                {
                    var axeBlade = CreatePrimitive("Tool • axe iron blade", PrimitiveType.Cube, tool,
                        new Vector3(0.13f, 0.38f, 0f), new Vector3(0.24f, 0.13f, 0.055f), _metalMaterial);
                    axeBlade.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
                    break;
                }
                case "mine":
                {
                    CreatePrimitive("Tool • pick iron head", PrimitiveType.Cube, tool,
                        new Vector3(0f, 0.42f, 0f), new Vector3(0.34f, 0.075f, 0.075f), _metalMaterial);
                    break;
                }
                case "forge":
                {
                    CreatePrimitive("Tool • smith hammer head", PrimitiveType.Cube, tool,
                        new Vector3(0f, 0.43f, 0f), new Vector3(0.23f, 0.13f, 0.13f), _metalMaterial);
                    break;
                }
                case "farm":
                {
                    var hoeBlade = CreatePrimitive("Tool • hoe iron blade", PrimitiveType.Cube, tool,
                        new Vector3(0f, 0.43f, 0f), new Vector3(0.29f, 0.055f, 0.075f), _metalMaterial);
                    hoeBlade.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
                    break;
                }
                case "fish":
                {
                    var rod = CreatePrimitive("Tool • fishing rod", PrimitiveType.Cylinder, tool,
                        new Vector3(0f, 0.42f, 0f), new Vector3(0.010f, 0.26f, 0.010f), _woodMaterial);
                    rod.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
                    CreatePrimitive("Tool • fishing line", PrimitiveType.Cube, tool,
                        new Vector3(0.045f, 0.66f, 0.015f), new Vector3(0.006f, 0.22f, 0.006f), _clothMaterial);
                    break;
                }
                case "hunt":
                {
                    var spearTip = CreatePrimitive("Tool • hunting spear tip", PrimitiveType.Cube, tool,
                        new Vector3(0f, 0.45f, 0f), new Vector3(0.075f, 0.17f, 0.045f), _metalMaterial);
                    spearTip.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
                    break;
                }
            }

            return tool.gameObject;
        }

        private static Mesh BuildTankardFoamMesh()
        {
            const int sides = 12;
            var vertices = new Vector3[sides * 2 + 1];
            var triangles = new int[sides * 9];
            var center = sides * 2;

            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                var scallop = 1f + Mathf.Sin(i * 2.37f) * 0.045f
                    + Mathf.Cos(i * 1.61f) * 0.025f;
                var radius = 0.092f * scallop;
                var x = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                vertices[i] = new Vector3(x, 0.269f, z);
                vertices[sides + i] = new Vector3(x, 0.282f, z);
            }
            vertices[center] = new Vector3(0f, 0.282f, 0f);

            var t = 0;
            for (var i = 0; i < sides; i++)
            {
                var next = (i + 1) % sides;
                // Outer skirt, followed by an upward-facing top fan.
                triangles[t++] = i;
                triangles[t++] = sides + i;
                triangles[t++] = next;
                triangles[t++] = next;
                triangles[t++] = sides + i;
                triangles[t++] = sides + next;
                triangles[t++] = center;
                triangles[t++] = sides + next;
                triangles[t++] = sides + i;
            }

            var mesh = new Mesh { name = "Irregular low-poly ale foam cap" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

            // Small emissive flecks rise from the forge without relying on the Particle System module.
            var embers = new Transform[4];
            for (var i = 0; i < embers.Length; i++)
            {
                embers[i] = CreatePrimitive("Forge ember", PrimitiveType.Sphere, smithy,
                    new Vector3(-1.1f, 1.12f, -1.45f), Vector3.one * 0.035f, _fireCoreMaterial).transform;
            }
            smithy.gameObject.AddComponent<ForgeEmberMotion>()
                .Initialize(embers, new Vector3(-1.1f, 1.12f, -1.45f), 4817);

            var forgeLightObject = new GameObject("Forge warm point light");
            forgeLightObject.transform.SetParent(smithy, false);
            forgeLightObject.transform.localPosition = new Vector3(-1.1f, 1.15f, -1.35f);
            var forgeLight = forgeLightObject.AddComponent<Light>();
            forgeLight.type = LightType.Point;
            forgeLight.range = 5.2f;
            forgeLight.intensity = 0.78f;
            forgeLight.color = new Color(1f, 0.36f, 0.12f);
            var ambient = root.GetComponent<VillageAmbientMotion>();
            if (ambient != null) ambient.RegisterLight(forgeLight, 0.28f, 3.4f);
            CreateSign(smithy, "КУЗНИЦА", new Vector3(0, 1.8f, -1.58f), 0.75f);
            CreatePoiMarker(root, "Кузница • работать", position + new Vector3(0, 0.08f, -8f), new Color(0.55f, 0.55f, 0.58f), "work");
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
            CreatePoiMarker(root, "Рынок • поесть", new Vector3(1f, 0.08f, 4f), new Color(0.72f, 0.62f, 0.30f), "eat");
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

            // Two continuous-looking reed courses weave around the upright staves.
            for (var course = 0; course < 2; course++)
            {
                var y = course == 0 ? 0.115f : 0.215f;
                for (var i = 0; i < 12; i++)
                {
                    var angle = i * Mathf.PI * 2f / 12f;
                    var reed = CreatePrimitive("Basket horizontal wicker weave", PrimitiveType.Cube, basket,
                        new Vector3(Mathf.Cos(angle) * width * 0.40f, y,
                            Mathf.Sin(angle) * depth * 0.40f),
                        new Vector3(0.12f, 0.018f, 0.028f),
                        (i + course) % 2 == 0 ? _roofMaterial : _woodMaterial);
                    reed.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f);
                }
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
                var z = row * 0.9f + 2.8f;
                CreatePrimitive("Garden bed • raised soil", PrimitiveType.Cube, garden,
                    new Vector3(x, 0.12f, z), new Vector3(0.62f, 0.22f, 0.68f), _pathMaterial);

                // Two separate leafy plants replace the old single green sphere per bed.
                for (var plant = 0; plant < 2; plant++)
                {
                    var side = plant == 0 ? -0.14f : 0.14f;
                    var height = 0.30f + ((row + col + plant) % 3) * 0.055f;
                    var stem = CreatePrimitive("Garden crop • stem", PrimitiveType.Cylinder, garden,
                        new Vector3(x + side, 0.24f + height * 0.5f, z + (plant == 0 ? -0.08f : 0.08f)),
                        new Vector3(0.025f, height * 0.5f, 0.025f), _leafMaterial);
                    stem.isStatic = true;

                    for (var leaf = 0; leaf < 3; leaf++)
                    {
                        var direction = leaf == 0 ? -1f : leaf == 1 ? 1f : 0f;
                        var leafObject = CreatePrimitive("Garden crop • leaf", PrimitiveType.Sphere, garden,
                            new Vector3(x + side + direction * 0.105f,
                                0.34f + height * (leaf == 2 ? 0.72f : 0.48f),
                                z + (plant == 0 ? -0.08f : 0.08f) + (leaf == 2 ? 0.025f : direction * 0.035f)),
                            new Vector3(0.20f, 0.075f, 0.105f), _leafMaterial);
                        leafObject.transform.localRotation = Quaternion.Euler(0f, direction * 24f, direction * -28f);
                        leafObject.isStatic = true;
                    }
                }
            }
            CreatePoiMarker(root, "Огород • работать", new Vector3(70f, 0.08f, -20f), new Color(0.32f, 0.58f, 0.28f), "work");
        }


        private static void BuildBarrel(Transform root, Vector3 position, float scale)
        {
            var authored = CreateImportedProp(root, "barrel_oak", position,
                Vector3.one * (scale * 1.17f), "Oak barrel • authored low-poly");
            if (authored != null) return;

            var barrel = new GameObject("Oak barrel • procedural fallback").transform;        {
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
            const int ringStride = sides + 1;
            var profile = new[] { new Vector2(0.31f, 0.02f), new Vector2(0.37f, 0.16f),
                new Vector2(0.405f, 0.48f), new Vector2(0.37f, 0.80f), new Vector2(0.31f, 0.94f) };
            var vertices = new Vector3[profile.Length * ringStride + 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(profile.Length - 1) * sides * 6 + sides * 6];
            for (var ring = 0; ring < profile.Length; ring++)
            for (var side = 0; side <= sides; side++)
            {
                var angle = side * Mathf.PI * 2f / sides;
                var index = ring * ringStride + side;
                vertices[index] = new Vector3(
                    Mathf.Cos(angle) * profile[ring].x, profile[ring].y,
                    Mathf.Sin(angle) * profile[ring].x);
                // Duplicate the seam vertex so the timber texture wraps cleanly from U=1 to U=0.
                uvs[index] = new Vector2((float)side / sides, profile[ring].y / 0.94f);
            }

            var t = 0;
            for (var ring = 0; ring < profile.Length - 1; ring++)
            for (var side = 0; side < sides; side++)
            {
                var a = ring * ringStride + side;
                var b = a + 1;
                var c = (ring + 1) * ringStride + side + 1;
                var d = (ring + 1) * ringStride + side;
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }

            var bottomCenter = profile.Length * ringStride;
            var topCenter = bottomCenter + 1;
            vertices[bottomCenter] = Vector3.up * profile[0].y;
            vertices[topCenter] = Vector3.up * profile[profile.Length - 1].y;
            uvs[bottomCenter] = new Vector2(0.5f, 0.5f);
            uvs[topCenter] = new Vector2(0.5f, 0.5f);
            for (var side = 0; side < sides; side++)
            {
                // Down-facing bottom cap and up-facing top cap.
                triangles[t++] = bottomCenter;
                triangles[t++] = side + 1;
                triangles[t++] = side;
                var top = (profile.Length - 1) * ringStride;
                triangles[t++] = topCenter;
                triangles[t++] = top + side;
                triangles[t++] = top + side + 1;
            }

            var mesh = new Mesh { name = "Ten-sided bulging oak barrel" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
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
            const float wheelRadius = 0.275f;
            const float wheelCenterY = wheelRadius;
            const float wheelThickness = 0.12f;
            const int rimSegments = 12;
            const int spokeCount = 8;
            const float hubRadius = 0.055f;
            const float rimInnerRadius = 0.205f;
            const float spokeLength = rimInnerRadius - hubRadius;
            const float spokeCenterRadius = (rimInnerRadius + hubRadius) * 0.5f;
            const float rimCenterRadius = (wheelRadius + rimInnerRadius) * 0.5f;
            const float rimRadialWidth = wheelRadius - rimInnerRadius;

            for (var side = -1; side <= 1; side += 2)
            {
                var wheelX = side * 0.72f;
                for (var segment = 0; segment < rimSegments; segment++)
                {
                    var angle = segment * (Mathf.PI * 2f / rimSegments);
                    var radial = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                    var segmentLength = 2f * rimCenterRadius * Mathf.Sin(Mathf.PI / rimSegments) + 0.012f;
                    var rimPiece = CreatePrimitive("Cart wheel wooden rim segment", PrimitiveType.Cube, cart,
                        new Vector3(wheelX, wheelCenterY + radial.y * rimCenterRadius,
                            radial.z * rimCenterRadius),
                        new Vector3(wheelThickness, rimRadialWidth, segmentLength), _woodMaterial);
                    rimPiece.transform.localRotation = Quaternion.Euler(segment * (360f / rimSegments), 0f, 0f);
                }

                for (var face = -1; face <= 1; face += 2)
                {
                    var faceX = wheelX + face * (wheelThickness * 0.5f + 0.012f);
                    for (var spoke = 0; spoke < spokeCount; spoke++)
                    {
                        var angle = spoke * (Mathf.PI * 2f / spokeCount);
                        var direction = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                        var spokeView = CreatePrimitive("Cart wheel wooden spoke", PrimitiveType.Cylinder, cart,
                            new Vector3(faceX, wheelCenterY + direction.y * spokeCenterRadius,
                                direction.z * spokeCenterRadius),
                            new Vector3(0.028f, spokeLength * 0.5f, 0.028f), _woodMaterial);
                        spokeView.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                    }

                    var hub = CreatePrimitive("Cart wheel iron hub", PrimitiveType.Cylinder, cart,
                        new Vector3(faceX, wheelCenterY, 0f), new Vector3(0.16f, 0.065f, 0.065f), _metalMaterial);
                    hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }

            // Presentation-only axle connects both wheel hubs; it does not affect server movement.
            var axle = CreatePrimitive("Cart wooden axle", PrimitiveType.Cylinder, cart,
                new Vector3(0f, wheelCenterY, 0f), new Vector3(0.10f, 0.66f, 0.10f), _woodMaterial);
            axle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            CreatePrimitive("Cart handle", PrimitiveType.Cube, cart, new Vector3(0, 0.52f, -0.78f),
                new Vector3(0.10f, 0.10f, 1.0f), _woodMaterial);
        }

        private static void BuildCreekAndFootbridge(Transform root)
        {
            var creek = CreatePrimitive("Narrow village creek", PrimitiveType.Cube, root,
                new Vector3(15.5f, -0.08f, 0), new Vector3(2.2f, 0.12f, 32f), _waterMaterial);
            var waterMotion = creek.AddComponent<CreekWaterDrift>();
            waterMotion.Initialize(_waterMaterial, 0.018f);
            BuildCreekBank(root, -1);
            BuildCreekBank(root, 1);
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

        private static void BuildCreekBank(Transform root, int side)
        {
            // A shallow, uneven mesh replaces the hard rectangular creek-bank slabs.
            // Outer edge blends into meadow; inner edge drops toward the water surface.
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];
            var random = new System.Random(StableSeed("creek-bank:" + side));

            for (var i = 0; i <= segments; i++)
            {
                var t = (float)i / segments;
                var z = -15.8f + t * 31.6f;
                var edgeJitter = ((float)random.NextDouble() - 0.5f) * 0.22f;
                var outerX = side < 0 ? 13.55f : 17.45f;
                var innerX = side < 0 ? 14.39f : 16.61f;
                outerX += ((float)random.NextDouble() - 0.5f) * 0.10f;
                innerX += ((float)random.NextDouble() - 0.5f) * 0.08f;
                var outerY = 0.025f + ((float)random.NextDouble() - 0.5f) * 0.025f;
                var innerY = -0.018f + ((float)random.NextDouble() - 0.5f) * 0.018f;
                var row = i * 2;
                vertices[row] = new Vector3(outerX, outerY, z + edgeJitter);
                vertices[row + 1] = new Vector3(innerX, innerY, z + edgeJitter);
            }

            for (var i = 0; i < segments; i++)
            {
                var a = i * 2;
                var b = a + 1;
                var d = a + 2;
                var e = a + 3;
                var t = i * 6;
                if (side < 0)
                {
                    // Counter-clockwise from above for the western bank.
                    triangles[t] = a; triangles[t + 1] = d; triangles[t + 2] = b;
                    triangles[t + 3] = b; triangles[t + 4] = d; triangles[t + 5] = e;
                }
                else
                {
                    triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = d;
                    triangles[t + 3] = b; triangles[t + 4] = e; triangles[t + 5] = d;
                }
            }

            var mesh = new Mesh { name = side < 0 ? "Creek west sloped bank" : "Creek east sloped bank" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var bank = CreateMeshPart("Uneven creek bank", root, mesh, _pathMaterial);
            bank.isStatic = true;

            // Scattered flat stones soften the waterline; leave the footbridge approach open.
            for (var i = 0; i < 10; i++)
            {
                var z = -14.2f + i * 2.85f + ((float)random.NextDouble() - 0.5f) * 0.65f;
                if (z > -4.2f && z < 0.2f) continue;
                var x = side < 0 ? 14.20f : 16.80f;
                x += ((float)random.NextDouble() - 0.5f) * 0.22f;
                var size = 0.10f + (float)random.NextDouble() * 0.11f;
                var stone = CreatePrimitive("Creek waterline stone", PrimitiveType.Sphere, root,
                    new Vector3(x, 0.015f, z), new Vector3(size * 1.25f, 0.055f, size), _stoneMaterial);
                stone.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, 0f);
                stone.isStatic = true;
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

            // Individual boards and diagonal braces break the broad door slabs into old timberwork.
            for (var leaf = -1; leaf <= 1; leaf += 2)
            {
                var centerX = leaf * 0.62f;
                for (var plank = -1; plank <= 1; plank++)
                {
                    var board = CreatePrimitive("Barn door • vertical oak board", PrimitiveType.Cube, barn,
                        new Vector3(centerX + plank * 0.34f, 1.0f, -2.145f),
                        new Vector3(0.075f, 1.82f, 0.025f), _woodMaterial);
                    board.isStatic = true;
                }

                var brace = CreatePrimitive("Barn door • diagonal timber brace", PrimitiveType.Cube, barn,
                    new Vector3(centerX, 1.0f, -2.17f),
                    new Vector3(1.02f, 0.12f, 0.055f), _woodMaterial);
                brace.transform.localRotation = Quaternion.Euler(0f, 0f, leaf * 31f);
                brace.isStatic = true;

                for (var hinge = -1; hinge <= 1; hinge += 2)
                {
                    var ironHinge = CreatePrimitive("Barn door • iron strap hinge", PrimitiveType.Cube, barn,
                        new Vector3(centerX + leaf * 0.48f, hinge * 0.62f + 1.0f, -2.19f),
                        new Vector3(0.48f, 0.065f, 0.035f), _metalMaterial);
                    ironHinge.isStatic = true;
                }
            }

            // A small dark loft opening gives the barn a readable upper working level.
            var loftOpening = CreatePrimitive("Barn loft • dark hay opening", PrimitiveType.Cube, barn,
                new Vector3(0f, 2.52f, -2.035f), new Vector3(0.92f, 0.52f, 0.035f), _metalMaterial);
            loftOpening.isStatic = true;
            for (var side = -1; side <= 1; side += 2)
            {
                var jamb = CreatePrimitive("Barn loft • timber jamb", PrimitiveType.Cube, barn,
                    new Vector3(side * 0.52f, 2.52f, -2.09f), new Vector3(0.10f, 0.70f, 0.12f), _roofMaterial);
                jamb.isStatic = true;
            }
            var loftLintel = CreatePrimitive("Barn loft • timber lintel", PrimitiveType.Cube, barn,
                new Vector3(0f, 2.88f, -2.09f), new Vector3(1.14f, 0.10f, 0.12f), _roofMaterial);
            loftLintel.isStatic = true;
            var loftSill = CreatePrimitive("Barn loft • timber sill", PrimitiveType.Cube, barn,
                new Vector3(0f, 2.16f, -2.09f), new Vector3(1.14f, 0.10f, 0.12f), _roofMaterial);
            loftSill.isStatic = true;
            var loftCrossbar = CreatePrimitive("Barn loft • wooden crossbar", PrimitiveType.Cube, barn,
                new Vector3(0f, 2.52f, -2.12f), new Vector3(0.92f, 0.07f, 0.06f), _woodMaterial);
            loftCrossbar.isStatic = true;

            BuildBarnFenceAndGate(root, new Vector3(-20f, 0f, -17f));

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

        private static void BuildHayBales(Transform root, string worldSeed)
        {
            // A small stack beside the outlying barn makes the farmyard read as an active
            // harvest site. These are static presentation props, not inventory or resources.
            var random = new System.Random(StableSeed((worldSeed ?? string.Empty) + ":barn-hay"));
            var stack = new GameObject("Barn hay bales • harvest storage").transform;
            stack.SetParent(root, false);
            stack.localPosition = new Vector3(-25.5f, 0f, -12.1f);
            stack.localRotation = Quaternion.Euler(0f, -9f, 0f);

            BuildHayBale(stack, new Vector3(-0.62f, 0.38f, 0f), random);
            BuildHayBale(stack, new Vector3(0.62f, 0.38f, 0.04f), random);
            BuildHayBale(stack, new Vector3(0f, 1.14f, 0.02f), random);

            // Loose straw around the stack softens its silhouette against the packed earth.
            for (var i = 0; i < 9; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = 0.95f + (float)random.NextDouble() * 0.55f;
                var straw = CreatePrimitive("Loose straw at hay stack", PrimitiveType.Cube, stack,
                    new Vector3(Mathf.Cos(angle) * radius, 0.045f, Mathf.Sin(angle) * radius),
                    new Vector3(0.16f, 0.025f, 0.035f), _produceMaterial);
                straw.transform.localRotation = Quaternion.Euler(0f,
                    (float)random.NextDouble() * 180f, (float)random.NextDouble() * 12f - 6f);
                straw.isStatic = true;
            }
        }

        private static void BuildFarmCart(Transform root, string worldSeed)
        {
            // A parked harvest cart gives the barn yard a readable working purpose.
            // It is a static visual prop: no collider, inventory, resource or server state.
            var random = new System.Random(StableSeed((worldSeed ?? string.Empty) + ":barn-farm-cart"));
            var cart = new GameObject("Barn yard • parked harvest cart").transform;
            cart.SetParent(root, false);
            cart.localPosition = new Vector3(-23.2f, 0f, -15.0f);
            cart.localRotation = Quaternion.Euler(0f, 180f + (float)random.NextDouble() * 5f - 2.5f, 0f);

            // The timber bed sits above a single cross-axle, with open space below the boards.
            var axle = CreatePrimitive("Farm cart • iron-bound axle", PrimitiveType.Cylinder, cart,
                new Vector3(0f, 0.57f, -0.18f), new Vector3(0.075f, 0.94f, 0.075f), _metalMaterial);
            axle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            axle.isStatic = true;

            CreatePrimitive("Farm cart • timber bed", PrimitiveType.Cube, cart,
                new Vector3(0f, 1.03f, 0f), new Vector3(1.55f, 0.16f, 1.55f), _woodMaterial);
            CreatePrimitive("Farm cart • front board", PrimitiveType.Cube, cart,
                new Vector3(0f, 1.34f, 0.73f), new Vector3(1.58f, 0.48f, 0.10f), _roofMaterial);
            CreatePrimitive("Farm cart • rear board", PrimitiveType.Cube, cart,
                new Vector3(0f, 1.34f, -0.73f), new Vector3(1.58f, 0.48f, 0.10f), _roofMaterial);

            for (var side = -1; side <= 1; side += 2)
            {
                CreatePrimitive("Farm cart • side board", PrimitiveType.Cube, cart,
                    new Vector3(side * 0.75f, 1.34f, 0f), new Vector3(0.10f, 0.48f, 1.42f), _woodMaterial);

                // Long shafts point toward the open yard gate, ready for a horse or ox.
                var shaft = CreatePrimitive("Farm cart • pulling shaft", PrimitiveType.Cube, cart,
                    new Vector3(side * 0.49f, 0.53f, 1.32f), new Vector3(0.12f, 0.12f, 1.65f), _woodMaterial);
                shaft.transform.localRotation = Quaternion.Euler(
                    (float)random.NextDouble() * 2f - 1f, 0f, (float)random.NextDouble() * 2f - 1f);
                shaft.isStatic = true;

                var grip = CreatePrimitive("Farm cart • shaft iron tip", PrimitiveType.Cylinder, cart,
                    new Vector3(side * 0.49f, 0.53f, 2.13f), new Vector3(0.075f, 0.09f, 0.075f), _metalMaterial);
                grip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                grip.isStatic = true;

                BuildFarmCartWheel(cart, side * 0.91f);
            }

            // A few loose sacks and a crate make the cart look used without implying loot.
            var sackA = CreatePrimitive("Farm cart • grain sack", PrimitiveType.Capsule, cart,
                new Vector3(-0.34f, 1.38f, -0.12f), new Vector3(0.30f, 0.27f, 0.28f), _clothMaterial);
            sackA.transform.localRotation = Quaternion.Euler(0f, 18f, 9f);
            sackA.isStatic = true;
            var sackB = CreatePrimitive("Farm cart • grain sack", PrimitiveType.Capsule, cart,
                new Vector3(0.30f, 1.37f, 0.18f), new Vector3(0.27f, 0.25f, 0.27f), _clothMaterial);
            sackB.transform.localRotation = Quaternion.Euler(0f, -24f, -7f);
            sackB.isStatic = true;
            var crate = CreatePrimitive("Farm cart • seed crate", PrimitiveType.Cube, cart,
                new Vector3(0.28f, 1.28f, -0.48f), new Vector3(0.48f, 0.34f, 0.42f), _roofMaterial);
            crate.transform.localRotation = Quaternion.Euler(0f, 7f, 0f);
            crate.isStatic = true;
            for (var side = -1; side <= 1; side += 2)
            {
                var crateSlat = CreatePrimitive("Farm cart • crate slat", PrimitiveType.Cube, cart,
                    new Vector3(0.28f + side * 0.17f, 1.30f, -0.265f),
                    new Vector3(0.035f, 0.28f, 0.025f), _woodMaterial);
                crateSlat.isStatic = true;
            }
        }

        private static void BuildFarmCartWheel(Transform cart, float x)
        {
            var wheel = new GameObject("Farm cart • wooden spoked wheel").transform;
            wheel.SetParent(cart, false);
            wheel.localPosition = new Vector3(x, 0.57f, -0.18f);
            wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Eight visible spokes and a faceted outer rim keep the wheels readable at village scale.
            for (var spoke = 0; spoke < 8; spoke++)
            {
                var angle = spoke * Mathf.PI / 4f;
                var beam = CreatePrimitive("Wheel • wooden spoke", PrimitiveType.Cube, wheel,
                    new Vector3(Mathf.Cos(angle) * 0.25f, 0f, Mathf.Sin(angle) * 0.25f),
                    new Vector3(0.50f, 0.075f, 0.065f), _roofMaterial);
                beam.transform.localRotation = Quaternion.Euler(0f, -spoke * 45f, 0f);
                beam.isStatic = true;
            }

            for (var segment = 0; segment < 12; segment++)
            {
                var angle = segment * Mathf.PI / 6f;
                var rim = CreatePrimitive("Wheel • outer wooden rim segment", PrimitiveType.Cube, wheel,
                    new Vector3(Mathf.Cos(angle) * 0.49f, 0f, Mathf.Sin(angle) * 0.49f),
                    new Vector3(0.30f, 0.11f, 0.10f), _woodMaterial);
                rim.transform.localRotation = Quaternion.Euler(0f, segment * 30f + 90f, 0f);
                rim.isStatic = true;
            }

            var hub = CreatePrimitive("Wheel • iron hub", PrimitiveType.Cylinder, wheel,
                Vector3.zero, new Vector3(0.16f, 0.10f, 0.16f), _metalMaterial);
            hub.isStatic = true;
        }

        private static void BuildHayBale(Transform parent, Vector3 position, System.Random random)
        {
            var bale = new GameObject("Bound rectangular hay bale").transform;
            bale.SetParent(parent, false);
            bale.localPosition = position;
            bale.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 14f - 7f, 0f);

            var body = CreatePrimitive("Compressed golden hay", PrimitiveType.Cube, bale,
                Vector3.zero, new Vector3(1.08f, 0.70f, 0.76f), _produceMaterial);
            body.isStatic = true;

            // Two visible twine loops cross the top and both long faces of each bale.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 0.29f;
                var top = CreatePrimitive("Hay bale • binding across top", PrimitiveType.Cube, bale,
                    new Vector3(x, 0.356f, 0f), new Vector3(0.035f, 0.018f, 0.80f), _woodMaterial);
                top.isStatic = true;
                for (var face = -1; face <= 1; face += 2)
                {
                    var strap = CreatePrimitive("Hay bale • binding down face", PrimitiveType.Cube, bale,
                        new Vector3(x, 0f, face * 0.386f), new Vector3(0.035f, 0.72f, 0.018f), _woodMaterial);
                    strap.isStatic = true;
                }
            }
        }

        private static void BuildRuralLandscape(Transform root, string worldSeed)
        {
            // Broad, legible farmland and country lanes make the settlement read as part
            // of a rural region. These are presentation-only: no colliders, POIs or server
            // navigation are created here. Keep the playable/server boundary unchanged.
            CreateCountryRoad(root, "Northwest country road", new Vector3(-18f, 0.018f, 0f),
                new Vector3(34f, 0.045f, 2.8f), -32f);
            CreateCountryRoad(root, "Eastern country road", new Vector3(18f, 0.020f, -3f),
                new Vector3(34f, 0.045f, 3.1f), 24f);
            CreateCountryRoad(root, "Southern farm lane", new Vector3(-4f, 0.022f, -22f),
                new Vector3(42f, 0.045f, 2.5f), 8f);

            BuildLargeField(root, "West grain field", new Vector3(-21f, 0f, -19f),
                15f, 11f, 10, 0.2f, worldSeed + ":west-field");
            BuildLargeField(root, "East grain field", new Vector3(22f, 0f, 17f),
                13f, 15f, 11, 0.8f, worldSeed + ":east-field");
            BuildLargeField(root, "Northern meadow plots", new Vector3(-5f, 0f, 25f),
                17f, 8f, 8, -0.35f, worldSeed + ":north-meadow");

            // Broken hedgerows and tree belts frame the fields without forming hard walls.
            BuildFieldTreeBelt(root, new Vector3(-29f, 0f, -18f), 13, true, worldSeed + ":west-belt");
            BuildFieldTreeBelt(root, new Vector3(29f, 0f, 16f), 15, false, worldSeed + ":east-belt");

            // Low wildflowers soften the transition from cultivated soil to meadow.
            // Keep them at the outer margins, away from road ribbons and clear walking lines.
            BuildWildflowerStrip(root, new Vector3(-25f, 0f, -32.2f), 14f, true,
                worldSeed + ":west-flowers");
            BuildWildflowerStrip(root, new Vector3(37.2f, 0f, 15f), 19f, false,
                worldSeed + ":east-flowers");
            BuildWildflowerStrip(root, new Vector3(-6f, 0f, 34.8f), 18f, true,
                worldSeed + ":north-flowers");

            BuildDistantRuralHorizon(root, worldSeed);
        }

        private static void BuildWildflowerStrip(Transform root, Vector3 center, float length,
            bool alongX, string seedKey)
        {
            var random = new System.Random(StableSeed(seedKey));
            const int flowers = 24;
            for (var i = 0; i < flowers; i++)
            {
                // Small gaps and uneven offsets create a naturally colonised field edge.
                if (random.NextDouble() < 0.18) continue;
                var t = (i + (float)random.NextDouble()) / flowers - 0.5f;
                var lateral = ((float)random.NextDouble() - 0.5f) * 1.15f;
                var position = center + (alongX
                    ? new Vector3(t * length, 0f, lateral)
                    : new Vector3(lateral, 0f, t * length));
                var height = 0.22f + (float)random.NextDouble() * 0.19f;
                var stem = CreatePrimitive("Wildflower • slender stem", PrimitiveType.Cylinder, root,
                    position + Vector3.up * (height * 0.5f), new Vector3(0.018f, height * 0.5f, 0.018f),
                    _grassMaterial);
                stem.transform.localRotation = Quaternion.Euler(
                    (float)random.NextDouble() * 10f - 5f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 10f - 5f);
                stem.isStatic = true;

                var bloomPosition = position + Vector3.up * height;
                var bloom = CreatePrimitive("Wildflower • pale bloom", PrimitiveType.Sphere, root,
                    bloomPosition, new Vector3(0.095f, 0.065f, 0.095f), _foamMaterial);
                bloom.isStatic = true;
                var heart = CreatePrimitive("Wildflower • golden heart", PrimitiveType.Sphere, root,
                    bloomPosition + Vector3.up * 0.018f,
                    new Vector3(0.038f, 0.035f, 0.038f), _produceMaterial);
                heart.isStatic = true;
            }
        }

        private static void CreateCountryRoad(Transform root, string name, Vector3 position,
            Vector3 size, float yaw)
        {
            // Build a deterministic, gently wandering centerline. The same road-path
            // helper also accepts arbitrary waypoint polylines, so later settlement-to-
            // settlement routes can reuse the exact surface/rut treatment without seams.
            var radians = yaw * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Cos(radians), 0f, -Mathf.Sin(radians));
            var side = new Vector3(-forward.z, 0f, forward.x);
            var start = position - forward * (size.x * 0.5f);
            var end = position + forward * (size.x * 0.5f);
            var random = new System.Random(StableSeed(name));
            var bend = ((float)random.NextDouble() - 0.5f) * 1.35f;
            var centerline = new Vector3[9];
            for (var i = 0; i < centerline.Length; i++)
            {
                var t = i / (float)(centerline.Length - 1);
                var edgeNoise = i == 0 || i == centerline.Length - 1
                    ? 0f
                    : ((float)random.NextDouble() - 0.5f) * 0.16f;
                centerline[i] = Vector3.Lerp(start, end, t)
                    + side * (Mathf.Sin(t * Mathf.PI) * bend + edgeNoise);
                centerline[i].y = position.y;
            }

            BuildRoadPath(root, name, centerline, size.z);
            BuildCountryRoadDitches(root, name, centerline, size.z);
        }

        private static void BuildCountryRoadDitches(Transform root, string name,
            Vector3[] centerline, float roadWidth)
        {
            // Shallow drainage runs follow the road's actual bends instead of cutting across
            // the meadow. Their wet centre and muddy lips are presentation-only mesh ribbons.
            var ditchOffset = roadWidth * 0.5f + 0.62f;
            for (var side = -1; side <= 1; side += 2)
            {
                var bank = CreateMeshPart(name + " • muddy drainage bank", root,
                    BuildRoadRibbonMesh(name + " • ditch bank", centerline,
                        0.48f, side * ditchOffset, 0.004f, false), _pathMaterial);
                bank.isStatic = true;

                var water = CreateMeshPart(name + " • shallow roadside water", root,
                    BuildRoadRibbonMesh(name + " • ditch water", centerline,
                        0.18f, side * ditchOffset, 0.013f, false), _waterMaterial);
                water.isStatic = true;
            }
        }

        /// <summary>
        /// Builds a presentation-only dirt road from shared waypoints. Adjacent road
        /// segments can reuse the same endpoint, allowing a continuous road graph between
        /// villages once the world/settlement placement data is available.
        /// </summary>
        public static void BuildRoadPath(Transform root, string name, Vector3[] centerline, float width)
        {
            if (root == null || centerline == null || centerline.Length < 2 ||
                _pathMaterial == null || _roadRutMaterial == null) return;

            var road = CreateMeshPart(name + " • packed-earth ribbon", root,
                BuildRoadRibbonMesh(name + " • surface", centerline, width, 0f, 0.025f, true),
                _pathMaterial);
            road.isStatic = true;

            // Two darker wheel tracks make the lane read as a used road rather than a flat strip.
            var rutOffset = width * 0.20f;
            var rutWidth = Mathf.Clamp(width * 0.085f, 0.16f, 0.28f);
            var leftRut = CreateMeshPart(name + " • left wheel rut", root,
                BuildRoadRibbonMesh(name + " • left rut", centerline, rutWidth, rutOffset, 0.032f, false),
                _roadRutMaterial);
            leftRut.isStatic = true;
            var rightRut = CreateMeshPart(name + " • right wheel rut", root,
                BuildRoadRibbonMesh(name + " • right rut", centerline, rutWidth, -rutOffset, 0.032f, false),
                _roadRutMaterial);
            rightRut.isStatic = true;
        }

        private static Mesh BuildRoadRibbonMesh(string meshName, Vector3[] centerline,
            float width, float lateralOffset, float height, bool irregularEdges)
        {
            var vertices = new Vector3[centerline.Length * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(centerline.Length - 1) * 6];
            var distance = 0f;
            for (var i = 0; i < centerline.Length; i++)
            {
                var tangent = i == 0
                    ? centerline[1] - centerline[0]
                    : i == centerline.Length - 1
                        ? centerline[i] - centerline[i - 1]
                        : centerline[i + 1] - centerline[i - 1];
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
                tangent.Normalize();
                var perpendicular = new Vector3(-tangent.z, 0f, tangent.x);
                var edgeVariation = irregularEdges
                    ? Mathf.Sin(i * 1.73f) * 0.035f + Mathf.Sin(i * 0.61f + 1.2f) * 0.022f
                    : 0f;
                var halfWidth = Mathf.Max(0.05f, width * 0.5f + edgeVariation);
                var center = centerline[i] + perpendicular * lateralOffset;
                center.y += height;
                var left = center + perpendicular * halfWidth;
                var right = center - perpendicular * halfWidth;
                vertices[i * 2] = left;
                vertices[i * 2 + 1] = right;

                if (i > 0) distance += Vector3.Distance(centerline[i - 1], centerline[i]);
                uvs[i * 2] = new Vector2(distance * 0.20f, 0f);
                uvs[i * 2 + 1] = new Vector2(distance * 0.20f, 1f);
            }

            var triangle = 0;
            for (var i = 0; i < centerline.Length - 1; i++)
            {
                var left = i * 2;
                var right = left + 1;
                var nextLeft = left + 2;
                var nextRight = left + 3;
                triangles[triangle++] = left;
                triangles[triangle++] = nextLeft;
                triangles[triangle++] = right;
                triangles[triangle++] = nextLeft;
                triangles[triangle++] = nextRight;
                triangles[triangle++] = right;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildLargeField(Transform root, string name, Vector3 center,
            float halfWidth, float halfLength, int rows, float yaw, string seedKey)
        {
            var field = new GameObject(name).transform;
            field.SetParent(root, false);
            field.position = center;
            field.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);

            // A single irregular low-poly soil patch reads as a proper field, not a tiny garden bed.
            const int sides = 16;
            var vertices = new Vector3[sides + 1];
            var triangles = new int[sides * 3];
            var random = new System.Random(StableSeed(seedKey));
            vertices[0] = new Vector3(0f, 0.028f, 0f);
            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                var variation = 0.90f + (float)random.NextDouble() * 0.16f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * halfWidth * variation,
                    0.028f, Mathf.Sin(angle) * halfLength * variation);
            }
            for (var i = 0; i < sides; i++)
            {
                var t = i * 3;
                triangles[t] = 0;
                triangles[t + 1] = (i + 1) % sides + 1;
                triangles[t + 2] = i + 1;
            }
            var soilMesh = new Mesh { name = name + " • irregular soil" };
            soilMesh.vertices = vertices;
            soilMesh.triangles = triangles;
            soilMesh.RecalculateNormals();
            soilMesh.RecalculateBounds();
            var soil = CreateMeshPart(name + " soil", field, soilMesh, _pathMaterial);
            soil.isStatic = true;

            // Long, widely spaced furrows provide scale and a readable agricultural rhythm.
            var rowLength = halfWidth * 1.72f;
            for (var row = 0; row < rows; row++)
            {
                var z = Mathf.Lerp(-halfLength * 0.76f, halfLength * 0.76f,
                    (row + 0.5f) / rows);
                var ridge = CreatePrimitive("Long crop furrow", PrimitiveType.Cube, field,
                    new Vector3(0f, 0.105f, z),
                    new Vector3(rowLength, 0.13f, 0.34f), _groundMaterial);
                ridge.isStatic = true;
                for (var clump = 0; clump < 9; clump++)
                {
                    var x = Mathf.Lerp(-rowLength * 0.46f, rowLength * 0.46f, (clump + 0.5f) / 9f);
                    var height = 0.42f + (float)random.NextDouble() * 0.25f;
                    var crop = CreatePrimitive("Field crop clump", PrimitiveType.Sphere, field,
                        new Vector3(x, 0.24f + height * 0.42f, z),
                        new Vector3(0.34f, height, 0.38f), _leafMaterial);
                    crop.transform.localRotation = Quaternion.Euler(0f,
                        (float)random.NextDouble() * 180f, 0f);
                    crop.isStatic = true;
                }
            }
        }

        private static void BuildFieldTreeBelt(Transform root, Vector3 start, int count,
            bool alongX, string seedKey)
        {
            var random = new System.Random(StableSeed(seedKey));
            for (var i = 0; i < count; i++)
            {
                // Gaps keep the belt organic and preserve long views across the farmland.
                if (random.NextDouble() < 0.22) continue;
                var offset = i * 2.15f + (float)random.NextDouble() * 0.8f;
                var position = start + (alongX
                    ? new Vector3(offset, 0f, (float)random.NextDouble() * 2.2f - 1.1f)
                    : new Vector3((float)random.NextDouble() * 2.2f - 1.1f, 0f, offset));
                BuildTree(root, position, 1.15f + (float)random.NextDouble() * 0.65f);
            }
        }

        private static void BuildDistantRuralHorizon(Transform root, string worldSeed)
        {
            var random = new System.Random(StableSeed(worldSeed + ":distant-horizon"));
            // Low-detail tree masses beyond the current 30 m server scene radius.
            // They are a visual horizon only, not traversable world or NPC destinations.
            for (var belt = 0; belt < 3; belt++)
            for (var i = 0; i < 24; i++)
            {
                var side = belt == 0 ? -1f : belt == 1 ? 1f : 0f;
                var x = side == 0f
                    ? -54f + i * 4.7f
                    : side * (43f + (float)random.NextDouble() * 5f);
                var z = side == 0f
                    ? 43f + (float)random.NextDouble() * 4f
                    : -48f + i * 4.1f;
                var scale = 2.2f + (float)random.NextDouble() * 1.5f;
                var tree = new GameObject("Distant woodland mass").transform;
                tree.SetParent(root, false);
                tree.position = new Vector3(x, 0f, z);
                tree.localScale = Vector3.one * scale;
                CreatePrimitive("Distant trunk", PrimitiveType.Cylinder, tree,
                    new Vector3(0f, 1.2f, 0f), new Vector3(0.13f, 1.2f, 0.13f), _woodMaterial);
                CreatePrimitive("Distant canopy", PrimitiveType.Sphere, tree,
                    new Vector3(0f, 3f, 0f), new Vector3(1.3f, 1.7f, 1.2f), _leafMaterial);
            }
        }

        private static void BuildFarmsteadAndOrchard(Transform root)
        {
            // A small cultivated field extends the village economy beyond the central garden.
            // All props are visual-only and deliberately use the existing ground plane.
            var field = new GameObject("Outlying crop field").transform;
            field.SetParent(root, false);
            field.position = new Vector3(12.5f, 0f, -14.5f);

            // The cultivated earth has a worn, irregular perimeter instead of a hard rectangular slab.
            // A shallow fan mesh keeps the soil flush with the meadow and remains presentation-only.
            const int fieldEdgeSegments = 4;
            const int fieldSides = fieldEdgeSegments * 4;
            var fieldVertices = new Vector3[fieldSides + 1];
            var fieldTriangles = new int[fieldSides * 3];
            fieldVertices[0] = new Vector3(0f, 0.035f, 0f);
            var fieldRandom = new System.Random(StableSeed("outer-field-soil"));
            for (var edge = 0; edge < 4; edge++)
            for (var segment = 0; segment < fieldEdgeSegments; segment++)
            {
                var t = segment / (float)fieldEdgeSegments;
                float x, z;
                if (edge == 0) { x = Mathf.Lerp(-3.9f, 3.9f, t); z = -2.8f; }
                else if (edge == 1) { x = 3.9f; z = Mathf.Lerp(-2.8f, 2.8f, t); }
                else if (edge == 2) { x = Mathf.Lerp(3.9f, -3.9f, t); z = 2.8f; }
                else { x = -3.9f; z = Mathf.Lerp(2.8f, -2.8f, t); }

                var inset = (float)fieldRandom.NextDouble() * 0.22f;
                x += (float)fieldRandom.NextDouble() * 0.18f - 0.09f;
                z += (float)fieldRandom.NextDouble() * 0.18f - 0.09f;
                if (edge == 0) z += inset;
                else if (edge == 1) x -= inset;
                else if (edge == 2) z -= inset;
                else x += inset;
                fieldVertices[1 + edge * fieldEdgeSegments + segment] = new Vector3(x, 0.035f, z);
            }

            for (var i = 0; i < fieldSides; i++)
            {
                var triangle = i * 3;
                fieldTriangles[triangle] = 0;
                fieldTriangles[triangle + 1] = (i + 1) % fieldSides + 1;
                fieldTriangles[triangle + 2] = i + 1;
            }

            var fieldSoilMesh = new Mesh { name = "Cultivated field • worn soil edge" };
            fieldSoilMesh.vertices = fieldVertices;
            fieldSoilMesh.triangles = fieldTriangles;
            fieldSoilMesh.RecalculateNormals();
            fieldSoilMesh.RecalculateBounds();
            var fieldSoil = CreateMeshPart("Field soil patch", field, fieldSoilMesh, _pathMaterial);
            fieldSoil.isStatic = true;
            for (var row = 0; row < 5; row++)
            {
                var z = -2.0f + row * 1.0f;
                CreatePrimitive("Raised crop ridge", PrimitiveType.Cube, field,
                    new Vector3(0f, 0.13f, z), new Vector3(6.8f, 0.16f, 0.52f), _groundMaterial);
                for (var plant = 0; plant < 9; plant++)
                {
                    var x = -3.0f + plant * 0.75f;
                    var height = 0.38f + ((row + plant) % 3) * 0.08f;
                    var yaw = (row * 31 + plant * 17) % 360;
                    var lean = ((row + plant) % 2 == 0 ? -1f : 1f) * (0.025f + (plant % 3) * 0.012f);
                    var stem = CreatePrimitive("Field crop stalk", PrimitiveType.Cylinder, field,
                        new Vector3(x + lean * 0.35f, 0.20f + height * 0.5f, z),
                        new Vector3(0.026f, height * 0.5f, 0.026f), _leafMaterial);
                    stem.transform.localRotation = Quaternion.Euler(0f, 0f, lean * 32f);
                    stem.isStatic = true;

                    // Two slim blades break up the old round crop silhouettes.
                    for (var blade = 0; blade < 2; blade++)
                    {
                        var side = blade == 0 ? -1f : 1f;
                        var bladeHeight = height * (blade == 0 ? 0.38f : 0.58f);
                        var bladeObject = CreatePrimitive("Field crop leaf", PrimitiveType.Sphere, field,
                            new Vector3(x + lean * 0.35f + side * 0.075f, 0.20f + bladeHeight, z),
                            new Vector3(0.17f, 0.035f, 0.045f), _leafMaterial);
                        bladeObject.transform.localRotation = Quaternion.Euler(0f, yaw + side * 22f,
                            side * (blade == 0 ? -28f : 34f));
                        bladeObject.isStatic = true;
                    }

                    // Only a portion of the stalks carry ripe seed heads, so the field reads as unevenly mature.
                    if ((row + plant) % 4 == 0)
                    {
                        var headPosition = new Vector3(x + lean, 0.20f + height + 0.06f, z);
                        var head = CreatePrimitive("Ripened grain head", PrimitiveType.Sphere, field,
                            headPosition, new Vector3(0.075f, 0.19f, 0.075f), _produceMaterial);
                        head.transform.localRotation = Quaternion.Euler(0f, yaw, lean * 18f);
                        head.isStatic = true;
                        for (var grain = 0; grain < 3; grain++)
                        {
                            var offset = (grain - 1) * 0.055f;
                            var kernel = CreatePrimitive("Grain kernel", PrimitiveType.Sphere, field,
                                headPosition + new Vector3(offset, -Mathf.Abs(grain - 1) * 0.025f, 0f),
                                new Vector3(0.055f, 0.075f, 0.055f), _produceMaterial);
                            kernel.transform.localRotation = Quaternion.Euler(0f, yaw, (grain - 1) * 18f);
                            kernel.isStatic = true;
                        }
                    }
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

                // Layer several offset foliage masses to avoid a single round lollipop crown.
                var crown = CreatePrimitive("Fruit canopy • main crown", PrimitiveType.Sphere, tree,
                    new Vector3(0f, 2.38f, 0f), new Vector3(1.22f, 1.12f, 1.18f), _leafMaterial);
                crown.isStatic = true;
                for (var lobe = 0; lobe < 3; lobe++)
                {
                    var angle = lobe * Mathf.PI * 2f / 3f + i * 0.41f;
                    var lobePosition = new Vector3(Mathf.Cos(angle) * 0.68f,
                        2.42f + ((lobe + i) % 2) * 0.18f, Mathf.Sin(angle) * 0.68f);
                    var foliage = CreatePrimitive("Fruit canopy • foliage lobe", PrimitiveType.Sphere, tree,
                        lobePosition, new Vector3(0.92f, 0.86f, 0.90f), _leafMaterial);
                    foliage.isStatic = true;
                }
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
                // Build a small leafy herb instead of a single placeholder sphere.
                // Fixed row/column offsets keep the patch deterministic across clients.
                var herbHeight = 0.22f + ((row + col) % 3) * 0.035f;
                var stem = CreatePrimitive("Kitchen herb stem", PrimitiveType.Cylinder, kitchenGarden,
                    new Vector3(x, 0.20f + herbHeight * 0.5f, z),
                    new Vector3(0.022f, herbHeight * 0.5f, 0.022f), _leafMaterial);
                stem.isStatic = true;
                for (var leaf = 0; leaf < 4; leaf++)
                {
                    var angle = leaf * 90f + ((row * 37 + col * 23) % 45);
                    var radians = angle * Mathf.Deg2Rad;
                    var height = leaf < 2 ? 0.34f : 0.42f;
                    var offset = new Vector3(Mathf.Cos(radians) * 0.095f, 0f,
                        Mathf.Sin(radians) * 0.095f);
                    var leafObject = CreatePrimitive("Kitchen herb leaf", PrimitiveType.Sphere, kitchenGarden,
                        new Vector3(x + offset.x, height + herbHeight * (leaf == 3 ? 0.12f : 0f),
                            z + offset.z),
                        new Vector3(0.14f, 0.055f, 0.085f), _leafMaterial);
                    leafObject.transform.localRotation = Quaternion.Euler(0f, angle, (leaf % 2 == 0 ? -24f : 24f));
                    leafObject.isStatic = true;
                }
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
            dock.position = new Vector3(13.45f, 0.10f, -76f);
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
            CreatePoiMarker(root, "Река • рыбачить", new Vector3(13.45f, 0.08f, -76f),
                new Color(0.28f, 0.53f, 0.62f), null, "fish");
        }

        private static void CreatePoiMarker(Transform root, string title, Vector3 position, Color color, string goal, string action = null)
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
            poi.Initialize(title, goal, renderer, color, action);
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

            // TextMesh is a flat surface. A single face can appear mirrored when viewed
            // from behind, so place a correctly oriented inscription on each side of the board.
            CreateSignFace(parent, title, position + new Vector3(0f, 0f, -0.07f), 180f, "front");
            CreateSignFace(parent, title, position + new Vector3(0f, 0f, 0.07f), 0f, "back");
        }

        private static void CreateSignFace(Transform parent, string title, Vector3 localPosition,
            float yaw, string faceName)
        {
            var label = new GameObject("Sign text • " + faceName).transform;
            label.SetParent(parent, false);
            label.localPosition = localPosition;
            label.localRotation = Quaternion.Euler(0f, yaw, 0f);
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

            // Keep the crown on a pivot above the trunk so a light breeze moves foliage,
            // not the whole tree or its collision-free trunk silhouette.
            var crown = new GameObject("Willow crown • wind pivot").transform;
            crown.SetParent(tree, false);
            crown.localPosition = new Vector3(0f, 2.25f, 0f);
            CreatePrimitive("Canopy", PrimitiveType.Sphere, crown, new Vector3(0, 0.30f, 0),
                new Vector3(2.0f, 1.8f, 1.9f), _leafMaterial);
            for (var i = 0; i < 5; i++)
            {
                var angle = i * Mathf.PI * 2f / 5f;
                CreatePrimitive("Hanging foliage", PrimitiveType.Sphere, crown,
                    new Vector3(Mathf.Cos(angle) * 0.72f, -0.60f, Mathf.Sin(angle) * 0.72f),
                    new Vector3(0.45f, 1.25f, 0.45f), _leafMaterial);
            }

            var ambient = root.GetComponent<VillageAmbientMotion>();
            if (ambient != null)
                ambient.RegisterSway(crown, 1.1f + scale * 0.35f, 0.62f + scale * 0.12f);
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


        private static void BuildBarnFenceAndGate(Transform root, Vector3 position)
        {
            var fence = new GameObject("Barn yard fence with open gate").transform;
            fence.SetParent(root, false);
            fence.localPosition = position;

            // The fence stops at a real opening aligned with the barn doors.
            for (var i = 0; i <= 8; i++)
            {
                var x = (i - 4f) * 0.65f;
                if (Mathf.Abs(x) < 0.64f) continue;
                var post = CreatePrimitive("Barn yard fence • upright post", PrimitiveType.Cube, fence,
                    new Vector3(x, 0.55f, 0f), new Vector3(0.13f, 1.1f, 0.13f), _woodMaterial);
                post.isStatic = true;
            }

            for (var side = -1; side <= 1; side += 2)
            {
                var rail = CreatePrimitive("Barn yard fence • upper rail", PrimitiveType.Cube, fence,
                    new Vector3(side * 1.625f, 0.78f, 0f), new Vector3(1.95f, 0.12f, 0.12f), _woodMaterial);
                rail.isStatic = true;
                var lowerRail = CreatePrimitive("Barn yard fence • lower rail", PrimitiveType.Cube, fence,
                    new Vector3(side * 1.625f, 0.35f, 0f), new Vector3(1.95f, 0.12f, 0.12f), _woodMaterial);
                lowerRail.isStatic = true;
            }

            // Leave the gate ajar, as if a farm worker has just led a cart through.
            var gate = new GameObject("Barn yard • open timber gate").transform;
            gate.SetParent(fence, false);
            gate.localPosition = new Vector3(-0.65f, 0f, 0f);
            gate.localRotation = Quaternion.Euler(0f, -55f, 0f);
            // The hinge side reuses the fence's end post; only the free gate edge needs its own upright.
            var gateFreeUpright = CreatePrimitive("Gate • free upright", PrimitiveType.Cube, gate,
                new Vector3(1.30f, 0.55f, 0f), new Vector3(0.13f, 1.1f, 0.13f), _roofMaterial);
            gateFreeUpright.isStatic = true;

            var gateTop = CreatePrimitive("Gate • upper cross rail", PrimitiveType.Cube, gate,
                new Vector3(0.65f, 0.82f, 0f), new Vector3(1.30f, 0.13f, 0.12f), _woodMaterial);
            gateTop.isStatic = true;
            var gateBottom = CreatePrimitive("Gate • lower cross rail", PrimitiveType.Cube, gate,
                new Vector3(0.65f, 0.34f, 0f), new Vector3(1.30f, 0.13f, 0.12f), _woodMaterial);
            gateBottom.isStatic = true;
            var gateBrace = CreatePrimitive("Gate • diagonal brace", PrimitiveType.Cube, gate,
                new Vector3(0.65f, 0.58f, 0f), new Vector3(1.15f, 0.10f, 0.10f), _roofMaterial);
            gateBrace.transform.localRotation = Quaternion.Euler(0f, 0f, -24f);
            gateBrace.isStatic = true;

            for (var hinge = -1; hinge <= 1; hinge += 2)
            {
                var strap = CreatePrimitive("Gate • iron hinge strap", PrimitiveType.Cube, gate,
                    new Vector3(0.08f, 0.55f + hinge * 0.25f, -0.075f),
                    new Vector3(0.34f, 0.055f, 0.035f), _metalMaterial);
                strap.isStatic = true;
            }
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

        private static void SetMaterialSurface(Material material, float metallic, float smoothness)
        {
            if (material == null) return;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        }

        private static void SetMaterialEmission(Material material, Color emissionColor, float intensity)
        {
            if (material == null || !material.HasProperty("_EmissionColor")) return;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emissionColor * Mathf.Max(0f, intensity));
        }

        private static Mesh BuildFireMesh(bool inner)
        {
            var vertices = new System.Collections.Generic.List<Vector3>(inner ? 22 : 33);
            var triangles = new System.Collections.Generic.List<int>(inner ? 90 : 135);

            if (inner)
            {
                AddFireTongue(vertices, triangles, -0.045f, -0.018f, 0.074f, 0.052f, 0.43f, 0.018f);
                AddFireTongue(vertices, triangles, 0.043f, 0.012f, 0.070f, 0.050f, 0.49f, -0.014f);
            }
            else
            {
                AddFireTongue(vertices, triangles, -0.112f, 0.012f, 0.118f, 0.078f, 0.56f, -0.026f);
                AddFireTongue(vertices, triangles, 0.000f, -0.018f, 0.145f, 0.092f, 0.72f, 0.035f);
                AddFireTongue(vertices, triangles, 0.116f, 0.020f, 0.108f, 0.074f, 0.58f, 0.022f);
            }

            var mesh = new Mesh { name = inner ? "Low-poly golden fire core" : "Low-poly orange fire tongues" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFireTongue(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles,
            float centerX, float centerZ, float width, float depth, float height, float leanX)
        {
            const int sides = 5;
            var lower = vertices.Count;
            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(centerX + Mathf.Cos(angle) * width, 0f,
                    centerZ + Mathf.Sin(angle) * depth));
            }

            var upper = vertices.Count;
            var upperCenterX = centerX + leanX * 0.45f;
            var upperCenterZ = centerZ - depth * 0.08f;
            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(upperCenterX + Mathf.Cos(angle) * width * 0.52f,
                    height * 0.56f, upperCenterZ + Mathf.Sin(angle) * depth * 0.56f));
            }

            var tip = vertices.Count;
            vertices.Add(new Vector3(centerX + leanX, height, centerZ - depth * 0.12f));
            for (var i = 0; i < sides; i++)
            {
                var next = (i + 1) % sides;
                // Outward-facing lower facets and upper tip facets.
                triangles.Add(lower + i); triangles.Add(upper + i); triangles.Add(lower + next);
                triangles.Add(lower + next); triangles.Add(upper + i); triangles.Add(upper + next);
                triangles.Add(upper + i); triangles.Add(tip); triangles.Add(upper + next);
            }
        }

        public static void Cleanup()
        {
            DestroyAsset(_groundMaterial);
            DestroyAsset(_pathMaterial);
            DestroyAsset(_roadRutMaterial);
            DestroyAsset(_woodMaterial);
            DestroyAsset(_roofMaterial);
            DestroyAsset(_stoneMaterial);
            DestroyAsset(_leafMaterial);
            DestroyAsset(_grassMaterial);
            DestroyAsset(_beerMaterial);
            DestroyAsset(_foamMaterial);
            DestroyAsset(_fireMaterial);
            DestroyAsset(_fireCoreMaterial);
            DestroyAsset(_metalMaterial);
            DestroyAsset(_clothMaterial);
            DestroyAsset(_produceMaterial);
            DestroyAsset(_mugWoodMaterial);
            DestroyAsset(_mugBandMaterial);
            DestroyAsset(_bottleMaterial);
            DestroyAsset(_corkMaterial);
            DestroyAsset(_labelMaterial);
            DestroyAsset(_waterMaterial);
            DestroyAsset(_smokeMaterial);
            for (var i = 0; i < _generatedMaterials.Count; i++)
                DestroyAsset(_generatedMaterials[i]);
            _generatedMaterials.Clear();
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
            _roadRutMaterial = null;
            _woodMaterial = null;
            _roofMaterial = null;
            _stoneMaterial = null;
            _leafMaterial = null;
            _grassMaterial = null;
            _beerMaterial = null;
            _foamMaterial = null;
            _fireMaterial = null;
            _fireCoreMaterial = null;
            _metalMaterial = null;
            _clothMaterial = null;
            _produceMaterial = null;
            _mugWoodMaterial = null;
            _mugBandMaterial = null;
            _bottleMaterial = null;
            _corkMaterial = null;
            _labelMaterial = null;
            _waterMaterial = null;
            _smokeMaterial = null;
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