# Pogranichye world material generator

This source generator creates a first consistent set of seamless 512×512 PBR
materials for the Unity 6 client:

- meadow soil / grass blend
- oak bark
- aged oak planks
- mossy stone
- forest floor litter

Each material has Albedo, tangent-space Normal, Roughness and AO PNG maps.
The generator is deterministic and uses periodic functions so opposite edges
match. It uses no external textures, add-ons, or downloaded assets.

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

The maps are generated from a shared periodic height field. This is an
optimized stylized-realism base pass; it is not a substitute for later
hand-authored hero materials or scanned assets.
