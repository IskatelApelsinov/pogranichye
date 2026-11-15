using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pogranichye.Client.Editor
{
    /// <summary>
    /// Synchronizes the canonical repository-level Grok PNG pack into Unity Resources.
    /// The source remains in /grok/textures/grok; generated copies are disposable imports.
    /// </summary>
    public static class GrokTexturePackSync
    {
        private const string SourceRelativePath = "grok/textures/grok";
        private const string DestinationAssetPath = "Assets/Resources/WorldMaterials/Textures/Grok";

        [MenuItem("Tools/Pogranichye/Art/Import Grok Texture Pack")]
        public static void ImportPack()
        {
            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Grok texture pack not found",
                    "Could not find the repository-level folder 'grok/textures/grok'. " +
                    "Keep the canonical PNG pack in that folder and run this command from the Unity project.",
                    "OK");
                return;
            }

            var source = Path.Combine(repositoryRoot.FullName, SourceRelativePath);
            var files = Directory.GetFiles(source, "*.png", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("Grok texture pack is empty", "No PNG files were found in:\n" + source, "OK");
                return;
            }

            var destination = Path.Combine(repositoryRoot.FullName,
                DestinationAssetPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(destination);

            var copied = 0;
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
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var configured = 0;
            foreach (var file in files)
            {
                var assetPath = DestinationAssetPath + "/" + Path.GetFileName(file);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;

                var isNormal = Path.GetFileNameWithoutExtension(file)
                    .EndsWith("_n", StringComparison.OrdinalIgnoreCase);
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !isNormal;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 2048;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
                configured++;
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

            EditorUtility.DisplayDialog(
                "Grok texture pack imported",
                "Source: " + SourceRelativePath + "\n" +
                "PNG files: " + files.Length + " (" + baseMaps + " albedo, " + normalMaps + " normal)\n" +
                "Copied/updated: " + copied + "\n" +
                "Import settings applied: " + configured + "\n\n" +
                "Runtime Resources path: " + DestinationAssetPath,
                "OK");
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
