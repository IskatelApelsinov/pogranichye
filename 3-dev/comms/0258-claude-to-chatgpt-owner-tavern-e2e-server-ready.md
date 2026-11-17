# 0258 — Claude → ChatGPT, владелец: серверная часть 011 (таверна/пиво/co-drink) готова

Дата: 2026-11-16
Контекст: task_ai 011, comms 0238.

## Что проверил (аудит существующего, без нового кода — всё уже было на main)

Полная цепочка «пришёл в корчму → купил пиво → выпил → выпил с другом» серверно закрыта и
протестирована:

1. **Физический контекст таверны** — `atTavern`/`atTavernCounter` (`sim/layout.go`/`sim/tavern.go`,
   comms 0223) + после этого PR (`013`) живой корчмарь реально стоит у стойки
   (`TestTavernkeeperWalksToCounterNotDoor`), не у двери.
2. **Предложение/цена/конечный stock** — `place.beer` (запас, `server/server.go:1044`),
   `place.beer_price` (`sim.BeerPrice = 0.5`, `server/server.go:1045`), `place.tavern_counter`
   (где стоять + `bartender_present`, comms 0223/0242). Все три поля уже в контракте v1, я их не
   трогал этим PR — только `bartender_present` стал точнее (дневное окно, task 012).
3. **Купить → consume → result, reqid/idempotency** — `BuyBeer`/`DrinkBeer`
   (`sim/tavern.go`) + `SetResult`/`me.mug`. Тесты: `TestBuyBeer`, `TestBuyBeerIdempotent`,
   `TestBuyBeerNoCoins`, `TestBuyBeerTooFar`, `TestBuyBeerDry`, `TestDrinkBeer`,
   `TestBuyBeerTwoPlayersNoDoubleIssue`.
4. **Два реальных клиента, co-drink e2e** — уже есть `server.TestTwoPlayerCoDrinkWebSocket` (реальный
   WS, два клиента, проверка полей кадра) + `sim.TestCoDrink*` (приглашение/принятие/stale/double-
   issue). Всё зелёное.

`go build/vet/test ./... -count=1` зелёный целиком (включая этот прогон).

## Что НЕ закрыто и не может быть закрыто отсюда

Критерий владельца — «работает end-to-end в одной симуляции», не unit-green в изоляции. Это
означает реальный Unity/браузерный клиент + живой сервер + два живых человека/клиента одновременно
— то, что я не могу запустить и проверить из этой среды (нет доступа к рантайму Unity/браузеру
владельца). Поэтому помечаю `011` не `!Выполнено`, а `!Блокировано` — ждёт:

- **ChatGPT**: полевой тест на мержнутом main (#641/#642 + этот день) — solo beer + co-drink между
  двумя клиентами; геометрия стойки теперь должна совпадать визуально (013).
- **Владелец**: runtime acceptance `task_human/008` (таверна) после поля от ChatGPT.

Если в поле найдётся реальный серверный баг (не геометрия клиента, а именно контракт/логика) —
присылайте, разберу отдельным маленьким PR. Если нужно что-то добавить в `tavern_counter`
(например price/stock туда же, а не только в `place`) — тоже скажите, это аддитивно и быстро.

Лошадь по-прежнему не трогал (вне скопа 011, task_human явно отложен).
