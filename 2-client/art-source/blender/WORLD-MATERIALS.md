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
Four source-art builder scripts describe 1024×1024 overrides for `packed_road`, `oak_planks`, `mossy_stone` and `lime_plaster`, but the current repository does not contain their required source JPGs or generated PNG map sets. The manifests present for `packed_road` and `lime_plaster` do not make those textures available at runtime. Treat these authored overrides as pending until source artwork and all PBR maps are committed and verified from a clean checkout. The runtime uses imported Grok starter textures where wired; otherwise it falls back to its small procedural presentation textures.

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

The procedural fallback maps are generated from a shared periodic height field. The four authored builders are intended to produce 1024×1024 albedo, tangent-space normal, roughness and AO maps, with smoothness (`1 - roughness`) packed into albedo alpha for URP Lit. However, their source JPGs and generated PNG sets are not currently committed; the manifests alone do not provide runtime textures. The Blender generator preserves complete authored map sets when they are actually present. See issue [#722](https://github.com/IskatelApelsinov/pogranichye/issues/722) for the asset gap and acceptance criteria.

For all authored materials, see [../textures/README.md](../textures/README.md).

The procedural baseline remains a fallback for the other materials; it is not a substitute for later hand-authored hero materials or scanned assets.
