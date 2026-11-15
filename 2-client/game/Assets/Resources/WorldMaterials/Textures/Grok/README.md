# Grok starter textures — Unity Resources

Place the original Grok PNG binaries in this folder:

- Albedo: `dirt_soil.png`, `grass_meadow.png`, `cobble_path.png`, `wood_planks.png`, `thatch_roof.png`, `plaster_wall.png`, `stone_wall.png`, `bark_tree.png`, `cloth_linen.png`, `water_shallow.png`
- Tangent-space OpenGL Y+ normals: matching `*_n.png` files, e.g. `wood_planks_n.png`

Unity import is handled by `WorldMaterialTextureImporter`:
- Albedo: Default, sRGB On
- Normal: Normal Map, sRGB Off
- Repeat, mipmaps, trilinear filtering
- Normal maps use positive tangent Y (OpenGL); do not flip green

The runtime presentation builder loads maps from Resources when present. If files are absent, existing procedural 64px materials remain in use. Authored project PBR maps are applied after Grok fallbacks and therefore keep priority.

Roughness/AO are not included in the first Grok package. Do not treat albedo as roughness or derive a final material mask from it.
