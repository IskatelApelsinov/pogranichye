using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Builds a broad, lightweight presentation environment around the starter village.
    /// All geometry is local client scenery; it is not world-authoritative or collidable.
    /// </summary>
    public static class WorldEnvironmentBuilder
    {
        private static Material _grassMaterial;
        private static Material _starMaterial;
        private static Material _skyMaterial;
        private static Material _cloudMaterial;
        private static Material _forestBarkMaterial;
        private static Material _forestLeafMaterial;
        private static Material _groundDetailMaterial;
        private static Material _moteMaterial;
        private static Material _mistMaterial;
        private static Mesh _forestMesh;
        private static Mesh _groundDetailMesh;
        private static Mesh _flowerMesh;
        private static Mesh _starMesh;
        private static Mesh _grassMesh;
        private static Mesh _ruralFieldMesh;
        private static readonly System.Collections.Generic.List<Material> _landscapeMaterials = new System.Collections.Generic.List<Material>();

        public static void Build(Transform root, Camera camera)
        {
            if (root == null || root.Find("WorldEnvironment") != null) return;

            var environment = new GameObject("WorldEnvironment").transform;
            environment.SetParent(root, false);
            BuildSky(environment, camera);
            BuildDistantLandscape(environment);
            BuildClouds(environment);
            BuildGrass(environment);
            BuildForest(environment);
            BuildRuralFields(environment);
            BuildGroundDetails(environment);
            BuildAmbientMotes(environment);
            BuildLowlandMist(environment);
            var cycle = environment.gameObject.AddComponent<WorldAtmosphereCycle>();
            cycle.Initialize(_skyMaterial, _starMaterial, _moteMaterial, _mistMaterial, FindSun());
        }

        private static void BuildSky(Transform root, Camera camera)
        {
            if (camera != null) camera.clearFlags = CameraClearFlags.Skybox;

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                _skyMaterial = new Material(skyShader) { name = "Pogranichye • procedural sky" };
                _skyMaterial.SetFloat("_SunSize", 0.035f);
                _skyMaterial.SetFloat("_AtmosphereThickness", 1.15f);
                _skyMaterial.SetColor("_SkyTint", new Color(0.72f, 0.82f, 0.92f));
                _skyMaterial.SetColor("_GroundColor", new Color(0.34f, 0.37f, 0.31f));
                _skyMaterial.SetFloat("_Exposure", 1.1f);
                RenderSettings.skybox = _skyMaterial;
                RenderSettings.sun = FindSun();
            }

            _starMaterial = CreateTransparentMaterial(
                Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"),
                "Distant stars", new Color(0.78f, 0.86f, 1f, 0f));
            if (_starMaterial == null) return;

            _starMesh = new Mesh { name = "Procedural star field" };
            var vertices = new Vector3[420 * 4];
            var triangles = new int[420 * 6];
            var random = new System.Random(7919);
            for (var i = 0; i < 420; i++)
            {
                var azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
                var elevation = Mathf.Lerp(0.12f, 1.48f, (float)random.NextDouble());
                var direction = new Vector3(
                    Mathf.Cos(azimuth) * Mathf.Cos(elevation),
                    Mathf.Sin(elevation),
                    Mathf.Sin(azimuth) * Mathf.Cos(elevation));
                var center = direction * 145f;
                var size = Mathf.Lerp(0.10f, 0.26f, (float)random.NextDouble());
                var right = Vector3.Cross(direction, Vector3.up).normalized * size;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right * size;
                var up = Vector3.Cross(direction, right).normalized * size;
                var v = i * 4;
                vertices[v] = center - right - up;
                vertices[v + 1] = center - right + up;
                vertices[v + 2] = center + right + up;
                vertices[v + 3] = center + right - up;
                var t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            _starMesh.vertices = vertices;
            _starMesh.triangles = triangles;
            _starMesh.RecalculateBounds();
            var stars = new GameObject("Stars • high atmosphere");
            stars.transform.SetParent(root, false);
            var filter = stars.AddComponent<MeshFilter>();
            filter.sharedMesh = _starMesh;
            var renderer = stars.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _starMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            stars.AddComponent<WorldSkyFollowCamera>();
        }

        private static void BuildDistantLandscape(Transform root)
        {
            // A faceted mountain ridge sits well beyond the 300×300 m playable area.
            var random = new System.Random(14027);
            var vertices = new System.Collections.Generic.List<Vector3>(2400);
            var paletteTriangles = new System.Collections.Generic.List<int>[5];
            for (var palette = 0; palette < paletteTriangles.Length; palette++)
                paletteTriangles[palette] = new System.Collections.Generic.List<int>(2400);

            for (var i = 0; i < 36; i++)
            {
                var angle = i * Mathf.PI * 2f / 36f;
                var distance = 255f + (float)random.NextDouble() * 50f;
                var height = 10f + (float)random.NextDouble() * 15f;
                var width = 22f + (float)random.NextDouble() * 12f;
                var center = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                AppendLowPolyHill(vertices, paletteTriangles[i % 5], center, width, width * 0.62f,
                    height, (float)random.NextDouble() * Mathf.PI * 2f, random);
            }

            // Low foothills form a second, distant layer without crowding the village or fields.
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Mathf.PI * 2f / 12f + 0.18f;
                var distance = 226f + (float)random.NextDouble() * 12f;
                var center = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                AppendLowPolyHill(vertices, paletteTriangles[(i + 41) % 5], center,
                    8.0f + (float)random.NextDouble() * 3.0f, 6.0f + (float)random.NextDouble() * 2.0f,
                    3.8f + (float)random.NextDouble() * 2.2f,
                    (float)random.NextDouble() * Mathf.PI * 2f, random);
            }

            var mesh = new Mesh { name = "Distant countryside • faceted ridgeline" };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.subMeshCount = paletteTriangles.Length;
            for (var i = 0; i < paletteTriangles.Length; i++)
                mesh.SetTriangles(paletteTriangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var landscape = new GameObject("Distant countryside • combined low-poly hills");
            landscape.transform.SetParent(root, false);
            landscape.isStatic = true;
            landscape.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = landscape.AddComponent<MeshRenderer>();
            var materials = new Material[paletteTriangles.Length];
            for (var i = 0; i < materials.Length; i++) materials[i] = CreateLandscapeMaterial(i);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void AppendLowPolyHill(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles,
            Vector3 center, float radiusX, float radiusZ, float height, float rotation,
            System.Random random)
        {
            const int sides = 8;
            var radii = new[] { 1f, 0.78f, 0.52f, 0.26f, 0.07f };
            var heights = new[] { 0.025f, 0.13f, 0.39f, 0.73f, 0.96f };
            var start = vertices.Count;
            for (var ring = 0; ring < radii.Length; ring++)
            for (var side = 0; side < sides; side++)
            {
                var angle = rotation + side * Mathf.PI * 2f / sides;
                var irregular = 1f + Mathf.Sin(side * 2.31f + ring * 1.7f) * 0.08f
                    + ((float)random.NextDouble() - 0.5f) * 0.06f;
                vertices.Add(center + new Vector3(
                    Mathf.Cos(angle) * radiusX * radii[ring] * irregular,
                    height * heights[ring],
                    Mathf.Sin(angle) * radiusZ * radii[ring] * irregular));
            }

            for (var ring = 0; ring < radii.Length - 1; ring++)
            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                var lower = start + ring * sides + side;
                var lowerNext = start + ring * sides + next;
                var upper = start + (ring + 1) * sides + side;
                var upperNext = start + (ring + 1) * sides + next;
                triangles.Add(lower); triangles.Add(upper); triangles.Add(lowerNext);
                triangles.Add(lowerNext); triangles.Add(upper); triangles.Add(upperNext);
            }

            var peak = vertices.Count;
            vertices.Add(center + Vector3.up * (height * 1.04f));
            var lastRing = start + (radii.Length - 1) * sides;
            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                triangles.Add(lastRing + side);
                triangles.Add(peak);
                triangles.Add(lastRing + next);
            }
        }

        private static Material CreateLandscapeMaterial(int seed)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var index = Mathf.Abs(seed % 5);
            while (_landscapeMaterials.Count <= index)
            {
                var paletteIndex = _landscapeMaterials.Count;
                var material = new Material(shader) { name = "Far hills • palette " + paletteIndex };
                var variation = paletteIndex * 0.018f;
                material.color = new Color(0.24f + variation, 0.32f + variation, 0.25f + variation);
                _landscapeMaterials.Add(material);
            }
            return _landscapeMaterials[index];
        }

        private static void BuildClouds(Transform root)
        {
            var cloudShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _cloudMaterial = new Material(cloudShader)
            {
                name = "High cloud • soft ivory",
                color = new Color(0.82f, 0.86f, 0.88f)
            };
            var random = new System.Random(551);
            for (var i = 0; i < 9; i++)
            {
                var cloud = new GameObject("Cloud bank • drifting").transform;
                cloud.SetParent(root, false);
                var angle = i * Mathf.PI * 2f / 9f;
                var radius = 48f + (float)random.NextDouble() * 34f;
                cloud.position = new Vector3(Mathf.Cos(angle) * radius, 23f + (float)random.NextDouble() * 10f,
                    Mathf.Sin(angle) * radius);
                cloud.localScale = Vector3.one * (1.2f + (float)random.NextDouble() * 1.3f);
                var puffs = 4 + random.Next(4);
                for (var p = 0; p < puffs; p++)
                {
                    var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    puff.name = "Cloud puff";
                    puff.transform.SetParent(cloud, false);
                    puff.transform.localPosition = new Vector3(p * 1.15f - puffs * 0.55f,
                        (float)random.NextDouble() * 0.35f, (float)random.NextDouble() * 0.6f - 0.3f);
                    puff.transform.localScale = new Vector3(2.2f + (float)random.NextDouble(),
                        0.65f + (float)random.NextDouble() * 0.45f, 1.4f + (float)random.NextDouble());
                    var collider = puff.GetComponent<Collider>();
                    if (collider != null) UnityEngine.Object.Destroy(collider);
                    puff.GetComponent<Renderer>().sharedMaterial = _cloudMaterial;
                }
                cloud.gameObject.AddComponent<WorldCloudDrift>().Initialize(0.22f + (float)random.NextDouble() * 0.18f);
            }
        }

        private static void BuildGrass(Transform root)
        {
            var random = new System.Random(48271);
            var vertices = new System.Collections.Generic.List<Vector3>(15000);
            var triangles = new System.Collections.Generic.List<int>(15000);
            for (var i = 0; i < 1250; i++)
            {
                var x = (float)random.NextDouble() * 50f - 25f;
                var z = (float)random.NextDouble() * 50f - 25f;
                if (Mathf.Abs(x) < 1.55f || Mathf.Abs(z) < 1.25f) continue;
                if (NearFootprint(x, z, -6f, 7f, 2.5f, 2.1f) ||
                    NearFootprint(x, z, 7f, 4f, 2.5f, 2.0f) ||
                    NearFootprint(x, z, -8f, -5f, 2.5f, 2.0f) ||
                    NearFootprint(x, z, 1.5f, 2.5f, 3.4f, 2.8f) ||
                    NearFootprint(x, z, 9f, -4.5f, 2.4f, 2.0f) ||
                    NearFootprint(x, z, -4.5f, -1.5f, 2.2f, 1.8f) ||
                    NearFootprint(x, z, 6f, -9f, 2.8f, 2.0f) ||
                    NearFootprint(x, z, 3.2f, -2.4f, 1.0f, 1.0f) ||
                    NearFootprint(x, z, -20f, -13f, 3.2f, 3.0f) ||
                    NearFootprint(x, z, -22f, 13f, 2.0f, 2.0f) ||
                    NearFootprint(x, z, -13f, -3.5f, 3.0f, 2.8f) ||
                    NearFootprint(x, z, -1.5f, -10.2f, 1.8f, 1.2f) ||
                    NearFootprint(x, z, 13.45f, 7f, 1.8f, 2.0f) ||
                    Mathf.Abs(x - 15.5f) < 1.8f) continue;
                var height = 0.18f + (float)random.NextDouble() * 0.30f;
                var width = 0.035f + (float)random.NextDouble() * 0.035f;
                var yaw = (float)random.NextDouble() * Mathf.PI * 2f;
                for (var blade = 0; blade < 3; blade++)
                {
                    var angle = yaw + blade * Mathf.PI / 3f;
                    var side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * width;
                    var lean = new Vector3(Mathf.Cos(angle + 1.1f), 0f, Mathf.Sin(angle + 1.1f)) * height * 0.22f;
                    var origin = new Vector3(x, 0.035f, z);
                    var first = vertices.Count;
                    vertices.Add(origin - side);
                    vertices.Add(origin + side);
                    vertices.Add(origin + lean + Vector3.up * height);
                    triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
                }
            }

            _grassMesh = new Mesh { name = "Meadow grass blades" };
            _grassMesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _grassMesh.SetVertices(vertices);
            _grassMesh.SetTriangles(triangles, 0);
            _grassMesh.RecalculateNormals();
            _grassMesh.RecalculateBounds();

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _grassMaterial = new Material(shader) { name = "Meadow grass • deep green", color = new Color(0.35f, 0.46f, 0.23f) };
            if (_grassMaterial.HasProperty("_Cull")) _grassMaterial.SetInt("_Cull", 0);
            var grass = new GameObject("Meadow grass • dense field detail");
            grass.transform.SetParent(root, false);
            grass.AddComponent<MeshFilter>().sharedMesh = _grassMesh;
            var grassRenderer = grass.AddComponent<MeshRenderer>();
            grassRenderer.sharedMaterial = _grassMaterial;
            grassRenderer.shadowCastingMode = ShadowCastingMode.Off;
            grass.AddComponent<MeadowGrassWind>().Initialize(_grassMesh);
        }

        private static bool NearFootprint(float x, float z, float centerX, float centerZ, float radiusX, float radiusZ)
        {
            return Mathf.Abs(x - centerX) < radiusX && Mathf.Abs(z - centerZ) < radiusZ;
        }


        /// <summary>Low-poly tree belt that frames the playable settlement and suggests a larger biome beyond it.</summary>
        private static void BuildForest(Transform root)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;
            _forestBarkMaterial = new Material(shader) { name = "Forest bark • warm umber", color = new Color(0.24f, 0.15f, 0.09f) };
            _forestLeafMaterial = new Material(shader) { name = "Forest canopy • deep evergreen", color = new Color(0.20f, 0.34f, 0.19f) };
            if (_forestLeafMaterial.HasProperty("_Cull")) _forestLeafMaterial.SetInt("_Cull", 0);

            var vertices = new System.Collections.Generic.List<Vector3>(18000);
            var bark = new System.Collections.Generic.List<int>(9000);
            var leaves = new System.Collections.Generic.List<int>(12000);
            var random = new System.Random(90210);
            for (var i = 0; i < 78; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = 29f + (float)random.NextDouble() * 29f;
                var center = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var height = 6.5f + (float)random.NextDouble() * 5.5f;
                var width = 0.55f + (float)random.NextDouble() * 0.55f;
                var yaw = (float)random.NextDouble() * Mathf.PI * 2f;
                AppendTree(vertices, bark, leaves, center, height, width, yaw, random.NextDouble() < 0.42);
            }

            // Shrubs soften the transition from open meadow to the tree belt.
            for (var i = 0; i < 34; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = 19f + (float)random.NextDouble() * 10f;
                var center = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Mathf.Abs(center.x) < 3.2f || Mathf.Abs(center.z) < 3.2f) continue;
                var size = 0.65f + (float)random.NextDouble() * 0.55f;
                AppendCanopyBlob(vertices, leaves, center + Vector3.up * (size * 0.72f), size, size * 0.78f, size, (float)random.NextDouble() * Mathf.PI * 2f);
            }

            _forestMesh = new Mesh { name = "Outer woodland • combined low-poly trees" };
            _forestMesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _forestMesh.SetVertices(vertices);
            _forestMesh.subMeshCount = 2;
            _forestMesh.SetTriangles(bark, 0);
            _forestMesh.SetTriangles(leaves, 1);
            _forestMesh.RecalculateNormals();
            _forestMesh.RecalculateBounds();

            var woodland = new GameObject("Woodland belt • distant tree clusters");
            woodland.transform.SetParent(root, false);
            woodland.AddComponent<MeshFilter>().sharedMesh = _forestMesh;
            var renderer = woodland.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { _forestBarkMaterial, _forestLeafMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void AppendTree(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> bark, System.Collections.Generic.List<int> leaves,
            Vector3 center, float height, float width, float yaw, bool broadleaf)
        {
            const int sides = 7;
            var trunkStart = vertices.Count;
            for (var ring = 0; ring < 2; ring++)
            {
                var radius = ring == 0 ? width * 0.34f : width * 0.20f;
                var y = ring == 0 ? 0.05f : height * 0.56f;
                for (var side = 0; side < sides; side++)
                {
                    var a = yaw + side * Mathf.PI * 2f / sides;
                    vertices.Add(center + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                }
            }
            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                bark.Add(trunkStart + side); bark.Add(trunkStart + sides + side); bark.Add(trunkStart + next);
                bark.Add(trunkStart + next); bark.Add(trunkStart + sides + side); bark.Add(trunkStart + sides + next);
            }

            if (broadleaf)
            {
                var crownY = height * 0.68f;
                AppendCanopyBlob(vertices, leaves, center + Vector3.up * crownY, width * 1.55f, height * 0.23f, width * 1.42f, yaw);
                AppendCanopyBlob(vertices, leaves, center + new Vector3(width * 0.82f, crownY - height * 0.06f, 0f), width * 0.92f, height * 0.19f, width * 0.94f, yaw + 0.4f);
                AppendCanopyBlob(vertices, leaves, center + new Vector3(-width * 0.78f, crownY - height * 0.04f, width * 0.18f), width * 0.88f, height * 0.18f, width * 0.92f, yaw + 1.1f);
                AppendCanopyBlob(vertices, leaves, center + new Vector3(0f, crownY + height * 0.12f, -width * 0.42f), width * 0.88f, height * 0.17f, width * 0.90f, yaw + 0.8f);
            }
            else
            {
                AppendCanopy(vertices, leaves, center + Vector3.up * (height * 0.27f), width * 1.75f, height * 0.43f, yaw);
                AppendCanopy(vertices, leaves, center + Vector3.up * (height * 0.49f), width * 1.42f, height * 0.37f, yaw + 0.17f);
                AppendCanopy(vertices, leaves, center + Vector3.up * (height * 0.70f), width * 1.08f, height * 0.34f, yaw + 0.31f);
            }
        }

        private static void AppendCanopyBlob(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles, Vector3 center, float radiusX, float radiusY, float radiusZ, float yaw)
        {
            var start = vertices.Count;
            var sin = Mathf.Sin(yaw);
            var cos = Mathf.Cos(yaw);
            vertices.Add(center + Vector3.up * radiusY);
            vertices.Add(center - Vector3.up * radiusY);
            vertices.Add(center + new Vector3(cos * radiusX, 0f, sin * radiusX));
            vertices.Add(center + new Vector3(-cos * radiusX, 0f, -sin * radiusX));
            vertices.Add(center + new Vector3(-sin * radiusZ, 0f, cos * radiusZ));
            vertices.Add(center + new Vector3(sin * radiusZ, 0f, -cos * radiusZ));
            var faces = new int[] { 0,2,4, 0,4,3, 0,3,5, 0,5,2, 1,4,2, 1,3,4, 1,5,3, 1,2,5 };
            for (var i = 0; i < faces.Length; i += 3)
            {
                triangles.Add(start + faces[i]);
                triangles.Add(start + faces[i + 1]);
                triangles.Add(start + faces[i + 2]);
            }
        }

        private static void AppendCanopy(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles, Vector3 center, float radius, float height, float yaw)
        {
            const int sides = 7;
            var start = vertices.Count;
            vertices.Add(center + Vector3.up * height);
            for (var side = 0; side < sides; side++)
            {
                var a = yaw + side * Mathf.PI * 2f / sides;
                vertices.Add(center + new Vector3(Mathf.Cos(a) * radius, -height * 0.48f, Mathf.Sin(a) * radius));
            }
            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                triangles.Add(start); triangles.Add(start + 1 + next); triangles.Add(start + 1 + side);
            }
        }

        /// <summary>
        /// Adds presentation-only cultivated plots beyond the village tree belt.
        /// These are visual scenery only: no colliders, ownership, crops or server state.
        /// </summary>
        private static void BuildRuralFields(Transform root)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            var soilMaterial = new Material(shader) { name = "Rural plots • worked soil", color = new Color(0.34f, 0.29f, 0.20f) };
            var furrowMaterial = new Material(shader) { name = "Rural plots • furrows", color = new Color(0.22f, 0.19f, 0.14f) };
            var cropMaterial = new Material(shader) { name = "Rural plots • young crops", color = new Color(0.43f, 0.48f, 0.24f) };
            if (cropMaterial.HasProperty("_Cull")) cropMaterial.SetInt("_Cull", 0);
            _landscapeMaterials.Add(soilMaterial);
            _landscapeMaterials.Add(furrowMaterial);
            _landscapeMaterials.Add(cropMaterial);

            var soilVertices = new System.Collections.Generic.List<Vector3>(256);
            var soilTriangles = new System.Collections.Generic.List<int>(384);
            var furrowVertices = new System.Collections.Generic.List<Vector3>(1024);
            var furrowTriangles = new System.Collections.Generic.List<int>(1536);
            var cropVertices = new System.Collections.Generic.List<Vector3>(4096);
            var cropTriangles = new System.Collections.Generic.List<int>(6144);
            var random = new System.Random(88421);

            // Eight small plots form a loose countryside ring, not a second settlement.
            for (var plot = 0; plot < 8; plot++)
            {
                var angle = (22.5f + plot * 45f + ((float)random.NextDouble() - 0.5f) * 8f) * Mathf.Deg2Rad;
                var radius = 82f + (float)random.NextDouble() * 48f;
                var center = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var halfWidth = 5.5f + (float)random.NextDouble() * 2.0f;
                var halfLength = 8.5f + (float)random.NextDouble() * 4.0f;
                var yaw = angle + Mathf.PI * 0.5f + ((float)random.NextDouble() - 0.5f) * 0.5f;
                var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var side = new Vector3(forward.z, 0f, -forward.x);
                var cornerNoise = 0.55f + (float)random.NextDouble() * 0.45f;
                var p0 = center - side * (halfWidth * cornerNoise) - forward * halfLength;
                var p1 = center + side * halfWidth - forward * (halfLength * 0.92f);
                var p2 = center + side * (halfWidth * 0.92f) + forward * halfLength;
                var p3 = center - side * halfWidth + forward * (halfLength * cornerNoise);
                var baseIndex = soilVertices.Count;
                soilVertices.Add(p0 + Vector3.up * 0.018f);
                soilVertices.Add(p1 + Vector3.up * 0.018f);
                soilVertices.Add(p2 + Vector3.up * 0.018f);
                soilVertices.Add(p3 + Vector3.up * 0.018f);
                soilTriangles.Add(baseIndex); soilTriangles.Add(baseIndex + 2); soilTriangles.Add(baseIndex + 1);
                soilTriangles.Add(baseIndex); soilTriangles.Add(baseIndex + 3); soilTriangles.Add(baseIndex + 2);

                const int rows = 7;
                for (var row = 0; row < rows; row++)
                {
                    var lateral = Mathf.Lerp(-halfWidth * 0.78f, halfWidth * 0.78f, row / (float)(rows - 1));
                    var rowStart = center + side * lateral - forward * (halfLength * 0.78f);
                    var rowEnd = center + side * lateral + forward * (halfLength * 0.78f);
                    AppendRuralStrip(furrowVertices, furrowTriangles, rowStart, rowEnd, side * 0.12f, 0.045f);
                    for (var plant = 0; plant < 8; plant++)
                    {
                        var t = (plant + 0.25f + (float)random.NextDouble() * 0.5f) / 8f;
                        var basePoint = Vector3.Lerp(rowStart, rowEnd, t) + Vector3.up * 0.055f;
                        var height = 0.24f + (float)random.NextDouble() * 0.18f;
                        var bladeWidth = 0.055f + (float)random.NextDouble() * 0.025f;
                        var lean = side * (((float)random.NextDouble() - 0.5f) * 0.13f);
                        var first = cropVertices.Count;
                        cropVertices.Add(basePoint - side * bladeWidth);
                        cropVertices.Add(basePoint + side * bladeWidth);
                        cropVertices.Add(basePoint + Vector3.up * height + lean);
                        cropTriangles.Add(first); cropTriangles.Add(first + 2); cropTriangles.Add(first + 1);
                    }
                }
            }

            var allVertices = new System.Collections.Generic.List<Vector3>(
                soilVertices.Count + furrowVertices.Count + cropVertices.Count);
            allVertices.AddRange(soilVertices);
            allVertices.AddRange(furrowVertices);
            allVertices.AddRange(cropVertices);
            var allTriangles = new[] { soilTriangles, furrowTriangles, cropTriangles };
            var offsets = new[] { 0, soilVertices.Count, soilVertices.Count + furrowVertices.Count };
            _ruralFieldMesh = new Mesh { name = "Rural farmland • combined plots and crop rows" };
            _ruralFieldMesh.indexFormat = IndexFormat.UInt16;
            _ruralFieldMesh.subMeshCount = 3;
            _ruralFieldMesh.SetVertices(allVertices);
            for (var submesh = 0; submesh < allTriangles.Length; submesh++)
            {
                var indices = allTriangles[submesh];
                var offset = offsets[submesh];
                for (var i = 0; i < indices.Count; i++) indices[i] += offset;
                _ruralFieldMesh.SetTriangles(indices, submesh);
            }
            _ruralFieldMesh.RecalculateNormals();
            _ruralFieldMesh.RecalculateBounds();

            var fields = new GameObject("Rural farmland • scattered cultivated plots");
            fields.transform.SetParent(root, false);
            fields.isStatic = true;
            fields.AddComponent<MeshFilter>().sharedMesh = _ruralFieldMesh;
            var renderer = fields.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { soilMaterial, furrowMaterial, cropMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void AppendRuralStrip(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles,
            Vector3 start, Vector3 end, Vector3 halfWidth, float height)
        {
            var first = vertices.Count;
            vertices.Add(start + halfWidth + Vector3.up * height);
            vertices.Add(start - halfWidth + Vector3.up * height);
            vertices.Add(end - halfWidth + Vector3.up * height);
            vertices.Add(end + halfWidth + Vector3.up * height);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        /// <summary>Small rock groups, wildflowers and scrub break up the uniform meadow without filling paths.</summary>
        private static void BuildGroundDetails(Transform root)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;
            var rockMaterial = new Material(shader) { name = "Field stones • weathered grey", color = new Color(0.39f, 0.40f, 0.36f) };
            var flowerMaterial = new Material(shader) { name = "Wildflowers • muted gold", color = new Color(0.78f, 0.66f, 0.38f) };
            _landscapeMaterials.Add(rockMaterial);
            _landscapeMaterials.Add(flowerMaterial);
            if (flowerMaterial.HasProperty("_Cull")) flowerMaterial.SetInt("_Cull", 0);

            var rockVertices = new System.Collections.Generic.List<Vector3>(1200);
            var rockTriangles = new System.Collections.Generic.List<int>(1800);
            var flowerVertices = new System.Collections.Generic.List<Vector3>(6000);
            var flowerTriangles = new System.Collections.Generic.List<int>(6000);
            var random = new System.Random(61027);

            for (var i = 0; i < 42; i++)
            {
                var x = (float)random.NextDouble() * 54f - 27f;
                var z = (float)random.NextDouble() * 54f - 27f;
                if (Mathf.Abs(x) < 2.7f || Mathf.Abs(z) < 2.7f || NearFootprint(x, z, -20f, -13f, 4f, 4f) ||
                    NearFootprint(x, z, -22f, 13f, 3f, 3f)) continue;
                var scale = 0.25f + (float)random.NextDouble() * 0.65f;
                AppendRock(rockVertices, rockTriangles, new Vector3(x, 0.10f, z), scale, (float)random.NextDouble() * 6.28f);
            }

            for (var i = 0; i < 115; i++)
            {
                var x = (float)random.NextDouble() * 48f - 24f;
                var z = (float)random.NextDouble() * 48f - 24f;
                if (Mathf.Abs(x) < 2.0f || Mathf.Abs(z) < 2.0f ||
                    NearFootprint(x, z, -6f, 7f, 3f, 2.5f) ||
                    NearFootprint(x, z, 7f, 4f, 3f, 2.5f) ||
                    NearFootprint(x, z, -8f, -5f, 3f, 2.5f) ||
                    NearFootprint(x, z, 1.5f, 2.5f, 4f, 3.2f) ||
                    NearFootprint(x, z, 9f, -4.5f, 3f, 2.4f) ||
                    NearFootprint(x, z, -4.5f, -1.5f, 2.8f, 2.2f) ||
                    NearFootprint(x, z, 6f, -9f, 3.2f, 2.5f) ||
                    NearFootprint(x, z, -20f, -13f, 4f, 4f) ||
                    NearFootprint(x, z, -22f, 13f, 3f, 3f)) continue;

                var blooms = 2 + random.Next(4);
                for (var bloom = 0; bloom < blooms; bloom++)
                {
                    var a = (float)random.NextDouble() * Mathf.PI * 2f;
                    var offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.10f;
                    var basePoint = new Vector3(x, 0.04f, z) + offset;
                    var height = 0.22f + (float)random.NextDouble() * 0.25f;
                    var top = basePoint + Vector3.up * height;
                    var stemSide = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.018f;
                    var stem = flowerVertices.Count;
                    flowerVertices.Add(basePoint - stemSide);
                    flowerVertices.Add(basePoint + stemSide);
                    flowerVertices.Add(top + Vector3.down * 0.025f);
                    flowerTriangles.Add(stem); flowerTriangles.Add(stem + 1); flowerTriangles.Add(stem + 2);

                    // Five small radial petals form a readable flower head at the tip of each stem.
                    for (var petal = 0; petal < 5; petal++)
                    {
                        var petalAngle = petal * Mathf.PI * 2f / 5f;
                        var radial = new Vector3(Mathf.Cos(petalAngle), 0f, Mathf.Sin(petalAngle));
                        var tangent = new Vector3(-radial.z, 0f, radial.x) * 0.035f;
                        var center = top;
                        var first = flowerVertices.Count;
                        flowerVertices.Add(center + radial * 0.012f - tangent);
                        flowerVertices.Add(center + radial * 0.012f + tangent);
                        flowerVertices.Add(center + radial * 0.105f + Vector3.up * 0.018f);
                        flowerTriangles.Add(first); flowerTriangles.Add(first + 1); flowerTriangles.Add(first + 2);
                    }
                }
            }

            var rockMesh = new Mesh { name = "Scattered field stones • combined" };
            rockMesh.indexFormat = rockVertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            rockMesh.SetVertices(rockVertices);
            rockMesh.SetTriangles(rockTriangles, 0);
            rockMesh.RecalculateNormals();
            rockMesh.RecalculateBounds();
            var rocks = new GameObject("Scattered field stones");
            rocks.transform.SetParent(root, false);
            rocks.AddComponent<MeshFilter>().sharedMesh = rockMesh;
            var rockRenderer = rocks.AddComponent<MeshRenderer>();
            rockRenderer.sharedMaterial = rockMaterial;
            rockRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _groundDetailMesh = rockMesh;

            var flowerMesh = new Mesh { name = "Wildflower clusters • combined" };
            flowerMesh.indexFormat = flowerVertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            flowerMesh.SetVertices(flowerVertices);
            flowerMesh.SetTriangles(flowerTriangles, 0);
            flowerMesh.RecalculateNormals();
            flowerMesh.RecalculateBounds();
            var flowers = new GameObject("Wildflowers along meadow edges");
            flowers.transform.SetParent(root, false);
            flowers.AddComponent<MeshFilter>().sharedMesh = flowerMesh;
            var flowerRenderer = flowers.AddComponent<MeshRenderer>();
            flowerRenderer.sharedMaterial = flowerMaterial;
            flowerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _flowerMesh = flowerMesh;
        }

        private static void AppendRock(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<int> triangles, Vector3 center, float scale, float yaw)
        {
            var start = vertices.Count;
            vertices.Add(center + new Vector3(0f, scale, 0f));
            for (var i = 0; i < 5; i++)
            {
                var a = yaw + i * Mathf.PI * 2f / 5f;
                vertices.Add(center + new Vector3(Mathf.Cos(a) * scale, (float)Math.Sin(i * 1.7f) * scale * 0.12f, Mathf.Sin(a) * scale));
            }
            vertices.Add(center + new Vector3(0f, -scale * 0.55f, 0f));
            for (var i = 0; i < 5; i++)
            {
                var next = (i + 1) % 5;
                triangles.Add(start); triangles.Add(start + 1 + next); triangles.Add(start + 1 + i);
                triangles.Add(start + 6); triangles.Add(start + 1 + i); triangles.Add(start + 1 + next);
            }
        }

        private static void BuildAmbientMotes(Transform root)
        {
            var shader = FindParticleShader();
            if (shader == null) return;
            var moteColor = new Color(0.88f, 0.82f, 0.56f, 0.42f);
            _moteMaterial = CreateTransparentMaterial(shader, "Ambient pollen • soft glow", moteColor);
            if (_moteMaterial == null) return;
            if (_moteMaterial.HasProperty("_BaseColor")) _moteMaterial.SetColor("_BaseColor", moteColor);
            if (_moteMaterial.HasProperty("_Color")) _moteMaterial.SetColor("_Color", moteColor);

            var holder = new GameObject("Ambient pollen • drifting in sunlight");
            holder.transform.SetParent(root, false);
            holder.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var particles = holder.AddComponent<ParticleSystem>();
            // AddComponent may auto-start the default system. Stop and clear it before
            // changing duration or other main-module settings during procedural setup.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 18f;
            main.maxParticles = 90;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 17f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.07f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = new Color(0.88f, 0.82f, 0.56f, 0.42f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission;
            emission.rateOverTime = 5.5f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 23f;
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.18f;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
        }

        /// <summary>Thin lowland mist drifting near the meadow floor; kept subtle so it never obscures play.</summary>
        private static void BuildLowlandMist(Transform root)
        {
            var shader = FindParticleShader();
            if (shader == null) return;

            var mistColor = new Color(0.72f, 0.79f, 0.82f, 0.075f);
            _mistMaterial = CreateTransparentMaterial(shader, "Lowland mist • translucent", mistColor);
            if (_mistMaterial == null) return;
            if (_mistMaterial.HasProperty("_BaseColor")) _mistMaterial.SetColor("_BaseColor", mistColor);
            if (_mistMaterial.HasProperty("_Color")) _mistMaterial.SetColor("_Color", mistColor);

            var holder = new GameObject("Lowland mist • drifting ribbons");
            holder.transform.SetParent(root, false);
            holder.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            var particles = holder.AddComponent<ParticleSystem>();
            // AddComponent may auto-start the default system. Stop and clear it before
            // changing duration or other main-module settings during procedural setup.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 32f;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.startColor = mistColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = particles.emission;
            emission.rateOverTime = 0.8f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(48f, 0.16f, 48f);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.18f;
            noise.frequency = 0.08f;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.72f, 0.18f),
                    new GradientAlphaKey(0.72f, 0.78f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _mistMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
        }

        private static Shader FindParticleShader()
        {
            // URP's particle shader is preferred. Transparent fallbacks keep procedural
            // ambience visible when shader stripping or package setup omits the URP particle shader.
            return Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                   Shader.Find("Particles/Standard Unlit") ??
                   Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                   Shader.Find("Universal Render Pipeline/Unlit");
        }

        private static Material CreateTransparentMaterial(Shader shader, string name, Color color)
        {
            if (shader == null) return null;
            var material = new Material(shader) { name = name, color = color };
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Light FindSun()
        {
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var light in lights)
                if (light.type == LightType.Directional) return light;
            return null;
        }

        public static void Cleanup()
        {
            if (RenderSettings.skybox == _skyMaterial) RenderSettings.skybox = null;
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
                RenderSettings.sun = null;
            DestroyAsset(_skyMaterial);
            DestroyAsset(_starMaterial);
            DestroyAsset(_grassMaterial);
            DestroyAsset(_cloudMaterial);
            DestroyAsset(_forestBarkMaterial);
            DestroyAsset(_forestLeafMaterial);
            DestroyAsset(_groundDetailMaterial);
            DestroyAsset(_moteMaterial);
            DestroyAsset(_mistMaterial);
            DestroyAsset(_forestMesh);
            DestroyAsset(_groundDetailMesh);
            DestroyAsset(_flowerMesh);
            DestroyAsset(_starMesh);
            DestroyAsset(_grassMesh);
            DestroyAsset(_ruralFieldMesh);
            foreach (var material in _landscapeMaterials) DestroyAsset(material);
            _landscapeMaterials.Clear();
            _cloudMaterial = null;
            _forestBarkMaterial = null;
            _forestLeafMaterial = null;
            _groundDetailMaterial = null;
            _moteMaterial = null;
            _mistMaterial = null;
            _forestMesh = null;
            _groundDetailMesh = null;
            _flowerMesh = null;
            _starMesh = null;
            _grassMesh = null;
            _ruralFieldMesh = null;
            _skyMaterial = null;
            _starMaterial = null;
            _grassMaterial = null;
        }

        private static void DestroyAsset(UnityEngine.Object asset)
        {
            if (asset != null) UnityEngine.Object.Destroy(asset);
        }
    }

    /// <summary>Presentation-only day/night palette cycle; later it can consume authoritative server time.</summary>
    public sealed class WorldAtmosphereCycle : MonoBehaviour
    {
        [SerializeField] private float visualDayLengthSeconds = 720f;
        private Material _sky;
        private Material _stars;
        private Material _motes;
        private Material _mist;
        private Light _sun;
        private float _phase;

        public void Initialize(Material sky, Material stars, Material motes, Material mist, Light sun)
        {
            _sky = sky;
            _stars = stars;
            _motes = motes;
            _mist = mist;
            _sun = sun;
            _phase = 0.28f;
        }

        private void Update()
        {
            _phase = Mathf.Repeat(_phase + Time.deltaTime / Mathf.Max(60f, visualDayLengthSeconds), 1f);
            var daylight = Mathf.Clamp01((Mathf.Sin((_phase - 0.25f) * Mathf.PI * 2f) + 0.12f) * 1.8f);
            var night = 1f - daylight;
            var dusk = 1f - Mathf.Clamp01(Mathf.Abs(daylight - 0.42f) * 3.2f);
            var daySky = new Color(0.72f, 0.83f, 0.94f);
            var duskSky = new Color(0.88f, 0.48f, 0.32f);
            var nightSky = new Color(0.12f, 0.17f, 0.29f);
            var tint = Color.Lerp(nightSky, daySky, daylight);
            tint = Color.Lerp(tint, duskSky, dusk * 0.72f);
            if (_sky != null)
            {
                _sky.SetColor("_SkyTint", tint);
                _sky.SetFloat("_Exposure", Mathf.Lerp(0.35f, 1.15f, daylight));
            }
            if (_stars != null)
            {
                var alpha = Mathf.SmoothStep(0.08f, 0.72f, night);
                var color = new Color(0.78f, 0.86f, 1f, alpha);
                if (_stars.HasProperty("_BaseColor")) _stars.SetColor("_BaseColor", color);
                else _stars.color = color;
            }
            if (_motes != null)
            {
                var color = new Color(0.88f, 0.82f, 0.56f, daylight * 0.42f);
                if (_motes.HasProperty("_BaseColor")) _motes.SetColor("_BaseColor", color);
                else if (_motes.HasProperty("_Color")) _motes.SetColor("_Color", color);
            }
            if (_sun != null)
            {
                var angle = _phase * 360f;
                _sun.transform.rotation = Quaternion.Euler(angle - 90f, 35f, 0f);
                _sun.intensity = Mathf.Lerp(0.08f, 1.25f, daylight);
                _sun.color = Color.Lerp(new Color(0.48f, 0.55f, 0.82f), new Color(1f, 0.91f, 0.76f), daylight);
            }
            RenderSettings.ambientLight = Color.Lerp(new Color(0.10f, 0.13f, 0.22f), new Color(0.58f, 0.61f, 0.64f), daylight);
            RenderSettings.fogColor = Color.Lerp(new Color(0.10f, 0.14f, 0.23f), new Color(0.61f, 0.70f, 0.73f), daylight);
            if (_mist != null)
            {
                // Mist gathers around dawn and dusk, then thins during bright daylight.
                var mistAmount = Mathf.Lerp(0.95f, 0.35f, daylight);
                mistAmount = Mathf.Max(mistAmount, dusk * 0.9f);
                var color = new Color(0.72f, 0.79f, 0.82f, 0.075f * mistAmount);
                if (_mist.HasProperty("_BaseColor")) _mist.SetColor("_BaseColor", color);
                else if (_mist.HasProperty("_Color")) _mist.SetColor("_Color", color);
            }
        }
    }

    /// <summary>Subtle vertex sway for the combined meadow mesh; no per-blade GameObjects are created.</summary>
    public sealed class MeadowGrassWind : MonoBehaviour
    {
        private Mesh _mesh;
        private Vector3[] _rest;
        private Vector3[] _vertices;

        public void Initialize(Mesh mesh)
        {
            _mesh = mesh;
            if (_mesh == null) return;
            _rest = _mesh.vertices;
            _vertices = new Vector3[_rest.Length];
            Array.Copy(_rest, _vertices, _rest.Length);
            var bounds = _mesh.bounds;
            bounds.Expand(new Vector3(0.12f, 0f, 0f));
            _mesh.bounds = bounds;
        }

        private void Update()
        {
            if (_mesh == null || _rest == null) return;
            var time = Time.time * 1.15f;
            for (var i = 0; i < _rest.Length; i++)
            {
                var vertex = _rest[i];
                if (i % 3 == 2)
                    vertex.x += Mathf.Sin(time + vertex.x * 0.27f + vertex.z * 0.19f) * 0.055f;
                _vertices[i] = vertex;
            }
            _mesh.vertices = _vertices;
        }
    }

    public sealed class WorldCloudDrift : MonoBehaviour
    {
        private float _speed;
        public void Initialize(float speed) { _speed = speed; }
        private void Update()
        {
            transform.position += Vector3.right * (_speed * Time.deltaTime);
            if (transform.position.x > 115f) transform.position += Vector3.left * 230f;
        }
    }

    public sealed class WindmillRotor : MonoBehaviour
    {
        private float _degreesPerSecond;
        public void Initialize(float degreesPerSecond) { _degreesPerSecond = degreesPerSecond; }
        private void Update() { transform.Rotate(0f, 0f, _degreesPerSecond * Time.deltaTime, Space.Self); }
    }

    public sealed class WorldSkyFollowCamera : MonoBehaviour
    {
        private Transform _camera;
        private void LateUpdate()
        {
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            if (_camera != null) transform.position = _camera.position;
        }
    }
}
