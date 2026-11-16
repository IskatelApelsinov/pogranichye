using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Renders optional server-authored multi-level spaces. All geometry is expressed in the
    /// canonical world frame and reprojected against the current settlement anchor. Mesh
    /// colliders are presentation/prediction only; the server remains movement authority.
    /// </summary>
    public static class WorldSpacePresentationBuilder
    {
        private const double ChunkMeters = 256d;
        private static Material _floor;
        private static Material _wall;
        private static Material _ceiling;
        private static Material _roof;
        private static Material _stone;
        private static Material _stair;

        public static void Apply(Transform parent, WorldSpaceChunkFrame chunk, WorldAnchorFrame anchor)
        {
            if (parent == null || chunk == null || !chunk.IsValid || anchor == null ||
                chunk.Frame != anchor.Frame) return;

            var rootName = RootName(chunk.Frame, chunk.Chunk[0], chunk.Chunk[1]);
            var old = parent.Find(rootName);
            if (old != null) RemoveRoot(old);

            var root = new GameObject(rootName).transform;
            root.SetParent(parent, false);
            try
            {
                var renderedSpaceIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var space in chunk.Spaces)
                {
                    if (space == null || space.Origin == null || space.Bounds == null ||
                        string.IsNullOrWhiteSpace(space.Id) || !renderedSpaceIds.Add(space.Id)) continue;
                    // A space may be repeated in every horizontal chunk it intersects.
                    // Render it only in the chunk containing its bounds centre to avoid duplicates.
                    var centreEast = (space.Bounds.MinEast + space.Bounds.MaxEast) * 0.5d;
                    var centreNorth = (space.Bounds.MinNorth + space.Bounds.MaxNorth) * 0.5d;
                    if (ChunkOf(centreEast) != chunk.Chunk[0] || ChunkOf(centreNorth) != chunk.Chunk[1])
                        continue;

                    var offset = new Vector3(
                        (float)(space.Origin.East - anchor.East),
                        (float)(space.Origin.Up - anchor.Up),
                        (float)(space.Origin.North - anchor.North));
                    var spaceRoot = new GameObject("Space_" + SafeName(space.Id)).transform;
                    spaceRoot.SetParent(root, false);
                    spaceRoot.localPosition = offset;
                    BuildSurfaces(spaceRoot, space);
                    BuildStairs(spaceRoot, space);
                }
            }
            catch
            {
                RemoveRoot(root);
                throw;
            }
        }

        public static void Clear(Transform parent)
        {
            if (parent == null) return;
            var children = parent.GetComponentsInChildren<Transform>(true);
            for (var i = children.Length - 1; i >= 0; i--)
            {
                var child = children[i];
                if (child != null && child.parent == parent &&
                    child.name.StartsWith("WorldSpaces_", StringComparison.Ordinal))
                    RemoveRoot(child);
            }
        }

        private static void BuildSurfaces(Transform parent, WorldSpaceFrame space)
        {
            if (space.Surfaces == null) return;
            foreach (var surface in space.Surfaces)
            {
                if (surface == null || surface.Points == null || surface.Points.Count < 3) continue;
                var vertices = new Vector3[surface.Points.Count];
                for (var i = 0; i < vertices.Length; i++)
                {
                    var point = surface.Points[i];
                    vertices[i] = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
                }

                // The descriptors currently describe polygon boundaries. Fan triangulation is
                // sufficient for the authored convex floor/wall panels; reverse faces keep them
                // visible from both sides without requiring a client-invented thickness.
                var triangles = new int[(vertices.Length - 2) * 6];
                var t = 0;
                for (var i = 1; i < vertices.Length - 1; i++)
                {
                    triangles[t++] = 0; triangles[t++] = i; triangles[t++] = i + 1;
                    triangles[t++] = 0; triangles[t++] = i + 1; triangles[t++] = i;
                }

                var mesh = new Mesh { name = "Server surface " + surface.Id };
                if (vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var go = new GameObject("Surface_" + SafeName(surface.Id));
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MaterialFor(surface.Kind);
                renderer.shadowCastingMode = surface.Kind == "floor" || surface.Kind == "ceiling"
                    ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (surface.Collidable)
                {
                    var collider = go.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                }
            }
        }

        private static void BuildStairs(Transform parent, WorldSpaceFrame space)
        {
            if (space.Stairs == null) return;
            foreach (var stair in space.Stairs)
            {
                if (stair == null || stair.Points == null || stair.Points.Count < 2) continue;
                var segments = stair.Points.Count - 1;
                var totalSteps = Mathf.Clamp(stair.StepCount, segments, 96);
                var remaining = totalSteps;
                for (var segment = 0; segment < segments; segment++)
                {
                    var a = stair.Points[segment];
                    var b = stair.Points[segment + 1];
                    var direction = new Vector3((float)(b.X - a.X), (float)(b.Y - a.Y), (float)(b.Z - a.Z));
                    var length = direction.magnitude;
                    if (length < 0.05f) continue;
                    var steps = segment == segments - 1 ? remaining :
                        Mathf.Max(1, Mathf.RoundToInt(totalSteps * length / PolylineLength(stair.Points)));
                    steps = Mathf.Min(steps, remaining);
                    if (steps <= 0) continue;
                    remaining -= steps;
                    var forward = direction / length;
                    var rotation = Quaternion.LookRotation(forward, Vector3.up);
                    var run = length / steps;
                    for (var step = 0; step < steps; step++)
                    {
                        var fraction = (step + 0.5f) / steps;
                        var centre = Vector3.Lerp(
                            new Vector3((float)a.X, (float)a.Y, (float)a.Z),
                            new Vector3((float)b.X, (float)b.Y, (float)b.Z), fraction);
                        var tread = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tread.name = "Stair_" + SafeName(stair.Id) + "_" + step;
                        tread.transform.SetParent(parent, false);
                        tread.transform.localPosition = centre;
                        tread.transform.localRotation = rotation;
                        tread.transform.localScale = new Vector3(
                            Mathf.Clamp((float)stair.WidthMeters, 0.6f, 4f),
                            Mathf.Max(0.08f, Mathf.Abs(direction.y) / steps + 0.08f),
                            Mathf.Max(0.12f, run));
                        // Local collider is useful for presentation physics only; server movement
                        // validation remains authoritative and must agree with this stair corridor.
                        tread.GetComponent<MeshRenderer>().sharedMaterial = GetStairMaterial();
                    }
                }
            }
        }

        private static float PolylineLength(List<WorldSpacePointFrame> points)
        {
            var length = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                var dx = (float)(b.X - a.X);
                var dy = (float)(b.Y - a.Y);
                var dz = (float)(b.Z - a.Z);
                length += Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            return Mathf.Max(0.001f, length);
        }

        private static Material MaterialFor(string kind)
        {
            switch (kind)
            {
                case "floor": return _floor ?? (_floor = MakeMaterial("Server space • timber floor", new Color(0.29f, 0.20f, 0.13f)));
                case "ceiling": return _ceiling ?? (_ceiling = MakeMaterial("Server space • ceiling", new Color(0.22f, 0.18f, 0.14f)));
                case "roof": return _roof ?? (_roof = MakeMaterial("Server space • roof", new Color(0.27f, 0.22f, 0.15f)));
                case "cliff":
                case "cave_wall":
                case "waterbed": return _stone ?? (_stone = MakeMaterial("Server space • stone", new Color(0.31f, 0.30f, 0.27f)));
                case "wall": return _wall ?? (_wall = MakeMaterial("Server space • wall", new Color(0.34f, 0.25f, 0.17f)));
                default: return _floor ?? (_floor = MakeMaterial("Server space • timber floor", new Color(0.29f, 0.20f, 0.13f)));
            }
        }

        private static Material GetStairMaterial()
        {
            return _stair ?? (_stair = MakeMaterial("Server space • stair timber", new Color(0.32f, 0.23f, 0.15f)));
        }

        private static Material MakeMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };
            return material;
        }

        private static long ChunkOf(double coordinate) => (long)Math.Floor(coordinate / ChunkMeters);
        private static string RootName(int frame, long x, long z) => "WorldSpaces_" + frame + "_" + x + "_" + z;

        private static string SafeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unnamed";
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        private static void RemoveRoot(Transform root)
        {
            if (root == null) return;
            var meshes = root.GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < meshes.Length; i++)
                if (meshes[i] != null && meshes[i].gameObject.name.StartsWith("Surface_", StringComparison.Ordinal) &&
                    meshes[i].sharedMesh != null)
                    UnityEngine.Object.Destroy(meshes[i].sharedMesh);
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
