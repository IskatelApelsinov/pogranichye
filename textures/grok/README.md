# textures/grok — procedural medieval textures (Grok)

Дата: 2026-11-15  
Автор: Grok (консультант)  
Назначение: стартовый набор бесшовных 512×512 PNG (albedo + normal) под средневековое поселение, оружие, персонажей и URP.

Всего материалов: **58** (по паре albedo + normal).

## Террейн / природа

| Albedo | Normal |
|--------|--------|
| `dirt_soil.png` | `dirt_soil_n.png` |
| `grass_meadow.png` | `grass_meadow_n.png` |
| `sand_dune.png` | `sand_dune_n.png` |
| `gravel_road.png` | `gravel_road_n.png` |
| `mud_wet.png` | `mud_wet_n.png` |
| `snow_pack.png` | `snow_pack_n.png` |
| `moss_rock.png` | `moss_rock_n.png` |
| `rock_dark.png` | `rock_dark_n.png` |
| `ash_volcanic.png` | `ash_volcanic_n.png` |
| `cobble_path.png` | `cobble_path_n.png` |
| `cobble_wet.png` | `cobble_wet_n.png` |
| `leaf_canopy.png` | `leaf_canopy_n.png` |
| `hay_straw.png` | `hay_straw_n.png` |

## Здания / интерьер

| Albedo | Normal |
|--------|--------|
| `stone_wall.png` | `stone_wall_n.png` |
| `stone_ashlar.png` | `stone_ashlar_n.png` |
| `brick_red.png` | `brick_red_n.png` |
| `plaster_wall.png` | `plaster_wall_n.png` |
| `plaster_cracked.png` | `plaster_cracked_n.png` |
| `timber_frame.png` | `timber_frame_n.png` |
| `wattle_daub.png` | `wattle_daub_n.png` |
| `door_oak.png` | `door_oak_n.png` |
| `window_glass.png` | `window_glass_n.png` |
| `chimney_soot.png` | `chimney_soot_n.png` |
| `thatch_roof.png` | `thatch_roof_n.png` |
| `thatch_reed.png` | `thatch_reed_n.png` |
| `shingle_wood.png` | `shingle_wood_n.png` |
| `roof_clay.png` | `roof_clay_n.png` |
| `concrete_ruin.png` | `concrete_ruin_n.png` |
| `wood_planks.png` | `wood_planks_n.png` |
| `wood_oak.png` | `wood_oak_n.png` |

## Вода

| Albedo | Normal |
|--------|--------|
| `water_shallow.png` | `water_shallow_n.png` |
| `water_deep.png` | `water_deep_n.png` |

## Оружие / металл / кость

| Albedo | Normal |
|--------|--------|
| `steel_blade.png` | `steel_blade_n.png` |
| `iron_forged.png` | `iron_forged_n.png` |
| `iron_plate.png` | `iron_plate_n.png` |
| `bronze_metal.png` | `bronze_metal_n.png` |
| `metal_rust.png` | `metal_rust_n.png` |
| `bone_weapon.png` | `bone_weapon_n.png` |
| `wood_haft.png` | `wood_haft_n.png` |
| `leather_wrap.png` | `leather_wrap_n.png` |
| `chainmail.png` | `chainmail_n.png` |

## Персонажи / одежда

| Albedo | Normal |
|--------|--------|
| `skin_fair.png` | `skin_fair_n.png` |
| `skin_tan.png` | `skin_tan_n.png` |
| `skin_dark.png` | `skin_dark_n.png` |
| `hair_brown.png` | `hair_brown_n.png` |
| `hair_black.png` | `hair_black_n.png` |
| `hair_blond.png` | `hair_blond_n.png` |
| `cloth_linen.png` | `cloth_linen_n.png` |
| `cloth_wool.png` | `cloth_wool_n.png` |
| `cloth_rough.png` | `cloth_rough_n.png` |
| `cloth_fine.png` | `cloth_fine_n.png` |
| `fabric_red.png` | `fabric_red_n.png` |
| `leather_worn.png` | `leather_worn_n.png` |
| `leather_armor.png` | `leather_armor_n.png` |
| `fur_pelt.png` | `fur_pelt_n.png` |
| `eye_sclera.png` | `eye_sclera_n.png` |
| `bark_tree.png` | `bark_tree_n.png` |
| `bone_ivory.png` | `bone_ivory_n.png` |

## Где лежат бинарники

- **Project artifacts (Grok):** `artifacts/textures/grok/` — все PNG здесь, ~2.7 MB.
- **Git:** `textures/grok/` в репозитории `IskatelApelsinov/pogranichye`.

PNG нужно закоммитить в git (раньше в репо был только этот README). Скопировать из project artifacts в локальный клон:

```bash
# из корня клона pogranichye
cp -v /path/to/project/artifacts/textures/grok/*.png textures/grok/
git add textures/grok/*.png
git commit -m "textures/grok: full procedural set (albedo+normals, 58 materials)"
git push
```

## Импорт URP

**Albedo:** Texture Type Default, sRGB On, Wrap Repeat, MipMaps On.  
**Normal:** Texture Type **Normal map**, sRGB **Off**, Wrap Repeat, MipMaps On.  
Материал URP/Lit: Base Map + Normal Map, Normal Scale ≈ 0.8–1.2.

Это procedural placeholder-набор для прототипа, не финальный hand-painted арт.
