!В работе
# 013 — Claude: targetFor tavernkeeper → TavernCounter

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
