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
            var cycle = environment.gameObject.AddComponent<WorldAtmosphereCycle>();
            cycle.Initialize(_skyMaterial, _starMaterial, FindSun());
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
            }

            _starMaterial = CreateTransparentMaterial(
                Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"),
                "Distant stars", new Color(0.78f, 0.86f, 1f, 0f));
            if (_starMaterial == null) return;

            var starMesh = new Mesh { name = "Procedural star field" };
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
            starMesh.vertices = vertices;
            starMesh.triangles = triangles;
            starMesh.RecalculateBounds();
            var stars = new GameObject("Stars • high atmosphere");
            stars.transform.SetParent(root, false);
            var filter = stars.AddComponent<MeshFilter>();
            filter.sharedMesh = starMesh;
            var renderer = stars.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _starMaterial;
            stars.AddComponent<WorldSkyFollowCamera>();
        }

        private static void BuildDistantLandscape(Transform root)
        {
            var random = new System.Random(14027);
            for (var i = 0; i < 36; i++)
            {
                var angle = i * Mathf.PI * 2f / 36f;
                var distance = 66f + (float)random.NextDouble() * 20f;
                var height = 7f + (float)random.NextDouble() * 13f;
                var width = 15f + (float)random.NextDouble() * 12f;
                var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Far ridge • soft silhouette";
                hill.transform.SetParent(root, false);
                hill.transform.position = new Vector3(Mathf.Cos(angle) * distance, height * 0.32f, Mathf.Sin(angle) * distance);
                hill.transform.localScale = new Vector3(width, height, width * 0.62f);
                var collider = hill.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);
                var renderer = hill.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateLandscapeMaterial(i);
                hill.isStatic = true;
            }

            // A few nearer rock outcrops break the perfectly flat horizon around the creek.
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Mathf.PI * 2f / 12f + 0.18f;
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "Distant rocky outcrop";
                rock.transform.SetParent(root, false);
                rock.transform.position = new Vector3(Mathf.Cos(angle) * 43f, 1.4f, Mathf.Sin(angle) * 43f);
                rock.transform.localScale = new Vector3(5f, 2.6f, 4f);
                var collider = rock.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);
                rock.GetComponent<Renderer>().sharedMaterial = CreateLandscapeMaterial(i + 41);
                rock.isStatic = true;
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
                if (x > 3f && x < 11f && z > -11f && z < 5f) continue;
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

            var mesh = new Mesh { name = "Meadow grass blades" };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _grassMaterial = new Material(shader) { name = "Meadow grass • deep green", color = new Color(0.35f, 0.46f, 0.23f) };
            if (_grassMaterial.HasProperty("_Cull")) _grassMaterial.SetInt("_Cull", 0);
            var grass = new GameObject("Meadow grass • dense field detail");
            grass.transform.SetParent(root, false);
            grass.AddComponent<MeshFilter>().sharedMesh = mesh;
            grass.AddComponent<MeshRenderer>().sharedMaterial = _grassMaterial;
            grass.isStatic = true;
        }

        private static Material CreateTransparentMaterial(Shader shader, string name, Color color)
        {
            if (shader == null) return null;
            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Light FindSun()
        {
            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
            foreach (var light in lights)
                if (light.type == LightType.Directional) return light;
            return null;
        }

        public static void Cleanup()
        {
            if (RenderSettings.skybox == _skyMaterial) RenderSettings.skybox = null;
            DestroyAsset(_skyMaterial);
            DestroyAsset(_starMaterial);
            DestroyAsset(_grassMaterial);
            DestroyAsset(_cloudMaterial);
            foreach (var material in _landscapeMaterials) DestroyAsset(material);
            _landscapeMaterials.Clear();
            _cloudMaterial = null;
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
        private Light _sun;
        private float _phase;

        public void Initialize(Material sky, Material stars, Light sun)
        {
            _sky = sky;
            _stars = stars;
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
            if (_sun != null)
            {
                var angle = _phase * 360f;
                _sun.transform.rotation = Quaternion.Euler(angle - 90f, 35f, 0f);
                _sun.intensity = Mathf.Lerp(0.08f, 1.25f, daylight);
                _sun.color = Color.Lerp(new Color(0.48f, 0.55f, 0.82f), new Color(1f, 0.91f, 0.76f), daylight);
            }
            RenderSettings.ambientLight = Color.Lerp(new Color(0.10f, 0.13f, 0.22f), new Color(0.58f, 0.61f, 0.64f), daylight);
            RenderSettings.fogColor = Color.Lerp(new Color(0.10f, 0.14f, 0.23f), new Color(0.61f, 0.70f, 0.73f), daylight);
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
