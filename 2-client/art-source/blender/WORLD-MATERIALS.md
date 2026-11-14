# Pogranichye world material library

The Unity 6 client has a deterministic Blender generator for ten seamless 512×512
world surfaces: meadow soil, oak bark, aged oak planks, mossy stone, forest floor,
leaf canopy, thatch, packed dirt road, cobblestone and limewashed plaster. It
produces Albedo, tangent-space Normal, Roughness and AO maps (40 maps total).
Those ten procedural sets are source-generated; they are not claimed to be
checked-in PNGs until the Blender generator is run.

## New authored material: homespun linen

A separate, image-generated linen weave with periodic edge processing is now committed for
the generated market awning, tutorial scarecrow tunic and windmill canvas sail. Its warm, undyed flax/burlap tone
fits rural fabric rather than modern synthetic cloth.

- `Resources/WorldMaterials/Textures/homespun_linen_albedo.jpg`: 192×192 sRGB
  albedo, tileable, with broad lighting flattened before export.
- `Resources/WorldMaterials/Textures/homespun_linen_normal.png`: 128×128
  tangent-space normal, imported by Unity as a native NormalMap.
- `Resources/WorldMaterials/Textures/homespun_linen_pbr.png`: 128×128 linear
  packed mask. **R = AO, G = roughness**; B/A are reserved.
- The runtime converts roughness to URP Lit smoothness as `1 - roughness`.
  AO and smoothness are split once and cached for reuse across streamed layouts.

The color image is larger than the data pack because the woven color detail is
the most visible near-field signal. The packed data texture is kept smaller to
limit memory; mipmaps and trilinear filtering remain enabled.

## Generate procedural world materials

From the repository root:

```bash
blender --background --python 2-client/art-source/blender/generate_world_materials.py
```

Output is written directly to
`2-client/game/Assets/Resources/WorldMaterials/`. When the committed linen maps
are present, the generator preserves them and includes their paths/channel layout
in `material_manifest.json` alongside the ten generated materials.

## Unity import

`WorldMaterialTextureImporter` configures Repeat wrapping, mipmaps, trilinear
filtering and anisotropy. Albedo uses sRGB; all data maps are linear. The packed
linen packed mask is readable and uncompressed so the client can split AO and
roughness into native URP Lit inputs once and cache them. The separate normal
map is imported using Unity's NormalMap pipeline.

This is a stylized historical-rural material, not scanned photogrammetry. The
new linen set is an authored image texture; the other ten remain deterministic
procedural source materials until generated locally with Blender.
