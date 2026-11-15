!Выполнено
# 012 — Claude: окно обслуживания корчмы (BuyBeer)

## Результат (2026-11-16)

Вариант A реализован: `bartenderServing(w, se, hour)` теперь true, если живой keeper-tavernkeeper
И (`ActCode=="keep_tavern"` ИЛИ час в `[10,22)`, см. `bartenderOpenHour/bartenderCloseHour` в
`1-server/sim/tavern.go`). Ночью (вне окна) отказ остаётся честным независимо от ActCode.
`BuyBeer` и `TavernCounterView` получили явный параметр `hour int`, прокинутый из
`server.runner.Clock.Hour()` (без скрытого глобального состояния, по образцу `QuestsView`/`day`).
GenerateStarter/seed не трогал.

Тесты: `TestBuyBeerDayVsNight` (день при ActCode=eat — успех; ночь при ActCode=keep_tavern —
всё равно отказ), `TestBuyBeerKeeperBusy` (переориентирован на ночной случай), 
`TestCoDrinkDoesNotNeedKeepTavern` (зафиксировал, что co-drink сознательно НЕ трогал — asimметрия
из 0242 остаётся как есть, не задача этого PR). `go build/vet/test ./... -count=1` зелёный.

Контракт-заметка про смысл `bartender_present` ("на смене", не "физически у стойки") добавлена в
`3-dev/docs/CLIENT-TAVERN-COUNTER-DTO-COVERAGE.md`. Делалось вместе с 013 (тот же файл/подсистема).

**Приоритет:** P0 fantasy пива, параллельно 009/010  
**Спека:** `3-dev/comms/0242-grok-bartender-schedule-vs-beer.md` вариант **A**

## Цель

Днём у стойки с пивом и живым tavernkeeper покупка не должна отваливаться только потому, что в этот hour Decide выбрал rest/eat. Ночь — отказ.

## Сделать

1. Смягчить `bartenderServing` (или обёртку для BuyBeer) дневным окном.  
2. Тесты day/night.  
3. Не трогать GenerateStarter seed.  
4. Короткий comms ChatGPT: когда `bartender_present` true.

## Не делать

Лошадь; 24/7 без условий; ослабление atTavernCounter.
