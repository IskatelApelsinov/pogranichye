using System;
using Pogranichye.Client.Networking.Protocol;
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

            // Water and bridge are placed from the exact server geometry.
            var river = Create("River • server layout", PrimitiveType.Cube, world,
                new Vector3(0f, -0.025f, (float)((layout.River.Y0 + layout.River.Y1) * 0.5)),
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
                    var center = new Vector3((float)((b.X0 + b.X1) * 0.5), 2.35f, (float)((b.Y0 + b.Y1) * 0.5));
                    var building = Create("Server building • " + b.Kind, PrimitiveType.Cube, world,
                        center, new Vector3(width, 4.7f, depth), _timber, false);
                    var collider = building.AddComponent<BoxCollider>();
                    collider.size = Vector3.one;
                    building.layer = 2; // PresentationCameraRig's obstruction mask.
                    var roof = Create("Thatched roof • " + b.Kind, PrimitiveType.Cube, world,
                        new Vector3(center.x, 4.95f, center.z), new Vector3(width + 0.8f, 0.65f, depth + 0.8f), _roof, false);
                }
            }

            // Broad, readable agricultural and woodland zones establish the scale of the 300 m world.
            Create("West forest floor", PrimitiveType.Cube, world, new Vector3(-108f, -0.08f, 0f),
                new Vector3(78f, 0.12f, 280f), _forest, false);
            Create("East grain country", PrimitiveType.Cube, world, new Vector3(103f, -0.07f, 10f),
                new Vector3(88f, 0.10f, 260f), _field, false);
            Create("North meadow", PrimitiveType.Cube, world, new Vector3(8f, -0.075f, 112f),
                new Vector3(115f, 0.10f, 64f), _field, false);
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