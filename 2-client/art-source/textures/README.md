## Статус чистого checkout — 2026-11-18

В библиотеке определены семь авторских PBR-наборов: packed_road, oak_planks, mossy_stone, lime_plaster, thatch_roof и forest_floor. Генераторы и манифесты находятся в репозитории, но чистый checkout пока не содержит source JPG и готовые 1024×1024 PNG-карты; соответствующие манифесты остаются availability: pending. Поэтому не считайте описания ниже подтверждением наличия бинарных карт в Git.

Пока полный набор не установлен, Unity использует импортированный Grok fallback (если выполнен Tools → Pogranichye → Art → Import Grok Texture Pack), иначе — существующий procedural fallback. 512px карты master generator остаются preview/fallback и не становятся авторским PBR override. Авторский набор получает приоритет только при наличии source artwork, всех четырёх 1024×1024 карт и манифеста availability: available.

Проверка чистого checkout и полноты доступных наборов:

```bash
python3 -m unittest discover -s 2-client/art-source/textures -p 'test_*.py'
python3 2-client/art-source/textures/validate_world_material_assets.py
```

# Авторская текстура утрамбованной земли

Целевой вид исходника (packed_road_source.jpg пока отсутствует в чистом checkout): влажная утрамбованная земля, мелкий гравий, отдельные камни, соломинки, корешки и редкие опавшие листья. Это не синусоидальный процедурный шум.

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

Для текущего исходника отношения среднего перепада на шве к обычному внутреннему перепаду (горизонталь / вертикаль): albedo — 1.07 / 1.15; normal — 1.08 / 1.10; roughness — 1.05 / 1.13; AO — 1.04 / 1.07. Все четыре карты проверяются генератором отдельно; это сохраняет мелкий рельеф вместо искусственно размытой полосы. 2×2 preview проверен визуально.

## Пересборка

Нужны Python 3.10+, Pillow, NumPy и OpenCV:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_packed_road_material.py
```

Ожидаемый packed_road_source.jpg должен лежать рядом со скриптом; в текущем main этот файл отсутствует. Генератор воспроизводимо пишет карты прямо в Unity Resources.

Unity импортирует PNG автоматически через `WorldMaterialTextureImporter`: Repeat, mipmaps, trilinear, anisotropic 8; normal получает тип Normal Map, roughness/AO — linear. Albedo RGB остаётся sRGB, alpha не трактуется как прозрачность.

## Подключение в мире

`WorldLayoutPresentationBuilder` загружает `packed_road` для:
- деревенских и межобъектных грунтовых дорог;
- утрамбованного пола таверны.

UV tiling дороги сохраняется: 2×12. URP Lit читает smoothness из albedo alpha через `_SmoothnessTextureChannel = 1`, `_Smoothness = 1`, metallic = 0. Отдельная roughness-карта остаётся доступной для других шейдеров и экспорта.

Если полного набора карт нет, текущая сцена остаётся работоспособной на прежнем материале.


## Авторская текстура состаренных дубовых досок

Целевой вид исходника oak_planks_source.jpg (файл пока отсутствует в чистом checkout): глубокие стыки, продольные волокна, сучки, трещины, потёртости и отверстия от старого крепежа. Это не синусоидальная процедурная подмена.

Скрипт `build_oak_planks_material.py` создаёт в Unity Resources:

- `oak_planks_albedo.png` — 1024×1024 RGBA; RGB — authored base color, A — линейная smoothness (`1 - roughness`);
- `oak_planks_normal.png` — tangent-space normal, positive tangent Y;
- `oak_planks_roughness.png` — отдельная линейная roughness;
- `oak_planks_ao.png` — отдельная линейная AO;
- `oak_planks_material.json` — диапазоны PBR-карт и отчёт бесшовности.

Для URP Lit включается Albedo Alpha smoothness (`_SmoothnessTextureChannel = 1`, `_Smoothness = 1`, metallic = 0). Отдельная roughness-карта сохраняется для других шейдеров и экспорта. Переключение alpha выполняется только при наличии соответствующего `*_material.json`, чтобы старые PNG с непрозрачной alpha не стали неожиданно глянцевыми. `WorldMaterialTextureImporter` уже задаёт Repeat, mipmaps, trilinear, anisotropic 8, Normal Map и linear data для roughness/AO.

Пересборка требует Python 3.10+, Pillow, NumPy и OpenCV:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_oak_planks_material.py
```

Ожидаемый oak_planks_source.jpg должен лежать рядом со скриптом; в текущем main этот файл отсутствует. Генератор берёт четыре целые доски между исходными продольными стыками, затем ремонтирует повтор по обеим осям выровненными внутренними фрагментами с cosine feather. Он проверяет каждую карту и завершает работу ошибкой, если отношение перепада на шве к внутреннему перепаду превышает 1.35.

## Подключение в мире

