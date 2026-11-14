# 0233 — ChatGPT: сверка typed DTO с реальным server wire и CoreGameplay

- Дата: 2026-11-14
- От: ChatGPT
- Кому: Kate, Claude
- Область: Unity client / protocol integration
- Основа: `task_ai/003-chatgpt-typed-gameplay-state.md`, `3-dev/comms/0230-chatgpt-client-fast-frame-boundary.md`, `3-dev/comms/0229-chatgpt-unity-client-audit-initial.md`

## Неизменная граница

Go остаётся единственным владельцем симуляции и исходов. Этот аудит касается только формы передаваемого состояния и read-only представления в Unity. Он не предлагает менять wire v1, серверную логику, семантику действий или вводить локальную истину.

## Источники, сверенные в main

- `1-server/server/server.go`: `stateJSON(cid)` и `fastJSON(cid)`.
- `2-client/game/Assets/_Project/Scripts/Networking/Protocol/WorldFrame.cs`.
- `2-client/game/Assets/_Project/Scripts/Networking/WorldFrameParser.cs`.
- `2-client/game/Assets/_Project/Scripts/Networking/FastFrameParser.cs`.
- `2-client/game/Assets/_Project/Scripts/Runtime/CoreGameplay.cs`.
- `2-client/game/Assets/_Project/Editor/ProtocolFixtureValidator.cs`.

Ниже зафиксирована именно структура, объявленная сервером в `stateJSON`/ `fastJSON`, а не предположения по UI.

## 1. Полный кадр: верхний уровень

| Wire path | Server source | DTO | CoreGameplay consumer | Семантика |
|---|---|---|---|---|
| `v`, `alive`, `day`, `year`, `season`, `hour` | `stateJSON` | `WorldFrame` | `hour`; остальное — controller/environment | Обязательная версия/lifecycle; `hour` аддитивен |
| `me` | `stateJSON` | `CharacterFrame` | основной источник `_me` | В живом полном кадре объект; поля внутри могут быть null/отсутствовать по версии/контексту |
| `place` | `stateJSON`, только если персонаж находится в поселении и не в дороге | `PlaceFrame` | основной источник `_place` | Может отсутствовать в полном кадре; отсутствие не эквивалентно пустому поселению |

## 2. `me`: server wire → CharacterFrame → CoreGameplay

В `CharacterFrame` сейчас объявлены все следующие top-level server fields, кроме явно перечисленных в колонке «Нет DTO».

| Wire path | DTO coverage | CoreGameplay reads | Примечание |
|---|---|---|---|
| `me.id/name/surname/nick/role/sex/trait/origin` | Есть | не все читаются CoreGameplay; id используется registry/controller | Строки/идентичность |
| `me.age/health/hunger/energy/morale` | Есть | `health` | Числовые поля |
| `me.skills` | Есть | нет прямого top-level read в CoreGameplay | Словарь навыков |
| `me.goal/last/home/act_code` | Есть | нет прямого read из `_me` в CoreGameplay для всех полей | `act_code` — стабильный код действия; не заменять прозой `goal/last` |
| `me.mug/mana/coins/debt/wounds/bleeding/stash` | Есть | `mug, mana, coins, stash` | Часть данных может меняться в fast кадре; раны — объект |
| `me.pos/wpos` | Есть | `pos` | `pos` — локальная сцена; `wpos` — глобальные double-координаты |
| `me.inv` | Есть (`InventoryFrame`) | `inv` | Вложенные `items/hand/worn` |
| `me.world/known_places` | Есть | `world` | Знания персонажа нельзя заменять полным developer manifest |
| `me.weave/talk/invite_from/social_event` | Есть | `weave, talk, party` (invite data также читается в party path) | Проверить вложенные схемы до типизации потребителей |
| `me.result` | Есть (`ActionResultFrame`) | `result` | Один последний ack-slot: reqid/ok/reason; быстрые последовательные запросы нельзя считать независимыми событиями |
| `me.task/journal/room` | Есть | `task` | `room` — optional/null, приватные данные сундука не переносить в публичные view |
| `me.combat` | **Нет DTO** | `combat` | Нужен отдельный класс по фактическому `CombatView` и fixtures |
| `me.feed` | **Нет DTO** | `feed` | Монотонная лента; не сводить к строке |
| `me.quests` | **Нет DTO** | `quests` | Вложенные `active/done/offers/rep` |
| `me.recipes` | **Нет DTO** | `recipes` | Вложенные рецепты/needs/can/here |
| `me.journey` | **Нет DTO** | `journey` | Может быть null вне дороги; вложенные `eta/event/follow/note/progress/scene/to_name` |
| `me.horse` | **Нет DTO** | `horse` | Может быть null; mounted/here/stamina/name/bag |
| `me.bag` | **Нет DTO** | `bag` | Load/capacity/overload |
| `me.stats` | **Нет DTO** | `stats` | agility/endurance/intellect/perception/strength/will |
| `me.drunk` | **Нет DTO** | `drunk` | Числовой уровень опьянения |
| `me.region` | **Нет DTO** | `region` | nodes/roads; не путать с полным manifest |
| `me.party` | **Нет DTO** | `party` | members/invite_from |
| `me.card_game` | **Нет DTO** | `card_game` | Личная партия; скрытая рука корчмаря сервером не раскрывается |

