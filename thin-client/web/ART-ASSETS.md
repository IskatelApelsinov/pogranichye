# Thin-client village art assets

All assets in this folder are local SVGs: no external fonts, image hosts, runtime packages or network fetches are required.

## Art direction

- Cozy frontier settlement, readable at phone width.
- Pixel-art-inspired silhouettes with crisp edges and hand-painted earth tones.
- Grass/forest greens, weathered timber, warm amber tavern light, river teal, parchment text.
- Keep gameplay state and controls in HTML; SVGs are presentation only.

## Files

- `village-scene.svg` — responsive illustrated village overview used as the current scene banner.
- `village-atlas.svg` — reusable 4×4 sprite atlas. Each cell is 96×96 px in a 384×384 viewBox.

## Atlas cell map

| Row | Column | Sprite |
|---:|---:|---|
| 1 | 1 | Grass tile |
| 1 | 2 | Dirt path |
| 1 | 3 | Stone ground |
| 1 | 4 | Water |
| 2 | 1 | Crop bed |
| 2 | 2 | Wood planks |
| 2 | 3 | Thatched roof |
| 2 | 4 | Stone wall |
| 3 | 1 | Tree |
| 3 | 2 | Cottage |
| 3 | 3 | Tavern |
| 3 | 4 | Forge |
| 4 | 1 | Market stall |
| 4 | 2 | Well |
| 4 | 3 | Barrel |
| 4 | 4 | Beer mug |

CSS background coordinates can use 25% steps because the atlas has four columns and four rows. For a canvas/tile renderer, source rectangles are 96×96 and should be scaled with nearest-neighbour sampling.

## Integration boundaries

The art does not create movement, collisions, NPC actions, inventory, minigames, beer purchases or intoxication. Keep those behind server-authoritative intents/results. The existing tavern action remains the supported v1 `socialize` intent until a drink contract is agreed.
