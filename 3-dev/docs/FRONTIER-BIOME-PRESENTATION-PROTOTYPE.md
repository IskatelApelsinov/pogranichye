# Frontier biome presentation prototype

Date: 2026-11-16  
Client: Unity 6.6 / URP  
Status: first visual test pass; requires Unity Editor and PlayMode verification.

## Purpose

Add enough distinct vegetation to the server-driven frontier that a developer can walk between terrain chunks and inspect biome appearance, chunk edges, LOD changes, and streaming unload/reload. This is a client presentation layer only.

The builder consumes the optional `/api/terrain` descriptors already present in `TerrainChunkFrame`:
- `seed`, `gen`, `biome`;
- `cover.forest/grass/rock/wet`;
- `roads` and `water` polylines, used as vegetation exclusion corridors.

No server contract changes are required. Existing frames without descriptors remain unchanged.

## Current server biome values

The server currently emits these biome keys from `sim/worldpos.go` / `sim/terrain_content.go`:
- `forest` — mixed forest;
- `plain` — open meadow;
- `hills` — rocky/highland transition;
- `coast` — coastal vegetation;
- `steppe` — dry open grassland.

The client normalizes these to presentation profiles. The builder also recognizes descriptive aliases (marsh/swamp, coniferous/taiga, deciduous/broadleaf, alpine/mountain) so future server keys can use them without changing the renderer.

## What is rendered

`WorldFrontierBiomeBuilder` creates one disposable presentation root per terrain chunk. It combines geometry into a small number of meshes instead of creating a GameObject for every plant.

- Forest: low-poly spruce/fir, birch/aspen, broadleaf crowns; shrubs and undergrowth.
- Hills: sparser mountain pine, scrub, exposed stone groups.
- Plain: open grass clumps, sparse trees and low bushes.
- Coast: scattered coastal trees and grass/reed-like clumps.
- Steppe: very sparse trees and taller dry-grass clumps.
- Marsh aliases: willow-like trees and reed clusters.

All generated objects are visual only. There are no harvest interactions, authoritative resource entities, POIs, or vegetation colliders. Server roads and water corridors are kept visually clear; this is not a replacement for server pathfinding or water geometry.

## LOD and budgets

Each 256 m server chunk receives one `LODGroup` with three combined geometry levels:
- LOD0: layered crowns, trunks, detailed shrubs/grass and rocks;
- LOD1: simplified crowns/trunks and reduced undergrowth;
- LOD2: compact distant tree silhouettes; small detail is omitted.

Current LOD screen-relative thresholds are 0.22 / 0.075 / 0.018 and cross-fade is enabled. Per-chunk generation caps are 420 trees, 700 understory/grass clumps and 260 rock groups. These are safety ceilings, not expected counts.

## Determinism and boundaries

Placement uses the server seed (or a stable chunk-coordinate fallback) plus global integer placement cells. It does not use Unity's runtime random state. A plant's candidate location is therefore stable after reconnect and reload, and its grid phase is shared across neighboring chunks, including negative world coordinates.

Height is sampled from the authoritative chunk heightfield. Vegetation is excluded around server-provided road and water polylines. Mesh vertices are expressed relative to the same settlement anchor as the terrain chunk.

The server currently supplies one biome label per chunk. A biome boundary can therefore change species/palette between chunks; the renderer does not invent a blended biome map. Test those boundaries visually and record abrupt changes separately from actual mesh/placement seams.

## Editor preview strip

For fast visual inspection without traveling to each region, use the Unity menu:
- `Pogranichye → Debug → Build Frontier Biome Test Strip`
- Five 256 m fixture chunks are placed in a row along +Z: forest, plain, hills, coast, steppe.
- A simple test trail crosses each chunk so you can check that vegetation respects path corridors.
- Select the generated `FrontierBiomePreview_TEST_ONLY` root and move the Scene/Game camera along +Z.
- Remove it with `Pogranichye → Debug → Clear Frontier Biome Test Strip`.

These fixtures exist only in the local Editor scene. They are not sent to the server and do not change the live world. The preview uses the same terrain and vegetation builders as streamed chunks.

## Unity test checklist

1. First build the Editor preview strip and inspect all five current server biome profiles. and confirm descriptors are present in the received terrain frames (`biome`, `seed`, `cover`).
2. Walk across all four edges of a terrain chunk. Check for repeated rows, gaps, sudden density jumps, and plants crossing the road/water corridors.
3. Walk through forest → plain → hills → coast/steppe regions where the server world provides them. Confirm species and density visibly change.
4. Move toward and away from a tree line. Confirm all three LOD levels switch without a large pop or missing renderers.
5. Cross a chunk boundary repeatedly and reconnect. Confirm vegetation does not duplicate, drift, or change its deterministic layout.
6. Watch Profiler: count vegetation roots/renderers/meshes, triangles, GC allocations and frame time. There should be a handful of renderers per LOD per chunk, not hundreds of plant GameObjects.
7. Check low-end hardware and the URP shadow cost. If frame time is high, first reduce LOD0 shadow casting and generation caps.

## Known limitations

- No imported tree/foliage assets yet; this pass uses generated low-poly meshes so it works without external downloads or licensing dependencies.
- No billboard impostor atlas; LOD2 is a very cheap mesh silhouette.
- No per-biome seasonal palette or snow layer yet.
- No terrain grass shader/GPU Resident Drawer integration yet.
- The renderer does not generate authoritative collision, harvestable resources, or biome transitions. The Unity Editor/PlayMode test remains required before treating this as verified.
