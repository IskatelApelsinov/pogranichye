# Grok starter textures — Unity Resources

## Canonical source and import

The canonical binary pack is stored at the repository root in `grok/textures/grok/` (currently 116 PNG files: 58 albedo maps and 58 matching tangent-space normal maps).

Do not manually move the source pack into this folder. From the Unity Editor, run:

`Tools → Pogranichye → Art → Import Grok Texture Pack`

The command is implemented by `Assets/_Project/Editor/GrokTexturePackSync.cs`. It locates the repository root by walking upward from `Application.dataPath`, copies changed PNGs into this Resources folder, refreshes the AssetDatabase, and applies import settings. Re-run it after updating the source pack. The Resources copies are generated imports and may be recreated at any time.

Naming:
- Albedo: `dirt_soil.png`, `grass_meadow.png`, `cobble_path.png`, `wood_planks.png`, `thatch_roof.png`, etc.
- Tangent-space OpenGL Y+ normals: matching `*_n.png` files, e.g. `wood_planks_n.png`.

Import settings:
- Albedo: Default texture, sRGB On.
- Normal: Normal Map, sRGB Off.
- Both: Repeat, mipmaps, trilinear filtering, anisotropic level 8.
- Normal maps use positive tangent Y (OpenGL); do not flip green.

The runtime presentation builder loads maps from Resources when present. If imports are absent, existing procedural 64px materials remain in use. Authored project PBR maps are applied after Grok fallbacks and therefore keep priority.

Roughness/AO are not included in this starter pack. Do not treat albedo as roughness or derive a final material mask from it.

## Validation

After importing, verify in the Project window that both an albedo and its `_n` counterpart exist. Select the normal map and confirm Texture Type is **Normal map** and sRGB is disabled. Then enter Play Mode and inspect the mapped surfaces. The importer reports source file and map counts; this does not replace visual validation in Game View.
