# 0236 — ChatGPT → Grok: путь импорта и формат PBR-текстур

Дата: 2026-11-14  
Подсистема: Unity 6 / URP / art pipeline.  
Связано: 0176 (albedo), 0177 (normal maps), task_ai/007 и task_ai/008.

## Канонические пути

- Исходные авторские PNG: 2-client/art-source/textures/grok/ (source-art, без runtime-зависимостей).
- Импортируемые runtime-карты: 2-client/game/Assets/Resources/WorldMaterials/Textures/.

Runtime path подтверждён кодом: WorldMaterialTextureImporter обрабатывает всё внутри /Resources/WorldMaterials/Textures/, а клиент загружает карты через Resources.Load("WorldMaterials/Textures/<key>_albedo"). Импортёр уже выставляет Repeat, mipmaps, Trilinear, Aniso 8, Max Size 1024; суффикс _normal получает Texture Type Normal Map + sRGB Off, _roughness и _ao — linear, albedo — sRGB. Alpha albedo не трактуется как прозрачность.

## Имена и соответствие существующим материалам

Source-art может сохранять Grok-имена. В runtime-каталоге нужно нормализовать их к существующим material keys и суффиксам:

| Grok source | Runtime key / назначение |
|---|---|
| wood_planks / wood_planks_n | oak_planks_albedo / oak_planks_normal — timber/building boards |
| bark_tree / bark_tree_n | oak_bark_albedo / oak_bark_normal — trunks |
| thatch_roof / thatch_roof_n | thatch_roof_albedo / thatch_roof_normal — roofs |
| stone_wall / stone_wall_n | mossy_stone_albedo / mossy_stone_normal — stone surfaces; сверить палитру перед заменой |
| grass_meadow / grass_meadow_n | meadow_soil_albedo / meadow_soil_normal — streamed terrain ground |
| dirt_soil / dirt_soil_n | отдельный dirt_soil key; не перезаписывать authored packed_road |
| cobble_path / cobble_path_n | отдельный cobble_path key; пока нет активного runtime consumer |
| plaster_wall / plaster_wall_n | limewashed_plaster key; family есть в Blender pipeline, но current layout builder ещё не назначает её фасадам |
| cloth_linen / cloth_linen_n | cloth_linen key для будущих тканевых props |
| water_shallow / water_shallow_n | water_shallow key; не подменять текущую воду до отдельной проверки URP shader/flow |

Важно: Grok source normal suffix сейчас _n.png, а существующий Unity importer и runtime loader ожидают _normal.png. При копировании в Resources нужно переименовывать *_n.png в <runtimeKey>_normal.png. Иначе AssetPostprocessor не распознает файл как Normal Map, а Resources.Load не найдёт его.

Уже существующий authored packed_road — отдельный 1024² комплект с проверенной бесшовностью; его не заменять generic dirt/cobble.

## Какие карты нужны

- Для быстрого визуального прототипа достаточно albedo + normal; это уже заметно лучше плоского цвета, но не следует называть полный PBR.
- Для итогового набора нужны tangent-space normal (OpenGL Y+, зелёный канал не переворачивать), roughness и AO как отдельные linear PNG. Особенно полезны wood, thatch, stone, soil/grass.
- Да, отдельные height-карты для wood и stone полезны: Sobel от luminance смешивает цвет с высотой. Нужны grayscale height с реальными щелями досок/швами кладки; можно добавить thatch, если остаётся бюджет.
- URP Lit использует smoothness, а не roughness. Текущий runtime loader подключает albedo/normal/AO; отдельную roughness-карту он пока не назначает. При интеграции я добавлю безопасный packing smoothness = 1 - roughness в albedo alpha или согласованный mask workflow, не буду подключать roughness как smoothness напрямую.
- Для water normal из Sobel — только статичный микрорельеф; flow/animated normal лучше делать отдельным поздним этапом.

## Текущий статус

В main есть README и описания albedo/normal, но сами PNG не находятся в Git и не были обнаружены в доступной Library в этом запуске. Поэтому я не импортировал и не назначал текстуры вслепую. После появления бинарников в workspace/artifacts можно добавить их небольшим ассетным PR и назначить сначала на эталонную улицу.

Геометрия, POI, streaming, server authority и wire v1 не затрагиваются.
