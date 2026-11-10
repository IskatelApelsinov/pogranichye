#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pogranichye.Client.Editor
{
    /// <summary>
    /// Copies selected prefabs from the optional EmaceArt Slavic village package into Resources.
    /// Original source assets remain untouched; runtime code has graceful procedural fallbacks.
    /// </summary>
    public static class SlavicVillageAssetInstaller
    {
        private const string SourceRoot = "Assets/EmaceArt/Slavic World Free/Prefabs/";
        private const string DestinationRoot = "Assets/_Project/Resources/PogranichyeArt/";

        private sealed class Entry
        {
            public readonly string Key;
            public readonly string Source;
            public Entry(string key, string source) { Key = key; Source = SourceRoot + source; }
        }

        private static readonly Entry[] Entries =
        {
            new Entry("VillageHouse01", "Town/Building/EA03_Town_House_Comp_03a_PRE.prefab"),
            new Entry("VillageHouse02", "Town/Building/EA03_Town_House_Comp_03b_PRE.prefab"),
            new Entry("VillageHouse03", "Town/Building/EA03_Town_House_Comp_03c_PRE.prefab"),
            new Entry("Tree01", "Nature/Tree/EA03_Nature_Tree_01b_PRE.prefab"),
            new Entry("Tree02", "Nature/Tree/EA03_Nature_Tree_02b_PRE.prefab"),
            new Entry("Tree03", "Nature/Tree/EA03_Nature_Tree_03b_PRE.prefab"),
            new Entry("Bush01", "Nature/Bushes/EA03_Nature_Bush_01b_PRE.prefab"),
            new Entry("Grass01", "Nature/Grass/EA03_Plant_Grass_01d_PRE.prefab"),
            new Entry("Bench01", "Prop/Furniture/EA03_Prop_Town_Bench_01b_PRE.prefab"),
            new Entry("Barrel01", "Prop/Container/EA03_Prop_Container_Barrel_01d_PRE.prefab"),
            new Entry("Mug01", "Items/Crockery/EA_Items_House_mug_02a_PRE.prefab"),
            new Entry("Cup01", "Items/Crockery/EA03_Items_House_Cup_01a_PRE.prefab")
        };

        [MenuItem("Pogranichye/Art/Install Slavic Village Prefabs")]
        public static void Install()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot + "Town/Building/EA03_Town_House_Comp_03a_PRE.prefab") == null)
            {
                EditorUtility.DisplayDialog("Slavic Village assets not found",
                    "Import 'Slavic Medieval Village Free Modular Environment Kit' first, then run this command again.",
                    "OK");
                return;
            }

            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder("Assets/_Project/Resources/PogranichyeArt");

            var installed = 0;
            var missing = new List<string>();
            try
            {
                for (var i = 0; i < Entries.Length; i++)
                {
                    var entry = Entries[i];
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Source);
                    if (source == null) { missing.Add(entry.Source); continue; }

                    var destination = DestinationRoot + entry.Key + ".prefab";
                    var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                    if (instance == null) { missing.Add(entry.Source + " (instantiate failed)"); continue; }
                    try
                    {
                        PrefabUtility.SaveAsPrefabAsset(instance, destination);
                        installed++;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            var message = "Installed " + installed + " presentation prefabs into Resources/PogranichyeArt.";
            if (missing.Count > 0) message += "\n\nNot found (" + missing.Count + "):\n" + string.Join("\n", missing.ToArray());
            EditorUtility.DisplayDialog("Pogranichye art install", message, "OK");
            EditorSceneManager.MarkAllScenesDirty();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var name = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