**Итого по `me`:** текущий `CharacterFrame` покрывает значительную часть wire, но не 11 top-level разделов, которые читает `CoreGameplay`. DTO нельзя использовать как полную замену raw document, пока эти разделы не типизированы и не подтверждены fixtures.

## 3. `place`: server wire → PlaceFrame → CoreGameplay

| Wire path | DTO coverage | CoreGameplay reads | Примечание |
|---|---|---|---|
| `place.id/name/tier/plan` | Есть | `id/name/tier` | `plan` — presentation/layout descriptor |
| `place.food/bread/bread_price/tavern/beer/beer_price` | Есть | `food/tavern/beer/beer_price` | Наличие корчмы и stock — server state |
| `place.anchor` | Есть | не читается через CoreGameplay | Глобальный anchor, double precision |
| `place.neighbors` | Есть (`NeighborFrame`) | `neighbors` | Стабильные server IDs; act_code и public gear |
| `place.life` | Есть (`SettlementLifeFrame`) | `life` | Core reads population/working/activities/stock |
| `place.rooms` | Есть | не читается CoreGameplay напрямую | Public vacancy summary; не содержит tenant chest data |
| `place.tavern_counter` | **Нет DTO** | нет прямого read в текущем списке CoreGameplay | Аддитивный server field из comms 0223; стойка/прислуживание корчмаря |
| `place.foes` | **Нет DTO** | `foes` | Полный кадр использует AOI view; отдельная форма fast foes должна быть сверена |
| `place.exits` | **Нет DTO** | `exits` | Выезды/дороги/перевал |
| `place.site` | **Нет DTO** | `site` | terrain/climate/features/state/specialty |
| `place.stable` | **Нет DTO** | `stable` | horses/price |
| `place.market` | **Нет DTO** | `market` | rows good/price/stock/sell |

**Итого по `place`:** `PlaceFrame` не покрывает 6 актуальных серверных разделов: `tavern_counter`, `foes`, `exits`, `site`, `stable`, `market`. Для gameplay потребителей как минимум 5 из них обязательны.

## 4. Fast frame — отдельный patch, не snapshot

Фактический `fastJSON` сервера сейчас отдаёт:

| Wire path | Фактическая форма | DTO/validator | Merge semantics |
|---|---|---|---|
| `v/fast/alive` | envelope | `FastFrameParser` проверяет | `alive:false` передаётся full lifecycle parser |
| `me.pos` | объект позиции | validator проверяет object | Перезаписывает только это поле |
| `me.mana/health/energy` | числа | validator проверяет number | Отсутствующие поля сохраняются; явный null, если когда-либо появится, должен иметь отдельно оговорённую семантику |
| `me.combat/wounds` | объекты | validator проверяет object | Частичное обновление конкретных разделов |
| `me.feed` | массив | validator проверяет array | Лента должна дедуплицироваться по sequence |
| `me.result` | объект или null | validator допускает object/null | Один last-result slot, не очередь ack |
| `place.neighbors` | массив id + pos | validator проверяет array и строковый id | Заменяется только при наличии в patch |
| `place.foes` | массив | validator проверяет array и строковый id | Заменяется только при наличии в patch |

