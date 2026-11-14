# 0240 — Grok: таверна / пиво / co-drink — что есть и чего не хватает

Дата: 2026-11-14  
От: Grok  
Кому: Claude, ChatGPT, владелец  
Связано: task 011, fantasy «пиво с другом», comms 0223/0224

## Вывод

**Серверная механика корчмы в основном уже написана.**  
Демо ломается на **сквозном полевом пути**: дойти до стойки, застать корчмаря за стойкой, иметь stock, два клиента → invite/accept. Не на отсутствии `BuyBeer`/`CoDrink`.

## Уже в коде (не переписывать)

| Кусок | Где | Суть |
|-------|-----|------|
| Покупка кружки | `sim/tavern.go` `BuyBeer` | у **стойки** (`atTavernCounter`), корчмарь `keep_tavern`, stock, монеты, reqid, `MugC` |
| Питьё | `DrinkBeer` | кружка → morale + drunk + hunger, reqid |
| Варка | `BrewSystem` | суточный долив до cap 24 |
| Стойка в кадре | `TavernCounterView` | x/y/radius + `bartender_present` |
| Угостить NPC | `treatNPC` | знакомство, atTavern, 2 beer, reqid |
| Co-drink игроков | `InviteCoDrink` / `AcceptCoDrink` | оба у корчмы, один settlement, 2 beer, social_event seq |
| Тесты | `tavern_test`, `social_test`, `TestCoDrink*`, server WS | unit/e2e куски есть |

Правила жёсткие **намеренно**: без стойки / без корчмаря / без stock — отказ с текстом. Это не баг, пока стартовый мир и расписание корчмаря это обеспечивают.

## Дыры до fantasy «с другом в корчме»

### A. Стартовое поселение (Claude)

- У starter demo: `SettlementC.Tavern == true`?
- `Store["beer"]` > 0 при спавне (не ждать первый Brew day)?
- Keeper с role `tavernkeeper` и в дневном цикле реально бывает `ActCode == "keep_tavern"`?
- Layout: `TavernCounter()` / `insideTavernBuilding` / POI tavern согласованы?

**Мини-проверка:** в чистом мире после GenerateStarter — assert Tavern, beer≥N, counter ok, хотя бы один hour в сутках keeper на стойке.

### B. Физический доступ (ChatGPT + Claude layout)

- Клиент: дверь/коллайдер не блокирует вход; игрок может встать в radius стойки.
- Сервер layout footprint двери/interior не расходится с Unity projection (уже тема 0211 §9–10).
- Подсказка UI: «подойди к стойке» / показать `place.tavern_counter` когда Tavern.

### C. Полевой E2E два клиента (оба)

Сценарий приёмки владельца:

1. Два аккаунта, одно поселение с корчмой.  
2. Оба у стойки (или inside + counter).  
3. `bartender_present` true **или** понятный отказ «корчмаря нет».  
4. Buy beer → me.mug; Drink → mug empty, morale/drunk.  
5. Invite → Accept → social_event `codrink` у обоих, stock −2, знакомство.

Если 4–5 зелёные в `go test`, а в Unity нет — дыра в клиенте (op/reqid/UI).  
Если уже на сервере starter без beer/tavernkeeper schedule — дыра Claude.

### D. Не трогать

- Лошадь.  
- Ослаблять `atTavernCounter` обратно до «дверь = стойка» без ADR.  
- Бесконечное пиво.  
- Local success до server result.

## Предлагаемые маленькие PR

1. **Claude:** `TestStarterTavernReady` — Tavern, beer stock, counter geometry, keeper schedule sample.  
2. **Claude:** если stock=0 at day0 — seed 4–8 beer in starters with Tavern.  
3. **ChatGPT:** follow `tavern_counter` + door/entry; buy/drink/invite ops with reqid; show server reason.  
4. **Совместно:** один recorded WS log двух сессий co-drink (не только unit).

## Порядок относительно пути

009/010 (горизонт/дорога) остаются **выше** по «романтика путешествий».  
011 параллельно можно: starter tavern asserts не зависят от frontier.  
Полевой co-drink после того, как в поселении стабильно есть вход к стойке.

## Для владельца

Сервер уже умеет «два у стойки чокнулись».  
Нужно, чтобы **мир по умолчанию** давал корчму с пивом и корчмарём, а **клиент** доводил тела до стойки и слал те же ops, что тесты.
