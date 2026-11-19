# 0311 — Grok: daily status 2026-11-19 (05:21 MSK)

От: Grok (консультант)  
Кому: владелец, Claude, ChatGPT

## P0 статус

**Сдвинулось с 18.11:**
- Серверные P0.7 (010 TerrainChunk content, 009 OpenFrontier, trails) на main.
- v1.31/v1.33: ремёсла/кров, разбойники, города, державы/обозы видны.
- Открытые клиентские PR: #773 (таверна E-interact), #774 (draft, horse pose), #762 (protocol diagnostics).

**Блокирует базовую играбельность:**
- **011** (tavern beer / co-drink e2e): `!Блокировано` — сервер green, ждёт Unity/thin field test + task_human/008 (`!В работе`).
- Runtime acceptance пешего seamless + таверна solo/co-drink — на владельце / ChatGPT.
- Клиентские interactions/камера/спринт ещё не приняты в PlayMode.

## Смоук сервера (sandbox, Go 1.23.4)

- `go test ./core ./sim ./mind ./memory ./dice ./econ ./server -count=1` — **PASS*** (все ok; в полном сюите один раз flaky FAIL на `TestVerticalSliceTalkWorkBeer` "корчмаря нет за стойкой" из-за часов/расписания; отдельно — PASS). Мир живой.
- `go run ./cmd/pogserver -demo -days 30` — **PASS**. Старт 84 души → 104, поселения живые, еда/зерно на месте, снимок ~210 КБ.

## Открытые задачи / риски

- 011 blocked on field (task_human/008).
- Клиент: 014/015 (context + tavern UI), 019 (activities/horse) — в работе.
- PR #773 (tavern E) — релевантен P0.6, следить за мержем.
- PR #774 draft — лошадь (P1), не блокирует путь+пиво.
- Флаки тест на расписание корчмаря — не регрессия контракта, но зафиксировать.
- Концепция/ADR не менялись. Лошадь не P0.

## Следующие шаги

1. **Владелец:** runtime — пеший выход (F3 на границе), solo beer + co-drink (task_human/008), общий demo (task_human/003).
2. **ChatGPT:** мерж #773 после ревью; полевой тест 011; учесть grid/storeys из 0293.
3. **Claude:** trails по уклону + броды; разбор flaky теста расписания корчмаря при необходимости.

Маленький шаг Grok: статус + смоук. Текстуры (007/008) ждут Unity import.
