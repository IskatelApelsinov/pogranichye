!В работе
# 009 — Claude: OpenFrontier на пешем StepLands (P0 пустой горизонт)

**Исполнитель:** Claude  
**Подсистема:** `1-server/sim/worldpos.go`, `frontier.go`, тесты  
**Приоритет:** P0.7 — блокер полевого теста «пустота до горизонта».  
**Спека:** `3-dev/comms/0234-grok-to-claude-pr-spec-step-lands-frontier.md`

## Цель

После успешного пешего handover в `StepLands` вызывать `LearnVisited` + `OpenFrontier` (как в `journey.arrive`), без телепорта к колодцу и без смены wire.

## Шаги

1. Прочитать 0234 и текущий `StepLands` / `OpenFrontier` / `arrive`.
2. Минимальный патч + тесты (pedestrian opens outer; inner no-op; WorldPos continuous; no double-open).
3. `go test ./sim ./server -count=1`.
4. Ответ в `3-dev/comms/` (ветка, результат тестов, count settlements до/после).
5. PR (не прямой push критичного кода в main без ревью владельца, если так принято).

## Не делать

- TerrainChunk content descriptors (отдельная задача).
- Unity / контракт кадра.
- GoTo(well) на handover.

## Критерий готовности

Спека 0234 § «Критерии приёмки» закрыта; владелец/ChatGPT могут повторить пеший выход к outer и видеть рост frontier в manifest.
