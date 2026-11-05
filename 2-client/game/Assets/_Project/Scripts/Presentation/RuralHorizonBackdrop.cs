using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Optional, non-traversable rural horizon made from directional JPEGs in Resources/RuralHorizon.
    /// It follows the presentation camera horizontally and never participates in collision/navigation.
    /// Expected resources: North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest.
    /// </summary>
    public sealed class RuralHorizonBackdrop : MonoBehaviour
    {
        private static readonly string[] Directions =
        {
            "North", "NorthEast", "East", "SouthEast",
            "South", "SouthWest", "West", "NorthWest"
        };

        [SerializeField] private float radius = 178f;
        [SerializeField] private float bottom = -8f;
        [SerializeField] private float top = 48f;
        [SerializeField] private float cameraFollowHeight = 0f;
        private Transform _cameraTransform;
        private Transform _ring;

        public static RuralHorizonBackdrop Build(Transform worldRoot, Camera camera, float worldHalf)
        {
            if (worldRoot == null || camera == null) return null;
            var existing = worldRoot.Find("RuralHorizonBackdrop");
            if (existing != null) return existing.GetComponent<RuralHorizonBackdrop>();

            var textures = new Texture2D[Directions.Length];
            var anyTexture = false;
            for (var i = 0; i < Directions.Length; i++)
            {
                textures[i] = Resources.Load<Texture2D>("RuralHorizon/" + Directions[i]);
                anyTexture |= textures[i] != null;
            }

            // Keep the existing sky/hill presentation untouched until the user supplies JPEGs.
            if (!anyTexture) return null;

            var host = new GameObject("RuralHorizonBackdrop");
            host.transform.SetParent(worldRoot, false);
            var backdrop = host.AddComponent<RuralHorizonBackdrop>();
            backdrop.radius = Mathf.Max(worldHalf + 24f, 178f);
            backdrop._cameraTransform = camera.transform;
            backdrop.CreateRing(textures);
            return backdrop;
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null || _ring == null) return;
            var p = _cameraTransform.position;
            _ring.position = new Vector3(p.x, cameraFollowHeight, p.z);
        }

        private void CreateRing(Texture2D[] textures)
        {
            _ring = new GameObject("JPEG horizon ring • non-traversable").transform;
            _ring.SetParent(transform, false);

            for (var i = 0; i < Directions.Length; i++)
            {
                var texture = textures[i];
                if (texture == null) continue;

                var angle0 = (i * 45f - 22.5f) * Mathf.Deg2Rad;
                var angle1 = ((i + 1) * 45f - 22.5f) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Sin(angle0) * radius, 0f, Mathf.Cos(angle0) * radius);
                var b = new Vector3(Mathf.Sin(angle1) * radius, 0f, Mathf.Cos(angle1) * radius);

                var mesh = new Mesh { name = "Horizon segment • " + Directions[i] };
                mesh.vertices = new[]
                {
                    a + Vector3.up * bottom,
                    b + Vector3.up * bottom,
                    a + Vector3.up * top,
                    b + Vector3.up * top
                };
                // Double-sided material below; triangles face both directions via duplicated winding.
                mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 1, 2, 0, 3, 2, 1 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var segment = new GameObject("Horizon • " + Directions[i]);
                segment.transform.SetParent(_ring, false);
                var filter = segment.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = segment.AddComponent<MeshRenderer>();
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                if (shader == null) continue;
                var material = new Material(shader)
                {
                    name = "Horizon JPEG • " + Directions[i],
                    mainTexture = texture
                };
                material.mainTextureScale = new Vector2(1f, 1f);
                material.SetInt("_Cull", 0);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}
