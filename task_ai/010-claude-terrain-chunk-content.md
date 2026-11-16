!Выполнено
# 010 — Claude: содержимое TerrainChunk (biome/cover/roads)

## Результат (2026-11-16)

Аддитивные поля `/api/terrain` (не ломают v1): `seed`, `gen`, `biome`, `cover`
(`forest/grass/rock/wet`, 0..1, биомный профиль + мягкий шум), `resources` (декоративные
точки-подсказки, НЕ Entity), `wild` (witness существующих `WildSiteC`, попавших в чанк — не
изобретаем новые), `roads` (отрезки `RoadGraph`, подрезанные границей чанка, алгоритм
Лианга-Барски). Новый файл `1-server/sim/terrain_content.go`, + `Terrain.Biome()` в
`sim/worldpos.go` (биом ближайшего села — то же правило, что уже решает высоту).

**Сознательно не в этом PR** (см. обновлённый `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md`): water
(ширина/уровень/течение — у Terrain нет такой модели вовсе, не выдумывал) и
road|trail|bridge|pass-различение (сейчас все "road"). Оба — отдельные маленькие PR при
необходимости.

Тесты: `TestTerrainContentDeterministicAndOrderIndependent` (A→B→A не меняет content),
`TestTerrainContentAdditiveFieldsShapeAndHeightsPreserved` (heights не регрессирует, новые поля —
корректная форма, не null), `TestTerrainChunkRoadCorridorBetweenSettlements` (чанк на середине
реального тракта содержит непустой roads; далёкий чанк — пустой массив, не null).
`go build/vet/test ./... -count=1` зелёный.

**Исполнитель:** Claude  
**Приоритет:** P0.7 сразу **после** task 009 / PR-spec 0234  
**Спека:** `3-dev/comms/0235-grok-to-claude-pr-spec-terrain-chunk-content.md`

## Цель

Additive descriptors в `/api/terrain`, чтобы между поселениями клиент мог детерминированно показать лес/поле/дорогу, а не голую heightmap.

## Зависимость

Сначала 009 (OpenFrontier на StepLands). Этот task не заменяет открытие frontier — дополняет картинку мира.

## Критерий

Спека 0235 § приёмка; короткое comms ChatGPT о новых полях.
