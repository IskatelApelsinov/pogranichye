using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Distant, non-colliding LOD of the cauldron mountain rim.
    /// Every point and height comes from the server's world manifest; this class only builds its mesh.
    /// </summary>
    public static class CauldronRimPresentationBuilder
    {
        private const string ObjectName = "ServerCauldronMountainRimLOD";
        private static Material _material;
        private static CauldronRimFrame _lastRim;
        private static double _lastAnchorEast = double.NaN;
        private static double _lastAnchorNorth = double.NaN;
        private static double _lastAnchorUp = double.NaN;

        public static void Clear(Transform root)
        {
            if (root == null) return;
            var old = root.Find(ObjectName);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            _lastRim = null;
            _lastAnchorEast = _lastAnchorNorth = _lastAnchorUp = double.NaN;
        }

        public static void BuildOrUpdate(Transform root, CauldronRimFrame rim, WorldAnchorFrame anchor)
        {
            if (root == null || rim == null || anchor == null || rim.Samples == null ||
                rim.Samples.Count < 12 || !Finite(rim.CenterEast) || !Finite(rim.CenterNorth) ||
                !Finite(anchor.East) || !Finite(anchor.North) || !Finite(anchor.Up))
            {
                Clear(root);
                return;
            }

            if (ReferenceEquals(_lastRim, rim) &&
                Math.Abs(_lastAnchorEast - anchor.East) < 0.001 &&
                Math.Abs(_lastAnchorNorth - anchor.North) < 0.001 &&
                Math.Abs(_lastAnchorUp - anchor.Up) < 0.001)
                return;

            var samples = rim.Samples;
            var vertices = new Vector3[samples.Count * 3];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[samples.Count * 2 * 6];
            var colors = new Color[vertices.Length];

            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var next = samples[(i + 1) % samples.Count];
                if (!Valid(sample) || (i > 0 && sample.AngleDegrees <= samples[i - 1].AngleDegrees))
                {
                    Clear(root);
                    return;
                }

                var angle = sample.AngleDegrees * Mathf.Deg2Rad;
                var radii = new[] { sample.InnerRadiusMeters, sample.CrestRadiusMeters, sample.OuterRadiusMeters };
                var heights = new[] { sample.InnerGroundMeters, sample.CrestMeters, sample.OuterGroundMeters };
                for (var band = 0; band < 3; band++)
                {
                    var x = rim.CenterEast + Math.Cos(angle) * radii[band] - anchor.East;
                    var z = rim.CenterNorth + Math.Sin(angle) * radii[band] - anchor.North;
                    var index = i * 3 + band;
                    vertices[index] = new Vector3((float)x, (float)(heights[band] - anchor.Up), (float)z);
                    uvs[index] = new Vector2(i / (float)samples.Count, band * 0.5f);
                    colors[index] = band == 1
                        ? new Color(0.52f, 0.50f, 0.46f, 1f)
                        : new Color(0.30f, 0.32f, 0.29f, 1f);
                }

                var nextIndex = ((i + 1) % samples.Count) * 3;
                var currentIndex = i * 3;
                var t = i * 12;
                // Inner slope: inner ground → crest.
                triangles[t] = currentIndex; triangles[t + 1] = currentIndex + 1; triangles[t + 2] = nextIndex;
                triangles[t + 3] = nextIndex; triangles[t + 4] = currentIndex + 1; triangles[t + 5] = nextIndex + 1;
                // Outer slope: crest → outer ground.
                triangles[t + 6] = currentIndex + 1; triangles[t + 7] = currentIndex + 2; triangles[t + 8] = nextIndex + 1;
                triangles[t + 9] = nextIndex + 1; triangles[t + 10] = currentIndex + 2; triangles[t + 11] = nextIndex + 2;
            }

            var mesh = new Mesh { name = "Server cauldron mountain rim • distant LOD" };
            if (vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = root.Find(ObjectName);
            if (host == null)
            {
                host = new GameObject(ObjectName).transform;
                host.SetParent(root, false);
                var filter = host.gameObject.AddComponent<MeshFilter>();
                var renderer = host.gameObject.AddComponent<MeshRenderer>();
                if (_material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (shader == null)
                    {
                        UnityEngine.Object.Destroy(host.gameObject);
                        UnityEngine.Object.Destroy(mesh);
                        return;
                    }
                    _material = new Material(shader) { name = "Server mountain rim • LOD" };
                    if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", new Color(0.38f, 0.39f, 0.37f));
                    else if (_material.HasProperty("_Color")) _material.color = new Color(0.38f, 0.39f, 0.37f);
                    if (_material.HasProperty("_Smoothness")) _material.SetFloat("_Smoothness", 0.12f);
                    _material.SetInt("_Cull", 0);
                }
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            var meshFilter = host.GetComponent<MeshFilter>();
            var oldMesh = meshFilter.sharedMesh;
            meshFilter.sharedMesh = mesh;
            if (oldMesh != null) UnityEngine.Object.Destroy(oldMesh);
            // Deliberately no MeshCollider: the server owns the impassable boundary.
            _lastRim = rim;
            _lastAnchorEast = anchor.East;
            _lastAnchorNorth = anchor.North;
            _lastAnchorUp = anchor.Up;
        }

        public static float RequiredFarClip(CauldronRimFrame rim)
        {
            if (rim?.Samples == null) return 0f;
            var outer = 0d;
            foreach (var sample in rim.Samples)
                if (sample != null && Finite(sample.OuterRadiusMeters))
                    outer = Math.Max(outer, sample.OuterRadiusMeters);
            return outer > 0d ? Mathf.Clamp((float)(outer * 2.05d + 500d), 1000f, 100000f) : 0f;
        }

        private static bool Valid(CauldronRimSampleFrame sample)
        {
            return sample != null && Finite(sample.AngleDegrees) &&
                Finite(sample.InnerRadiusMeters) && Finite(sample.CrestRadiusMeters) &&
                Finite(sample.OuterRadiusMeters) && Finite(sample.InnerGroundMeters) &&
                Finite(sample.CrestMeters) && Finite(sample.OuterGroundMeters) &&
                sample.InnerRadiusMeters > 0 &&
                sample.CrestRadiusMeters > sample.InnerRadiusMeters &&
                sample.OuterRadiusMeters > sample.CrestRadiusMeters;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
