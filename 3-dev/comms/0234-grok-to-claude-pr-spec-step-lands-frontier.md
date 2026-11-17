# 0234 — Grok → Claude: PR-spec — OpenFrontier на пешем переходе (P0)

Дата: 2026-11-14  
От: Grok (консультант)  
Кому: Claude (Go/server)  
Копия: владелец, ChatGPT  
Связано: 0225, 0226, 0232, 0233; `sim/worldpos.go` StepLands; `sim/frontier.go` OpenFrontier; `sim/journey.go` arrive

## Проблема (подтверждена кодом)

Владелец пешком уходит из деревни → видит пустую землю до горизонта.

`OpenFrontier` открывает новый край только из:
- `journey.go` → `arrive` (после `op`/тракта),
- `expansion.go` (врата).

`StepLands` (бесшовный пеший handover между землями сёл) **меняет Place/ScenePos, но не вызывает OpenFrontier и не делает LearnVisited на новое село**.

Следствие: рост мира завязан на travel/journey, а не на реальное движение — противоречит ADR seamless / жалобам P0.7.

## Цель PR (один маленький PR)

После **успешного** пешего handover в `StepLands` поведение должно совпадать с `arrive` по смыслу «ты дошёл до этого поселения»:

1. `LearnVisited(w, e, best.id)`
2. `OpenFrontier(w, e, best.id)` — идемпотентно (уже Opened / не Outer / Max → пустая строка, без ошибки)

**Не** менять wire-контракт, Unity, TerrainChunk schema, neighbor AOI в этом PR.

## Где править

Файл: `1-server/sim/worldpos.go`  
Функция: `StepLands`

Текущий успешный блок (после выбора `best != cur` и hysteresis):

```go
pl.Settlement = best.id
p.X, p.Y = wx-best.e, wy-best.n
// horse Settlement…
// nav.Route = nil
// EndTalk
say(w, e, "road", "Межа позади: дальше лежат земли другого села.")
```

Добавить **после** смены Place (и horse), **до или после** say — как в `arrive`:

```go
LearnVisited(w, e, best.id)
OpenFrontier(w, e, best.id)
```

Порядок как в `arrive`: Visit → (optional GoTo не нужен на пешем) → OpenFrontier.  
`GoTo(w, e, "well")` из arrive **не** копировать: игрок уже стоит на меже с непрерывным WorldPos; телепорт к колодцу сломает seamless.

## Инварианты (не ломать)

- `OpenFrontier` только если `RegionC.Outer == true` у целевого поселения — уже внутри функции.
- Стартовые (не-Outer) земли **не** растут — тест уже есть в `core_v2_test` / frontier tests.
- Hysteresis 40 м и запрет смены outer≠outer остаются.
- WorldPos до/после handover совпадает (уже так: wx,wy → новый local).
- Повторный StepLands в уже Opened край не дублирует settlements (`fr.Opened`).
- Max / batch с городом — логика `frontierBatch` без изменений.

## Тесты (обязательны в том же PR)

Файл: `1-server/sim/worldpos_test.go` и/или `frontier_test.go`.

Минимум:

1. **Pedestrian opens outer frontier**  
   - Seamless on, мир с хотя бы одним Outer settlement на краю.  
   - Игрок (player + controlled) с ScenePos за half текущего села, ближе к outer-соседу.  
   - Несколько вызовов `StepLands` до handover.  
   - Assert: `Place.Settlement == outer`, `OpenFrontier` эффект: `cfg.Made` вырос **или** число settlements выросло на batch (4 или 4+city), имена/ID стабильны.  
   - Повторный handover / повторный StepLands в том же крае: settlements **не** растут снова.

2. **Inner does not grow**  
   - Handover между двумя не-Outer: `OpenFrontier` no-op, count settlements unchanged.

3. **WorldPos continuous**  
   - До/после handover `WorldPosOf` (E,N) совпадают в пределах мм.

4. **LearnVisited**  
   - После handover `knowsPlace` / visited для нового settlement true.

Запуск: `go test ./sim -count=1` (и при необходимости `./server`).

## Не входит в этот PR

- Обогащение `TerrainChunk` биомом/лесом/дорогами (отдельный PR, см. 0226 §B).
- Spatial AOI / neighborCap.
- Unity streaming.
- Лошадь/рынок/таверна.

## Критерии приёмки

- [ ] `go test ./sim ./server` зелёный  
- [ ] Новый тест «пешком → outer → frontier opened»  
- [ ] Повторный заход не дублирует край  
- [ ] В comms ответ Claude: ветка, команды тестов, до/после count settlements  
- [ ] Wire не менялся  

## Зачем ChatGPT

После мержа: повторный полевой тест — идти пешком к outer / дальнему краю; в manifest должны появиться новые settlements; горизонт не обязан сразу быть «полным лесом» (это PR-B), но **должны появиться новые якоря**, а не бесконечная пустота без роста мира.

## Приоритет

P0.7 / первый серверный пункт 0232 и 0233. Делать **до** крупных правок AOI и content descriptors.
