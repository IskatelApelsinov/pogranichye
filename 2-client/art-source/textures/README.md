# Авторская текстура утрамбованной земли

Исходник — оригинальная AI-сгенерированная художественная поверхность: влажная утрамбованная земля, мелкий гравий, отдельные камни, соломинки, корешки и редкие опавшие листья. Это не синусоидальный процедурный шум.

## Состав PBR-набора

Скрипт `build_packed_road_material.py` создаёт в
`2-client/game/Assets/Resources/WorldMaterials/Textures/`:

- `packed_road_albedo.png` — 1024×1024 RGBA; RGB — цвет, A — линейная smoothness (`1 - roughness`);
- `packed_road_normal.png` — tangent-space normal, positive Y в зелёном канале;
- `packed_road_roughness.png` — отдельная линейная roughness-карта;
- `packed_road_ao.png` — отдельная линейная AO-карта;
- `packed_road_material.json` — параметры, диапазоны и контроль непрерывности края.

Albedo, normal, roughness и AO получены из одного художественного исходника и общей карты рельефа. Одна цветная картинка не выдаётся за полный PBR-набор.

## Бесшовность

Исходные края перемещаются в центр изображения. Вместо inpainting, который на этом исходнике давал заметную размытую тёмную полосу, используется детерминированная замена центрального шва фрагментами из внутренней области с мягким cosine-feather переходом.

Для текущего исходника средний перепад RGB на границе повторения составляет 19.88 (горизонталь) и 23.47 (вертикаль) уровней из 255; средний перепад между соседними внутренними пикселями — 18.58 и 20.42. Отношения шва к внутренней вариации: 1.07 и 1.15. Это сохраняет мелкий рельеф вместо искусственно размытой полосы. 2×2 preview проверен визуально.

## Пересборка

Нужны Python 3.10+, Pillow, NumPy и OpenCV:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_packed_road_material.py
```

Исходник `packed_road_source.jpg` лежит рядом со скриптом. Генератор воспроизводимо пишет карты прямо в Unity Resources.

Unity импортирует PNG автоматически через `WorldMaterialTextureImporter`: Repeat, mipmaps, trilinear, anisotropic 8; normal получает тип Normal Map, roughness/AO — linear. Albedo RGB остаётся sRGB, alpha не трактуется как прозрачность.

## Подключение в мире

`WorldLayoutPresentationBuilder` загружает `packed_road` для:
- деревенских и межобъектных грунтовых дорог;
- утрамбованного пола таверны.

UV tiling дороги сохраняется: 2×12. URP Lit читает smoothness из albedo alpha через `_SmoothnessTextureChannel = 1`, `_Smoothness = 1`, metallic = 0. Отдельная roughness-карта остаётся доступной для других шейдеров и экспорта.

Если полного набора карт нет, текущая сцена остаётся работоспособной на прежнем материале.
