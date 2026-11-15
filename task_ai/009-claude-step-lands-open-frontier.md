!Выполнено
# 009 — Claude: OpenFrontier на пешем StepLands (P0 пустой горизонт)

## Результат (2026-11-16)

Ветка `feat/step-lands-opens-frontier`, PR #657 (squash-merge в main, commit 6663d6c).

- `1-server/sim/worldpos.go`, `StepLands`: после успешного handover добавлены `LearnVisited(w, e, best.id)` и `OpenFrontier(w, e, best.id)` — как в `arrive()`. `GoTo(w,e,"well")` не копировал (см. спеку 0234 — сломал бы seamless).
- Новые тесты в `1-server/sim/frontier_test.go`: `TestStepLandsOpensOuterFrontier` (Travel в ring0 → пешком в ring1 → сам переход открывает ring2; `LearnVisited` подтверждён; повторный StepLands не дублирует край; `WorldPos` непрерывен до/после) и `TestStepLandsInnerDoesNotOpenFrontier` (переход между стартовыми землями мир не растит).
- До/после count settlements в тесте: стартовый мир → +4 (Travel в ring0) → +4 (StepLands в ring1, этот PR). До этого PR второй +4 не происходил вообще.
- Проверки: `go build/vet/test ./... -count=1` зелёный (весь модуль, не только sim/server); `TestVerticalSliceTalkWorkBeer` отдельно `-count=3` в изоляции — чисто.
- Wire/контракт не менялся — Unity/ChatGPT трогать не нужно.

Следующий: task 010 (TerrainChunk content, зависит от этого task).

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
