# Авторская текстура утрамбованной земли

Исходник — новая AI-сгенерированная художественная текстура: влажная утрамбованная земля с мелким гравием, редкими соломинками, опавшими листьями и неглубокими следами. Это не процедурный шум из старого Blender-генератора.

## Состав

Скрипт `build_packed_road_material.py` создаёт в
`2-client/game/Assets/Resources/WorldMaterials/Textures/`:

- `packed_road_albedo.png` — 1024×1024 RGBA; RGB — цвет, A — линейная smoothness (`1 - roughness`);
- `packed_road_normal.png` — tangent-space normal, positive Y в зелёном канале;
- `packed_road_roughness.png` — отдельная линейная roughness-карта;
- `packed_road_ao.png` — отдельная линейная AO-карта;
- `packed_road_material.json` — параметры и контроль шва.

Противоположные края исходной картинки сведены в центр и исправлены inpainting; затем из общего height/detail поля выводятся согласованные normal, roughness и AO. Albedo остаётся отдельной цветовой картой, а не подменяет весь PBR-набор.

## Пересборка

Нужны Python 3.10+, Pillow, NumPy и OpenCV:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_packed_road_material.py
```

Исходник `packed_road_source.jpg` лежит рядом со скриптом. Unity импортирует карты автоматически через `WorldMaterialTextureImporter`: Repeat, mipmaps, trilinear, anisotropic 8; normal получает тип Normal Map, roughness/AO — linear. Albedo RGB остаётся sRGB, alpha не трактуется как прозрачность.

## Подключение в мире

`WorldLayoutPresentationBuilder` загружает карты по ключу `packed_road` для:
- деревенских и межобъектных грунтовых дорог;
- утрамбованного пола таверны.

UV tiling сохраняется: 2×12 для дорожного материала. URP Lit читает smoothness из albedo alpha через `_SmoothnessTextureChannel = 1`, `_Smoothness = 1`, metallic = 0. Отдельная roughness-карта остаётся доступной для будущих шейдеров/экспорта.

Если полный authored map set уже присутствует, Blender baseline generator не перезаписывает его процедурным вариантом.
