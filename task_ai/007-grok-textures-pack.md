# 007 — Grok: стартовый пакет procedural-текстур + интеграция

**Исполнитель:** Grok (генерация) → ChatGPT (Unity integration) → владелец (визуальная приёмка)
**Подсистема:** `Assets/Resources/WorldMaterials/Textures/Grok/`
**Приоритет:** P1.4 (визуальное качество), не блокирует симуляцию.

## Цель

Подключить стартовый набор бесшовных albedo-текстур к существующим presentation materials поселения, сохранив fallback и авторские PBR-карты проекта.

## Сделано

- В канонической папке `grok/textures/grok/` найдено 58 albedo + 58 tangent-space normal maps (OpenGL Y+), всего 116 PNG.
- Документы: `textures/grok/README.md`, `3-dev/docs/NORMAL-MAPS-GENERATION.md`.
- Канонический источник: `grok/textures/grok/`; Unity Resources — синхронизируемая копия.
- Runtime builder получил необязательную загрузку Grok maps; при отсутствии PNG остаются прежние procedural материалы.
- Unity importer распознаёт суффикс `_n` как Normal Map, sRGB Off.
- Авторские карты (например packed-road PBR) имеют приоритет над Grok fallback.

## Бинарные файлы — найдены, импорт в Unity ожидает запуска

Бинарники уже присутствуют в `grok/textures/grok/`: 116 PNG, 58 albedo + 58 normal, около 22.2 MB. Это канонический источник. Unity Resources — генерируемая копия; синхронизация выполняется Editor-командой `Tools → Pogranichye → Art → Import Grok Texture Pack` из `GrokTexturePackSync.cs`. Команду ещё должен запустить владелец в Unity. Не отмечать карты импортированными или визуально проверенными до этого шага и Game View-приёмки.

## Соответствия

- grass_meadow → meadow ground
- dirt_soil → field ground
- cobble_path → road fallback only
- wood_planks → timber
- thatch_roof → roof/straw
- bark_tree → trunk
- stone_wall → stone
- cloth_linen → cloth props
- water_shallow → water albedo only
- plaster_wall → оставить до отдельного art pass стен

## Критерии приёмки

- Все 58 albedo + 58 normal PNG синхронизированы в Unity Resources командой Editor importer.
- Import settings проверены в Unity Inspector.
- Эталонная улица сравнивается в дневном и вечернем освещении.
- Normal Scale не создаёт рябь/пластиковый вид; бесшовность проверена на 2×2 tiling.
- Проверены Generated Layout и tutorial layout, чтобы не сломать streaming.
- Нет изменений сервера, контрактов, POI, footprint или коллизий.
- Владелец подтверждает визуальный результат.
