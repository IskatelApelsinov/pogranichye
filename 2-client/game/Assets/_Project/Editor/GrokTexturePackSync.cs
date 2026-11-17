using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pogranichye.Client.Editor
{
    /// <summary>
    /// Synchronizes the canonical repository-level Grok PNG pack into Unity Resources.
    /// The source remains in /grok/textures/grok; generated copies are disposable imports.
    /// </summary>
    [InitializeOnLoad]
    public static class GrokTexturePackSync
    {
        private const string SourceRelativePath = "grok/textures/grok";
        private const string DestinationAssetPath = "Assets/Resources/WorldMaterials/Textures/Grok";

        static GrokTexturePackSync()
        {
            // The runtime Resources folder is generated from the canonical repository pack.
            // Sync automatically after Unity loads so a fresh checkout does not silently run
            // with the low-resolution procedural fallback until someone finds the menu item.
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    ImportPack(false);
            };
        }

        [MenuItem("Tools/Pogranichye/Art/Import Grok Texture Pack")]
        public static void ImportPack()
        {
            ImportPack(true);
        }

        private static void ImportPack(bool showDialog)
        {
            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                Report("Grok texture pack not found",
                    "Could not find the repository-level folder 'grok/textures/grok'. " +
                    "Keep the canonical PNG pack in that folder and run this command from the Unity project.",
                    showDialog, true);
                return;
            }

            var source = Path.Combine(repositoryRoot.FullName, SourceRelativePath);
            var files = Directory.GetFiles(source, "*.png", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                Report("Grok texture pack is empty", "No PNG files were found in:\n" + source, showDialog, true);
                return;
            }

            var destination = Path.Combine(repositoryRoot.FullName,
                DestinationAssetPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(destination);

            var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files) sourceNames.Add(Path.GetFileName(file));

            var copied = 0;
            var removed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var file in files)
                {
                    var target = Path.Combine(destination, Path.GetFileName(file));
                    if (!File.Exists(target) || !FilesEqual(file, target))
                    {
                        File.Copy(file, target, true);
                        copied++;
                    }
                }

                // Keep the generated Resources folder an exact PNG mirror of the canonical source.
                foreach (var existing in Directory.GetFiles(destination, "*.png", SearchOption.TopDirectoryOnly))
                {
                    if (sourceNames.Contains(Path.GetFileName(existing))) continue;
                    File.Delete(existing);
                    removed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var reimported = 0;
            foreach (var file in files)
            {
                var assetPath = DestinationAssetPath + "/" + Path.GetFileName(file);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;

                var isNormal = Path.GetFileNameWithoutExtension(file)
                    .EndsWith("_n", StringComparison.OrdinalIgnoreCase);
                var desiredType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                var desiredSrgb = !isNormal;
                if (importer.textureType == desiredType &&
                    importer.sRGBTexture == desiredSrgb &&
                    importer.mipmapEnabled &&
                    importer.wrapMode == TextureWrapMode.Repeat &&
                    importer.filterMode == FilterMode.Trilinear &&
                    importer.anisoLevel == 8 &&
                    importer.maxTextureSize == 2048 &&
                    !importer.alphaIsTransparency)
                    continue;

                importer.textureType = desiredType;
                importer.sRGBTexture = desiredSrgb;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 2048;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
                reimported++;
            }

            var baseMaps = 0;
            var normalMaps = 0;
            foreach (var file in files)
            {
                if (Path.GetFileNameWithoutExtension(file).EndsWith("_n", StringComparison.OrdinalIgnoreCase))
                    normalMaps++;
                else
                    baseMaps++;
            }

            var missingNormals = 0;
            foreach (var file in files)
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.EndsWith("_n", StringComparison.OrdinalIgnoreCase)) continue;
                if (!sourceNames.Contains(stem + "_n.png")) missingNormals++;
            }

            Report("Grok texture pack imported",
                "Source: " + SourceRelativePath + "\n" +
                "PNG files: " + files.Length + " (" + baseMaps + " albedo, " + normalMaps + " normal)\n" +
                "Copied/updated: " + copied + "\n" +
                "Removed stale copies: " + removed + "\n" +
                "Import settings changed: " + reimported + "\n" +
                "Albedo without matching normal: " + missingNormals + "\n\n" +
                "Runtime Resources path: " + DestinationAssetPath,
                showDialog, false);
        }

        private static void Report(string title, string message, bool showDialog, bool warning)
        {
            if (showDialog)
                EditorUtility.DisplayDialog(title, message, "OK");
            else if (warning)
                Debug.LogWarning("[Pogranichye] " + title + ": " + message);
            else
                Debug.Log("[Pogranichye] " + title + ": " + message);
        }

        private static DirectoryInfo FindRepositoryRoot()
        {
            var current = new DirectoryInfo(Application.dataPath);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, SourceRelativePath)))
                    return current;
                current = current.Parent;
            }
            return null;
        }

        private static bool FilesEqual(string left, string right)
        {
            var leftInfo = new FileInfo(left);
            var rightInfo = new FileInfo(right);
            if (leftInfo.Length != rightInfo.Length) return false;

            const int bufferSize = 81920;
            using (var a = File.OpenRead(left))
            using (var b = File.OpenRead(right))
            {
                var bufferA = new byte[bufferSize];
                var bufferB = new byte[bufferSize];
                int readA;
                while ((readA = a.Read(bufferA, 0, bufferA.Length)) > 0)
                {
                    var readB = b.Read(bufferB, 0, bufferB.Length);
                    if (readA != readB) return false;
                    for (var i = 0; i < readA; i++)
                        if (bufferA[i] != bufferB[i]) return false;
                }
                return b.ReadByte() == -1;
            }
        }
    }
}
