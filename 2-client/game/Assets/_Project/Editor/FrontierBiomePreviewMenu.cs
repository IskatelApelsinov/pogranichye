#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using Pogranichye.Client.Presentation;
using UnityEditor;
using UnityEngine;

namespace Pogranichye.Client.Editor
{
    /// <summary>
    /// Creates a disposable five-chunk strip so all current server biome profiles and their
    /// LODs can be inspected in the Editor without waiting to discover each region in-game.
    /// These fixtures are never sent to the server and are not gameplay/world data.
    /// </summary>
    public static class FrontierBiomePreviewMenu
    {
        private const string RootName = "FrontierBiomePreview_TEST_ONLY";

        [MenuItem("Pogranichye/Debug/Build Frontier Biome Test Strip")]
        public static void BuildTestStrip()
        {
            ClearTestStrip();

            var root = new GameObject(RootName);
            var anchor = new WorldAnchorFrame { Frame = 0, East = 0, North = 0, Up = 0 };
            var biomes = new[]
            {
                new { Name = "forest", Cover = new TerrainCoverFrame { Forest = 0.55, Grass = 0.30, Rock = 0.05, Wet = 0.10 } },
                new { Name = "plain", Cover = new TerrainCoverFrame { Forest = 0.10, Grass = 0.75, Rock = 0.05, Wet = 0.10 } },
                new { Name = "hills", Cover = new TerrainCoverFrame { Forest = 0.25, Grass = 0.40, Rock = 0.30, Wet = 0.05 } },
                new { Name = "coast", Cover = new TerrainCoverFrame { Forest = 0.15, Grass = 0.35, Rock = 0.10, Wet = 0.40 } },
                new { Name = "steppe", Cover = new TerrainCoverFrame { Forest = 0.02, Grass = 0.85, Rock = 0.08, Wet = 0.05 } }
            };

            for (int i = 0; i < biomes.Length; i++)
            {
                var chunk = CreateChunk(i, biomes[i].Name, biomes[i].Cover);
                WorldTerrainChunkBuilder.Apply(root.transform, chunk, anchor);
                WorldFrontierBiomeBuilder.Apply(root.transform, chunk, anchor);
                CreateLabel(root.transform, biomes[i].Name, i);
            }

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("Frontier biome test strip created. Move the Scene/Game camera along +Z; each 256 m tile is a different server biome fixture. This is local Editor-only presentation data.");
        }

        [MenuItem("Pogranichye/Debug/Clear Frontier Biome Test Strip")]
        public static void ClearTestStrip()
        {
            var old = GameObject.Find(RootName);
            if (old == null) return;

            // The generated meshes are transient runtime objects, not project assets.
            foreach (var filter in old.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
            UnityEngine.Object.DestroyImmediate(old);
        }

        private static TerrainChunkFrame CreateChunk(int index, string biome, TerrainCoverFrame cover)
        {
            const int size = 256;
            const int step = 16;
            const int side = size / step + 1;
            var heights = new List<double>(side * side);
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                double wx = x * step;
                double wz = index * size + z * step;
                heights.Add(1.4 * Math.Sin(wx * 0.018) + 1.1 * Math.Cos(wz * 0.014));
            }

            var chunk = new TerrainChunkFrame
            {
                Id = "frontier-preview:0:" + index,
                Frame = 0,
                Chunk = new[] { 0L, (long)index },
                Origin = new TerrainOriginFrame { East = 0, North = index * size },
                SizeMeters = size,
                StepMeters = step,
                Side = side,
                Heights = heights,
                Seed = 92831L,
                GeneratorVersion = 1,
                Biome = biome,
                Cover = cover,
                Roads = new List<TerrainPathFeatureFrame>
                {
                    new TerrainPathFeatureFrame
                    {
                        Id = "preview-road-" + index,
                        Kind = "trail",
                        WidthMeters = 5,
                        Points = new List<TerrainFeaturePointFrame>
                        {
                            new TerrainFeaturePointFrame { X = 0, Z = 128 },
                            new TerrainFeaturePointFrame { X = 256, Z = 128 }
                        }
                    }
                }
            };
            return chunk;
        }

        private static void CreateLabel(Transform root, string biome, int index)
        {
            var label = new GameObject("TEST LABEL • " + biome);
            label.transform.SetParent(root, false);
            label.transform.localPosition = new Vector3(128, 19, index * 256 + 128);
            var text = label.AddComponent<TextMesh>();
            text.text = biome.ToUpperInvariant() + "  /  TEST CHUNK";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 1.8f;
            text.fontSize = 36;
            text.color = Color.white;
        }
    }
}
#endif