`WorldLayoutPresentationBuilder` загружает `oak_planks` для общего timber-материала: деревянные поверхности сгенерированных зданий и мостика, а также других элементов, использующих `_timber`. Сначала применяется Grok starter texture как fallback, затем авторский набор с приоритетом. UV tiling остаётся 2×2. URP Lit получает smoothness из albedo alpha; нормаль и AO подключаются к штатным слотам.

Если PNG-набор ещё не распакован в Resources, проект остаётся работоспособным на Grok/процедурном fallback. Для установки готового набора распакуй архив `pogranichye_oak_planks_texture_pack.zip` в корень репозитория с сохранением путей.

## Авторская текстура старой каменной кладки

Целевой вид исходника stone_masonry_source.jpg (файл пока отсутствует в чистом checkout): серый сланец, тёплый известняк и песчаник, выветренные грани, минеральные прожилки, углублённый известковый раствор и сдержанные участки мха. Это не процедурная синусоидальная имитация.

`build_mossy_stone_material.py` создаёт в Unity Resources:

- `mossy_stone_albedo.png` — 1024×1024 RGBA; RGB — цвет, A — линейная smoothness (`1 - roughness`);
- `mossy_stone_normal.png` — tangent-space normal, positive tangent Y;
- `mossy_stone_roughness.png` — отдельная линейная roughness;
- `mossy_stone_ao.png` — отдельная линейная AO;
- `mossy_stone_material.json` — диапазоны PBR-карт и контроль швов.

Все карты получаются из одного исходника и общей карты рельефа. Швы исправляются внутренними фрагментами с cosine-feather переходом; генератор отдельно проверяет четыре карты и прекращает работу при отношении перепада на шве к внутреннему перепаду выше 1.35.

Пересборка требует Python 3.10+, Pillow, NumPy и OpenCV:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_mossy_stone_material.py
```

Ожидаемый stone_masonry_source.jpg должен находиться рядом со скриптом; в текущем main этот файл отсутствует. Карты записываются прямо в `2-client/game/Assets/Resources/WorldMaterials/Textures/`; `WorldMaterialTextureImporter` задаёт Repeat, mipmaps, trilinear, anisotropic 8, Normal Map и linear для roughness/AO.

## Подключение в мире

`WorldLayoutPresentationBuilder` использует `mossy_stone` для общего материала каменной кладки поселения, включая каменные цоколи и кладку кузницы. Сохраняется существующий UV tiling 1.5×2. URP Lit читает smoothness из albedo alpha через `_SmoothnessTextureChannel = 1`; metallic = 0. Отдельная roughness-карта остаётся доступной для других шейдеров и экспорта.

Если полный набор карт или `mossy_stone_material.json` отсутствует, остаётся прежний fallback. Blender master generator сохраняет полный авторский набор и не перезаписывает его процедурной версией.



## Авторская состаренная известковая штукатурка

Целевой вид исходника lime_plaster_source.jpg (файл пока отсутствует в чистом checkout): тёплый кремовый известковый слой, пигментные охристые пятна, мелкие трещины, потёртые гребни и зернистые следы ручного нанесения. Это отдельный художественный исходник, а не процедурный синусоидальный узор.

Скрипт `build_lime_plaster_material.py` создаёт в Unity Resources:

- `lime_plaster_albedo.png` — 1024×1024 RGBA; RGB — albedo, A — линейная smoothness (`1 - roughness`);
- `lime_plaster_normal.png` — tangent-space normal, positive Y;
- `lime_plaster_roughness.png` — отдельная линейная roughness;
- `lime_plaster_ao.png` — отдельная линейная AO;
- `lime_plaster_material.json` — PBR-настройки и измерения швов.

Все карты выведены из одного художественного исходника и общей карты высот. Для URP Lit smoothness хранится в alpha albedo, а отдельная roughness остаётся для других шейдеров и экспорта. Проверка генератора требует seam ratio не выше 1.35, нормализованные normal-векторы и совпадение alpha с обратной roughness с погрешностью не более одного уровня.

Пересборка:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_lime_plaster_material.py
```

Ранее документированный архив pogranichye_lime_plaster_pbr_pack.zip также отсутствует в текущем main. Когда source artwork будет предоставлен, положить lime_plaster_source.jpg рядом со скриптом и запустить builder.

В `WorldLayoutPresentationBuilder` штукатурка применяется к стеновым плоскостям сгенерированных домов и таверны. Деревянный каркас, двери, стойки, крыши и открытый рыночный навес остаются на отдельных timber/thatch материалах. UV tiling — 1.5×1.5. До появления полного authored pack сначала используется Grok plaster_wall (если импортирован), затем procedural fallback; authored lime plaster применяется только при availability: available и наличии 1024px albedo.


# Авторская текстура соломенной кровли

Целевой вид — уложенные рядами пучки ржаной соломы и камыша, отдельные волокна, тёмные промежутки между рядами и редкие верёвочные перевязки. Это не повторяющийся синусоидальный узор и не одна цветная картинка вместо PBR.

Скрипт build_thatch_roof_material.py создаёт в Unity Resources:

