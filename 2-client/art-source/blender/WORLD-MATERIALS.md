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

The library contains ten materials. The procedural baseline maps are 512×512; each has Albedo, tangent-space Normal, Roughness and AO PNG maps. URP smoothness is the linear inverse of roughness and is stored in the albedo alpha channel; this avoids a redundant fifth texture.
The procedural baseline generator is deterministic and uses periodic functions
so opposite edges match; it needs no downloaded textures or Blender add-ons.
The authored packed-road override is the explicit exception and is maintained
by the separate source-art builder described below.

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

The procedural baseline maps are generated from a shared periodic height field. The packed-road material is an authored artwork override at 1024×1024: its albedo is generated separately, seam-repaired, and paired with derived normal, roughness and AO maps. The manifest records this per-material resolution. The Blender generator preserves that complete authored map set rather than overwriting it with procedural noise.

For the authored packed-road material, see
[../textures/README.md](../textures/README.md). It is used by the generated settlement roads and the tavern packed-earth floor through `WorldLayoutPresentationBuilder`.

The procedural baseline remains a fallback for the other materials; it is not a substitute for later hand-authored hero materials or scanned assets.
