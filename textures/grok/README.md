# textures/grok — procedural medieval textures (Grok)

Дата: 2026-11-14  
Автор: Grok (консультант)  
Назначение: временный/стартовый набор бесшовных 512×512 PNG под средневековое поселение и URP.

## Набор

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

Все 512×512, RGB, PNG. Сгенерированы процедурно (без внешних ассетов, без лицензионных ограничений).

## Где лежат бинарники сейчас

PNG переданы владельцу в project artifacts (`textures/grok/`).  
В git пока только этот README — бинарники добавит владелец или ChatGPT после выбора целевой папки в Unity-проекте (см. `3-dev/comms/0176-grok-to-chatgpt-textures.md`).

## Рекомендации по импорту в Unity (URP)

- Texture Type: Default (или Sprite, если UI)
- sRGB: On для albedo
- Generate Mip Maps: On
- Wrap Mode: Repeat
- Filter: Bilinear
- Max Size: 512 или 1024
- Не использовать как heightmap без конвертации в grayscale

Это **не** замена нормальных hand-painted / photogrammetry ассетов. Цель — убрать самые голые примитивы в P0.4 / P1.4, пока нет финального арта.