- thatch_roof_albedo.png — 1024×1024 RGBA; RGB — цвет соломы, A — линейная smoothness (1 - roughness);
- thatch_roof_normal.png — tangent-space normal, positive Y;
- thatch_roof_roughness.png — отдельная линейная roughness;
- thatch_roof_ao.png — отдельная линейная AO;
- thatch_roof_material.json — параметры PBR, источник и метрики швов.

Все карты получаются из одного исправленного по краям художественного исходника и общей карты рельефа. Генератор проверяет четыре карты отдельно: seam ratio не выше 1.35, длину декодированных нормалей и соответствие alpha обратной roughness с погрешностью не более одного уровня.

Пересборка:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_thatch_roof_material.py
```

При наличии полного набора авторская солома применяется к _roof (кровли зданий и навесы), _straw (соломенные props) и server-authored поверхностям kind roof. Для кровель сохраняется tiling 2×3, для соломенных props — 2×2. При pending/отсутствующих картах остаётся Grok/procedural fallback.


# Авторская текстура лесной подстилки

Целевой исходник — плотный естественный слой лесной подстилки: влажная тёмная земля, перекрывающиеся листья дуба и берёзы, хвоя, тонкие веточки, корни, кусочки коры, мелкие камни, мох и лишайник. Источник не является процедурным шумом: это отдельная авторская hand-painted / realistic-stylized композиция.

Скрипт `build_forest_floor_material.py` создаёт в Unity Resources:

- `forest_floor_albedo.png` — 1024×1024 RGBA; RGB — цвет подстилки, A — линейная smoothness (`1 - roughness`);
- `forest_floor_normal.png` — tangent-space normal, positive Y;
- `forest_floor_roughness.png` — отдельная линейная roughness;
- `forest_floor_ao.png` — отдельная линейная AO;
- `forest_floor_material.json` — диапазоны карт, SHA-256 исходника и метрики бесшовности.

Normal, roughness и AO выведены из общей карты высот, полученной из границ листьев, веток и перекрывающегося растительного слоя. Все карты проходят отдельную проверку seam ratio (не выше 1.35), длины декодированных normal-векторов и совпадения smoothness alpha с обратной roughness (погрешность не более одного уровня).

Пересборка:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_forest_floor_material.py
```

Исходник `forest_floor_source.jpg` должен лежать рядом со скриптом. Он и четыре карты входят в архив `pogranichye_forest_floor_pbr_pack.zip`; распаковывать архив нужно в корень репозитория. В чистом checkout манифест остаётся `availability: pending`, пока исходник и карты не установлены.

## Подключение в мире

`WorldLayoutPresentationBuilder` создаёт отдельный общий `_forestFloor` материал только для серверных зон `forest`. Поверхность — один quad на лесную зону с UV, выровненными по мировым координатам: один повтор на 4 метра. Это сохраняет одинаковый размер листьев на маленьких и больших лесных участках. Материал `_forest` остаётся отдельным и продолжает обслуживать кроны деревьев; зоны `copse` сохраняют более светлый травяной пол. Количество объектов не растёт на каждое дерево — создаётся один mesh на лесную зону.

`WorldMaterialTextureImporter` автоматически применяет Repeat, mipmaps, trilinear, anisotropic 8, Normal Map и linear data import для roughness/AO. Если авторский комплект отсутствует, остаётся текущий procedural fallback.


# Авторская текстура коры старого дуба

Целевой исходник `oak_bark_source.jpg` — выветренная кора дуба с вертикальными пластинами, глубокими трещинами, волокнистой поверхностью и сдержанными участками лишайника. Генератор `build_oak_bark_material.py` создаёт в Unity Resources:

- `oak_bark_albedo.png` — 1024×1024 RGBA; RGB — цвет коры, A — линейная smoothness (`1 - roughness`);
- `oak_bark_normal.png` — tangent-space normal, positive Y;
- `oak_bark_roughness.png` — отдельная линейная roughness;
- `oak_bark_ao.png` — отдельная линейная AO;
- `oak_bark_material.json` — диапазоны карт, SHA-256 исходника и метрики бесшовности.

Генератор смещает исходник на половину тайла и ремонтирует центральный крест детерминированным Poisson patch из другого участка того же изображения. Все четыре карты проверяются отдельно; порог seam ratio — 1.35. Normal, roughness и AO выводятся из общей карты рельефа. Smoothness хранится в linear alpha albedo для URP Lit. Для пересборки нужны Python 3.10+, Pillow, NumPy и OpenCV:

```bash
python -m pip install pillow numpy opencv-python
python 2-client/art-source/textures/build_oak_bark_material.py
```

В runtime авторская кора применяется к общему `_trunk` материалу деревьев с сохранением UV tiling 1×3. Приоритет: полный authored pack, затем Grok `bark_tree` albedo/normal, затем procedural fallback. Исходный JPG и четыре 1024px PNG распространяются в отдельном PBR-архиве; манифест чистого checkout остаётся pending, пока бинарные файлы не установлены.
