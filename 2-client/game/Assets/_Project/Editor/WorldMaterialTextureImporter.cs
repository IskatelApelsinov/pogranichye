using UnityEditor;
using UnityEngine;

namespace Pogranichye.Client.Editor
{
    /// <summary>
    /// Applies consistent import settings to generated world PBR maps.
    /// The generator writes into Resources so runtime presentation can load maps
    /// without Addressables or a separate asset-bundle build.
    /// </summary>
    public sealed class WorldMaterialTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetPath == null ||
                !assetPath.Contains("/Resources/WorldMaterials/Textures/"))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 1024;

            var file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (file.EndsWith("_normal", System.StringComparison.OrdinalIgnoreCase))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = !file.EndsWith("_roughness", System.StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith("_ao", System.StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
