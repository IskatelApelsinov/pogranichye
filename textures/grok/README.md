# textures/grok — procedural medieval textures (Grok)

Дата: 2026-11-14  
Автор: Grok (консультант)  
Назначение: временный/стартовый набор бесшовных 512×512 PNG под средневековое поселение и URP.

## Albedo

| Файл | Назначение |
|------|------------|
| `dirt_soil.png` | земля / тропинка |
| `grass_meadow.png` | луг / трава |
| `cobble_path.png` | булыжная дорога |
| `wood_planks.png` | доски / пол / забор |
| `thatch_roof.png` | соломенная крыша |
| `plaster_wall.png` | штукатурка стен |
| `stone_wall.png` | каменная кладка |
| `bark_tree.png` | кора деревьев |
| `cloth_linen.png` | грубый лён / ткань |
| `water_shallow.png` | мелкая вода |

## Normal maps

Пары `*_n.png` (OpenGL, Y+). Метод и strength: `3-dev/docs/NORMAL-MAPS-GENERATION.md`.

| Albedo | Normal |
|--------|--------|
| `dirt_soil.png` | `dirt_soil_n.png` |
| `grass_meadow.png` | `grass_meadow_n.png` |
| `cobble_path.png` | `cobble_path_n.png` |
| `wood_planks.png` | `wood_planks_n.png` |
| `thatch_roof.png` | `thatch_roof_n.png` |
| `plaster_wall.png` | `plaster_wall_n.png` |
| `stone_wall.png` | `stone_wall_n.png` |
| `bark_tree.png` | `bark_tree_n.png` |
| `cloth_linen.png` | `cloth_linen_n.png` |
| `water_shallow.png` | `water_shallow_n.png` |

## Где лежат бинарники

PNG (albedo + normals) у владельца в project artifacts: `textures/grok/`.  
В git — описание; бинарники добавятся после согласования пути в Unity (comms 0176/0177).

## Импорт URP

**Albedo:** Texture Type Default, sRGB On, Wrap Repeat, MipMaps On.  
**Normal:** Texture Type **Normal map**, sRGB **Off**, Wrap Repeat, MipMaps On.  
Материал URP/Lit: Base Map + Normal Map, Normal Scale ≈ 0.8–1.2.

Это стартовый procedural-набор для G2, не финальный hand-painted арт.
