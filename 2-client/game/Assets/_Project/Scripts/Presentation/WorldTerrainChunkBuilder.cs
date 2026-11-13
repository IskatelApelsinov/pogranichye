using System;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Render and static collision mesh for one server-owned v1.19 world chunk.
    /// Vertices are rebased against the active settlement anchor, keeping Unity coordinates near zero.
    /// </summary>
    public static class WorldTerrainChunkBuilder
    {
        private const string RootName = "WorldTerrainChunk";
        private static Material _material;

        public static string ObjectName(string chunkId)
        {
            return RootName + "_" + (chunkId ?? string.Empty).Replace(':', '_').Replace('-', 'm');
        }

        public static void Clear(Transform root)
        {
            if (root == null) return;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null || !child.name.StartsWith(RootName + "_", StringComparison.Ordinal)) continue;
                RemoveObject(child);
            }
        }

        public static void Remove(Transform root, string chunkId)
        {
            if (root == null) return;
            var old = root.Find(ObjectName(chunkId));
            if (old != null) RemoveObject(old);
        }

        private static void RemoveObject(Transform old)
        {
            var filter = old.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            old.name += " (replaced)";
            UnityEngine.Object.Destroy(old.gameObject);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }

        public static void Apply(Transform root, TerrainChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (root == null || chunk == null || !chunk.IsValid || anchor == null) return;
            var objectName = ObjectName(chunk.Id);
            var old = root.Find(objectName);
            if (old != null)
            {
                RemoveObject(old);
            }

            var side = chunk.Side;
            var vertices = new Vector3[chunk.Heights.Count];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(side - 1) * (side - 1) * 6];
            var originX = chunk.Origin.East - anchor.East;
            var originZ = chunk.Origin.North - anchor.North;
            for (var z = 0; z < side; z++)
            for (var x = 0; x < side; x++)
            {
                var i = z * side + x;
                vertices[i] = new Vector3(
                    (float)(originX + x * chunk.StepMeters),
                    (float)(chunk.Heights[i] - anchor.Up),
                    (float)(originZ + z * chunk.StepMeters));
                uvs[i] = new Vector2((float)x / (side - 1), (float)z / (side - 1));
            }

            var t = 0;
            for (var z = 0; z < side - 1; z++)
            for (var x = 0; x < side - 1; x++)
            {
                var a = z * side + x;
                var b = a + 1;
                var c = a + side;
                var d = c + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }

            var mesh = new Mesh { name = "Terrain " + chunk.Id };
            if (vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(objectName);
            go.transform.SetParent(root, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            // One non-convex collider per bounded chunk. No per-vertex GameObjects or dynamic rigidbodies.
            // This gives future cross-settlement traversal real ground contact without changing server movement authority.
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    UnityEngine.Object.Destroy(go);
                    UnityEngine.Object.Destroy(mesh);
                    return;
                }
                _material = new Material(shader) { name = "World terrain • URP compatible" };
                if (_material.HasProperty("_BaseColor"))
                    _material.SetColor("_BaseColor", new Color(0.31f, 0.37f, 0.24f));
                else if (_material.HasProperty("_Color"))
                    _material.color = new Color(0.31f, 0.37f, 0.24f);
            }
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }
    }
}
