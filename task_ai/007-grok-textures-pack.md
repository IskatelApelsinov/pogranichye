# 007 — Grok: стартовый пакет procedural-текстур + интеграция

**Исполнитель:** Grok (генерация) → ChatGPT (Unity integration) → владелец (визуальная приёмка)
**Подсистема:** `Assets/Resources/WorldMaterials/Textures/Grok/`
**Приоритет:** P1.4 (визуальное качество), не блокирует симуляцию.

## Цель

Подключить стартовый набор бесшовных 512×512 albedo-текстур к существующим presentation materials поселения, сохранив fallback и авторские PBR-карты проекта.

## Сделано

- Grok описал 10 albedo и 10 tangent-space normal maps (Sobel по luminance, OpenGL Y+).
- Документы: `textures/grok/README.md`, `3-dev/docs/NORMAL-MAPS-GENERATION.md`.
- Канонический Unity-путь: `2-client/game/Assets/Resources/WorldMaterials/Textures/Grok/`.
- Runtime builder получил необязательную загрузку Grok maps; при отсутствии PNG остаются прежние procedural материалы.
- Unity importer распознаёт суффикс `_n` как Normal Map, sRGB Off.
- Авторские карты (например packed-road PBR) имеют приоритет над Grok fallback.

## Бинарные файлы — блокер

На GitHub main в текущей проверке отсутствуют все PNG Grok: там только README и описание. В comms 0176/0177 указано, что бинарники находятся в artifacts владельца, но доступного файла для импорта в репозиторий сейчас нет. Не отмечать текстуры как импортированные/визуально проверенные до появления реальных PNG.

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

- Все 10 albedo + 10 normal PNG реально присутствуют в Unity Resources.
- Import settings проверены в Unity Inspector.
- Эталонная улица сравнивается в дневном и вечернем освещении.
- Normal Scale не создаёт рябь/пластиковый вид; бесшовность проверена на 2×2 tiling.
- Проверены Generated Layout и tutorial layout, чтобы не сломать streaming.
- Нет изменений сервера, контрактов, POI, footprint или коллизий.
- Владелец подтверждает визуальный результат.
