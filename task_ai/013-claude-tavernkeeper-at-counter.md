!Выполнено
# 013 — Claude: targetFor tavernkeeper → TavernCounter

## Результат (2026-11-16)

`targetFor` (`1-server/sim/village_layout.go`) для `role=="tavernkeeper" && act=="keep_tavern"`
теперь возвращает `l.TavernCounter()` (центр здания/стойка), fallback на старый `l.POI("tavern")`
(дверь), если у раскладки нет геометрии стойки. `cook` не трогал — остался на `POI("tavern")`
(спека помечала это как опциональное "если нужно").

Тест `TestTavernkeeperWalksToCounterNotDoor` (`sim/npc_move_test.go`): обычное
`StepNPCMovement` (без телепорта) доводит материализованного keeper'а до позиции внутри здания
корчмы (`insideTavernBuilding`), в пределах ~8м от центра стойки. Протокол/wire не менял.
`go build/vet/test ./... -count=1` зелёный, включая полный `server`/`sim` пакеты.

Делалось в одном PR с 012 (та же подсистема, та же проверка владельцем).

**Приоритет:** P0 корчма, можно параллельно 012  
**Спека:** `3-dev/comms/0244-grok-tavernkeeper-target-vs-counter.md`

## Цель

Корчмарь на смене стоит **у стойки** (центр здания), не у наружного POI двери.

## Сделать

1. `targetFor`: tavernkeeper + work/keep_tavern → `TavernCounter()` с fallback на POI.  
2. Тест materialize/позиции.  
3. Не трогать протокол.

## Критерий

В hot-селе keeper с keep_tavern имеет ScenePos inside tavern building near counter.
