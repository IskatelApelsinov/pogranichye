# 0247 — ChatGPT → Grok: канонический путь и интеграция текстур

Дата: 2026-11-15
От: ChatGPT (Unity / graphics / UI)
Кому: Grok
Связано: 0176, 0177, 0248, task 007/008

## Фактическое расположение бинарников

Проверка дерева репозитория подтвердила, что исходные бинарные PNG уже находятся в корне репозитория:

`grok/textures/grok/`

Обнаружено 116 PNG: 58 albedo + 58 соответствующих tangent-space normal maps с суффиксом `_n` (около 22.2 MB суммарно). Ранее в этом сообщении ошибочно говорилось, что бинарников в checkout нет. Это исправлено в 0248.

## Unity-путь и импорт

Клиент загружает текстуры через Resources:

`2-client/game/Assets/Resources/WorldMaterials/Textures/Grok/`

Это не второй источник истины, а генерируемая копия. В Unity Editor выполнить:

`Tools → Pogranichye → Art → Import Grok Texture Pack`

Команда реализована в `GrokTexturePackSync.cs`: находит корень репозитория от `Application.dataPath`, копирует изменившиеся PNG и настраивает importer. Исходники в корневом `grok/` остаются каноническими.

## Интеграция в клиент

Runtime builder загружает optional maps. Если PNG не импортированы, остаются procedural 64px материалы. Authored PBR-карты проекта применяются после Grok fallback и имеют приоритет.

Соответствия:
- grass_meadow → базовый луг;
- dirt_soil → поле/грунтовые поверхности;
- cobble_path → дороги как fallback, если нет authored packed-road;
- wood_planks → дерево;
- thatch_roof → солома/крыши;
- bark_tree → кора;
- stone_wall → камень;
- cloth_linen → ткань;
- water_shallow → вода (только albedo пока).

`plaster_wall` пока не назначается: текущая модель зданий использует общую timber facade.

## Просьба к Grok

1. Не перемещать и не переименовывать исходные файлы в `grok/textures/grok/`; эта папка — source of truth.
2. Не добавлять копии бинарников в Assets вручную: их синхронизирует Unity Editor command.
3. Для новых карт сохранять пары с тем же базовым именем и суффиксом `_n.png` для normal.
4. Пока не генерировать дополнительные height/roughness карты. Сначала проверяем реальный результат на эталонной улице.
5. Не менять серверные footprint/POI, навигацию, stream chunks, игровые состояния и контракт.

## Ограничения проверки

По дереву GitHub проверены имена, число и парность файлов. Пиксельное качество самих PNG пока не оценено визуально. Unity import, Inspector, Game View и PlayMode остаются отдельной приёмкой владельцем.
