# Pogranichye world material generator

This source generator creates a first consistent set of seamless 512×512 PBR
materials for the Unity 6 client:

- meadow soil / grass blend
- oak bark
- aged oak planks
- mossy stone
- forest floor litter
- leaf canopy
- thatched roofing
- packed dirt road
- cobblestone paving
- limewashed plaster

The library contains ten material keys. The procedural fallback is 512×512; each key has Albedo, tangent-space Normal, Roughness and AO PNG maps. URP smoothness is the linear inverse of roughness and is stored in the albedo alpha channel; this avoids a redundant fifth texture.
The procedural fallback generator is deterministic and uses periodic functions
so opposite edges match; it needs no downloaded textures or Blender add-ons.
Six keys now have authored 1024×1024 overrides: `packed_road` (damp compacted earth),
`oak_planks` (weathered boards), `mossy_stone` (weathered fieldstone masonry), `lime_plaster` (aged limewash), `thatch_roof` (layered rye straw), and `forest_floor` (damp leaf litter). Their separate source-art builders create all four PBR maps and are preserved by this Blender generator when the complete map sets are present. The procedural versions remain safe fallbacks.

## Generate

From the repository root:

```bash
blender --background --python 2-client/art-source/blender/generate_world_materials.py
```

Output is written directly to
`2-client/game/Assets/Resources/WorldMaterials/`. The PNG maps are therefore
available through `Resources.Load` at runtime. Unity's
`WorldMaterialTextureImporter` configures Repeat wrapping, mipmaps, normal-map
import, and linear data-map colour space automatically.

## Unity import

Unity imports the PNG files automatically. Set:
- Albedo: sRGB enabled, Wrap Mode Repeat, mipmaps enabled.
- Normal: Texture Type = Normal map.
- Roughness and AO: sRGB disabled automatically by the editor importer.
- Roughness is not Unity's Smoothness. A shader/material setup must invert it
  when feeding a Smoothness slot (Smoothness = 1 - Roughness).

The procedural fallback maps are generated from a shared periodic height field. Five authored 1024×1024 overrides are now defined: packed earth for generated settlement roads/tavern floors, aged oak boards for shared timber surfaces, weathered fieldstone for stonework, aged limewash for generated building wall planes, and layered rye thatch for building roofs, straw props and server-authored roof surfaces. Each override has albedo, tangent-space normal, roughness and AO; smoothness (`1 - roughness`) is packed into the linear albedo alpha channel for URP Lit. The manifest records each per-material resolution and source builder. The Blender generator preserves complete authored map sets rather than overwriting them with procedural noise.

For all authored materials, see [../textures/README.md](../textures/README.md).

The forest-floor source is a dense woodland litter composition: curled oak/birch leaves, pine needles, twigs, roots, bark fragments, small stones and moss over dark damp soil. `build_forest_floor_material.py` derives Albedo, tangent-space Normal, Roughness and AO from one repaired tileable source. Smoothness (`1 - roughness`) is packed into albedo alpha. The source and complete 1024×1024 maps are distributed in the forest-floor texture pack; the repository manifest remains `pending` until the pack is installed.

The Unity client uses a separate `_forestFloor` material for server-authored `forest` zone ground patches. A single quad per forest zone uses world-aligned UVs at four metres per repeat, while `_forest` remains dedicated to the animated canopy. Copses keep their lighter grass floor. This avoids changing foliage colour or introducing per-tree materials.

The procedural baseline remains a fallback for the other materials; it is not a substitute for later hand-authored hero materials or scanned assets.
