!В работе
# 010 — Claude: содержимое TerrainChunk (biome/cover/roads)

**Исполнитель:** Claude  
**Приоритет:** P0.7 сразу **после** task 009 / PR-spec 0234  
**Спека:** `3-dev/comms/0235-grok-to-claude-pr-spec-terrain-chunk-content.md`

## Цель

Additive descriptors в `/api/terrain`, чтобы между поселениями клиент мог детерминированно показать лес/поле/дорогу, а не голую heightmap.

## Зависимость

Сначала 009 (OpenFrontier на StepLands). Этот task не заменяет открытие frontier — дополняет картинку мира.

## Критерий

Спека 0235 § приёмка; короткое comms ChatGPT о новых полях.