Сервер не отправляет в fast кадре `me.id`, `me.inv`, `me.horse`, `me.journey`, `place.anchor`, `place.market`, `place.stable`, `place.tavern_counter` и прочие поля полного снимка.

Текущий `FastFramePatch` — типизированная оболочка envelope, но его `Me` и `Place` остаются `JObject`. `CoreGameplay.ApplyFastFrame` поверхностно накладывает присутствующие свойства на сохранённые `_me/_place`. Это правильная базовая patch-семантика, но не конечная typed model.

## 5. Текущее состояние границы и безопасная последовательность

- Full: `WorldFrameParser.Parse(json, out document)` один раз проверяет JSON, возвращает `WorldFrame` и тот же validated token tree. Controller передаёт оба значения в `CoreGameplay.ApplyFrame(frame, document)`.
- Fast: `FastFrameParser.TryParse` проверяет fast envelope и возвращает `FastFramePatch`; full parser не принимает частичный fast snapshot.
- Gameplay пока продолжает читать `JObject _me/_place`. Повторного parse строки внутри CoreGameplay уже нет, но миграция к read-only typed state ещё не завершена.
- Full snapshot заменяет сохранённое состояние; fast patch обновляет только явно присутствующие разделы. Не превращать отсутствующие поля в default values.
- На дороге полный кадр может не содержать `place`; null/отсутствие нужно отличать от пустого объекта. Для optional server sections не выдумывать default-объекты.
- `me.result` — последнее подтверждение, а не полноценный поток ack. Сохранять текущую сериализацию ожидающих запросов в `RequestGate`, пока не доказана безопасная другая схема.

### Следующий безопасный шаг

1. Создать отдельные typed nested DTO только для подтверждённых server sections из таблиц выше: начать с тех, что читаются CoreGameplay (`combat/feed/quests/recipes/journey/horse/bag/stats/region/party/card_game` и `foes/exits/site/market/stable`).
2. Для каждого раздела взять точный Go view builder и server tests/fixtures; проверить отсутствует/null/объект/пустой массив, числовые типы и ID.
3. Сделать immutable/read-only gameplay view builder из full DTO + fast patch. Fast merge должен менять только переданные разделы и не стирать full-only state.
4. Перевести одну ограниченную группу читателей (рекомендуемый первый срез — read-only HUD health/mana/stats либо journal/feed; выбрать по минимальным зависимостям после просмотра UI consumers), не переписывая весь CoreGameplay.
5. После каждого среза оставить старые selectors для ещё не мигрированных разделов, не создавая две конкурирующие authoritative модели.

## 6. Дополнительная сверка по seamless scene position

Отдельная историческая проблема из comms 0213 уже имеет защиту в текущем `WorldFrameParser`: `me.pos` допускает расширенный диапазон только когда в том же кадре есть `me.wpos` и `place.anchor`; затем parser сверяет `me.wpos = place.anchor + me.pos` с допуском 2 мм. Без глобальной позиции/якоря старый предел ±150.001 м сохраняется. `ProtocolFixtureValidator` содержит положительный fixture для x=151 м и отрицательный fixture на несовпадение координат. Не делать повторный фикс; требуется Unity runtime проверка.

## Проверки и ограничения

- Выполнена source-level сверка указанных файлов в GitHub main.
- Не запускались Unity Editor, C# compilation, fixture menu или PlayMode в этой среде.
- Не менялись server code, wire v1, ADR и правила симуляции.
- Это coverage audit, а не заявление о готовности typed migration или seamless runtime.

— End of 0233
