# 0229 — ChatGPT: первичный аудит Unity-клиента и серверных стыков

Дата: 2026-11-14  
Основа: чтение файлов из текущего `main`; отчёт source-level, Unity Editor/PlayMode в этой итерации не запускался.  
Задача: `task_ai/001-chatgpt-client-audit.md`.

## 0. Канон

Клиент не является владельцем симуляции. Go-сервер определяет мир и исходы; Unity отображает полученное состояние и отправляет намерения. Два режима входа из ADR 0006 остаются частью одной вселенной. Никаких предложений удалить котёл или заменить концепцию.

## 1. Краткий вывод

Клиент уже имеет отдельные транспортные, протокольные и presentation-классы, но верхний слой архитектурно перегружен. Два особенно крупных монолита:

- `VerticalSliceController.cs` — 3 927 строк: composition/bootstrap, lifecycle соединения, HTTP-загрузка ресурсов, WebSocket callbacks, очередь main thread, movement, streaming, камера/input coordination, UI и большая часть экранов/карты/диалогов.
- `CoreGameplay.cs` — 3 270 строк: одновременно парсит сырой JSON через `JObject/JToken`, хранит текущие `me/place`, управляет очередью intent/RequestGate, combat/weave/mount/activity input, foe/exit views, HUD/feed/quest UI.

Это не означает, что нужно переписать их целиком. Безопасный путь — оставить `VerticalSliceController` composition root на первом этапе и постепенно вынести из него отдельные сервисы/панели, сохраняя один transport, один typed frame model, один entity registry и один coordinate projection.

## 2. Таблица ответственности по фактическому коду

| Область | Текущие файлы/классы | Фактическая ответственность | Оценка |
|---|---|---|---|
| Composition/bootstrap | `Runtime/VerticalSliceController.cs` | Создаёт presentation root, layout cache, entity registry, камеру/input, soundscape, environment, CoreGameplay; управляет соединением и состояниями экрана | Слишком много несвязанных обязанностей |
| Auth + HTTP resources | `Networking/HttpAuthClient.cs` | Register/Login; `/api/layout`; `/api/world` manifest; `/api/terrain` chunk; валидирует DTO | Работает как HTTP gateway, но auth и world streaming смешаны |
| WebSocket transport | `Networking/WorldSocketClient.cs` | Connect /ws, receive loop, UTF-8/1 MiB guard, маршрутизация chat/voice/world, сериализация intent payloads | Отдельный транспортный слой; специализированные методы частично дублируют generic payload |
| Full-frame protocol | `Networking/WorldFrameParser.cs` | Проверяет v=1, типы/диапазоны и разбирает snapshot в protocol DTO | Правильная граница, но используется не для всего потока |
| Full-frame orchestration | `VerticalSliceController.OnSocketMessage` | Отличает fast/full, парсит full frame, обновляет состояние, layout/terrain, journal, entities, CoreGameplay, movement reconciliation, camera | Контроллер владеет слишком большим orchestration pipeline |
| Gameplay state + intents | `Runtime/CoreGameplay.cs` | Хранит raw `JObject _me/_place`, читает feed/result, собирает intent, combat/weave/craft/journey/party/mount, создаёт визуальные foe/exit objects и рисует HUD | Главный boundary debt: raw JSON + gameplay input + presentation/UI в одном классе |
| Entity presentation | `Presentation/EntityViewRegistry.cs` | Создаёт/обновляет временные GameObject по server entity IDs, отображает соседа/игрока/equipment/activity | В целом верная роль, но класс также содержит много procedural character/equipment geometry |
| Settlement presentation cache | `Presentation/SettlementLayoutCache.cs` | Bounded cache до 12 layout roots; видимость до 1050 м; проекция по anchors | Отдельный cache, не владеет terrain/entity state |
| Global position projection | `Presentation/WorldPositionProjection.cs` | Разность double-precision WorldPos относительно текущего authoritative origin и перевод в Unity Vector3 | Единая projection utility найдена; не доказательство полноценного floating origin всего мира |
| Map projection | `Presentation/WorldMapProjection.cs` | Геометрическое преобразование world metres в UI rect | Чистая utility |
| UI/map/chat/login | `Runtime/VerticalSliceController.cs` | OnGUI экраны, character/inventory/journal/map/settings, local/world map, chat/social/context/tavern/minigames | UI нужно отделить от orchestration, не переносить игровую истину в UI |

Пути относятся к `2-client/game/Assets/_Project/Scripts/`.

## 3. Поток сервер → экран

```text
Go server
  ├─ HTTP auth /api/layout /api/world /api/terrain
  │    └─ HttpAuthClient → DTO validation → VerticalSliceController streaming
  │          ├─ WorldTerrainChunkBuilder / environment & layout builders
  │          └─ SettlementLayoutCache
  └─ WebSocket /ws
       └─ WorldSocketClient.ReceiveLoop
            ├─ chat envelope → ChatReceived
            ├─ voice envelope → VoiceSignalReceived
            └─ world JSON → VerticalSliceController.OnSocketMessage
                 ├─ full snapshot → WorldFrameParser.Parse → WorldFrame
                 │    ├─ EntityViewRegistry.Apply(frame)
                 │    ├─ CoreGameplay.ApplyFrame(raw JSON)  [повторный parse]
                 │    └─ movement reconciliation / camera / journal
                 └─ fast frame → CoreGameplay.ApplyFastFrame(raw JSON)
                                  [обходит WorldFrameParser]
```

### Подтверждённый разрыв №1: две модели одного кадра

Full snapshot проходит строгий `WorldFrameParser.Parse` и typed `WorldFrame`, но затем исходная JSON-строка повторно передаётся в `CoreGameplay.ApplyFrame`, где создаются новые `JObject` и хранятся raw `me/place`. Fast frame вообще распознаётся через строковый поиск `"fast":true` и направляется прямо в `CoreGameplay.ApplyFastFrame`, минуя строгий parser.

Это создаёт две параллельные модели состояния и два пути валидации. Риск — поля могут приниматься/отбрасываться по-разному, а UI/gameplay остаётся связан с JSON-именами. Это первый кандидат на поэтапное исправление: typed DTO/selector для gameplay, с сохранением fast-channel semantics и без изменения wire.

## 4. Поток намерения игрока → сервер → подтверждение

```text
ClientInput / UI / CoreGameplay
   └─ Dictionary<string, object> payload
       └─ VerticalSliceController.SendCorePayloadAsync
           └─ WorldSocketClient.SendPayloadAsync
               └─ WebSocket /ws
                   └─ Go server resolves action
                       └─ next full/fast frame (feed/result/state)
                           └─ CoreGameplay.ReadResult / ReadFeed / RequestGate
                               └─ UI status / confirmed visual event
```

Положительное свойство: `CoreGameplay` явно документирует, что не предсказывает попадания/урон/добычу/прогресс; исходы берутся из server frame/feed/result. В `VerticalSliceController.OnCoreRequestResolved` сообщения об успехе/отказе строятся из результата RequestGate. Звук питья пива запускается от server-confirmed transition в entity presentation, а не от одного нажатия.

Риск: создание payload и логика очередей находятся в нескольких местах (`CoreGameplay.QueueRequest`, controller-specific Queue* methods, transport-specific send methods). Нужно инвентаризировать reqid/idempotency per op, не унифицируя их механически: часть операций state-setting, часть требует request correlation.

## 5. Карта: важное несоответствие модели знаний

В `VerticalSliceController.DrawWorldConnectionsMap` геометрия поселений и дорог берётся из `_worldManifest` (`/api/world`). Имена фильтруются по `_frame.Me.KnownPlaces`, но неизвестные settlements всё равно рисуются маркером `?`, а дороги из полного manifest рисуются между известными manifest endpoints.

Иными словами, клиент уже частично скрывает имена, но это ещё не строгое отображение знаний персонажа: сам факт и примерное расположение неизвестных поселений и сеть дорог раскрываются из developer/streaming manifest. Комментарий в `HttpAuthClient.GetWorldManifestAsync` прямо называет manifest streaming metadata, not player knowledge. Нужно получить от Claude точную server-side модель player knowledge/API и отделить:
- streaming metadata, необходимую для загрузки ближайшей геометрии;
- knowledge/map payload персонажа (rumoured/located/visited/known и фактическая семантика текущего протокола);
- то, что вообще не должно отображаться игроку.

Не удалять `/api/world` из стриминга без анализа: это отдельный ресурс геометрии/чанков, но не источник карты персонажа.

## 6. Координаты и стриминг — что уже есть и чего не доказывает

Подтверждено по исходникам и README:

- `WorldFrameParser` валидирует optional WorldPos/anchor и позиции кадра v1.
- `HttpAuthClient` валидирует manifest scheme `yof-tangent`, frame 0, 256 m chunks, 64 m layers, settlement anchors, roads, optional wildsites.
- `/api/terrain` загружается как deterministic 256 m height chunk; Unity строит mesh + static MeshCollider в `WorldTerrainChunkBuilder`.
- `VerticalSliceController.EnsureTerrainChunkForFrame` использует authoritative `me.wpos`, окно 3×3, ограничение in-flight и retention cache; смена place не должна менять canonical chunk identity.
- `SettlementLayoutCache` хранит несколько layout roots и размещает их по глобальным anchors.
- `WorldPositionProjection` переводит глобальные double координаты относительно текущего origin в Unity float coordinates.

Не доказано этими механизмами: полная физическая непрерывность дороги/реки/моста, корректная коллизия на всех швах, полный floating origin для всех представлений/камеры/эффектов, непрерывная AOI/NPC lifecycle через любое число регионов, содержательное наполнение каждого чанка. Heightfield + collider не равны полноценной генерации мира.

## 7. Локальная процедурная геометрия

В `VerticalSliceController.Awake` вызывается `ProceduralVillageBuilder.Build(_presentationRoot)` до подключения к серверу. Это требует отдельной проверки назначения: допустимо как fallback/визуальная декорация/стартовая оболочка, но нельзя считать её источником поселения, POI, проходимости, ресурсов или NPC. Актуальная server layout должна оставаться источником координат и POI; в `CoreGameplay.SetLayout` есть комментарий, что серверный Layout v2 используется для локальных affordances, а сервер всё равно валидирует действие.

## 8. Подтверждённые архитектурные проблемы

1. `VerticalSliceController` (3 927 строк) совмещает connection lifecycle, world streaming, movement, camera/input routing, UI, maps, chat/social/tavern/minigames, journal and settings.
2. `CoreGameplay` (3 270 строк) совмещает raw JSON state, intents/RequestGate, combat/weave/mount, foe/exit visual objects, UI drawing.
3. Full frames разбираются дважды; fast frames обходят строгий typed parser.
4. World map использует full streaming manifest для дорог и наличия неизвестных поселений, маскируя лишь имена. Нужен player-knowledge contract.
5. HTTP auth и world-resource loading объединены в одном классе; это не самый срочный дефект, но границу стоит разделить после typed state.
6. ProceduralVillageBuilder вызывается на старте до серверного layout; назначение нужно явно закрепить как visual fallback/decorative, чтобы не создавать иллюзию авторитетного локального мира.
7. EntityViewRegistry создаёт только presentation objects по server IDs — это допустимо. Однако предел соседей (24) и retention 2.25 сек должны быть согласованы с реальной серверной AOI; не лечить пропажи повышением лимита без server audit.

## 9. Поэтапный безопасный план

### Шаг A — typed gameplay state (первый кодовый шаг после завершения аудита)
- Добавить typed gameplay snapshot/selector поверх уже существующего `WorldFrame` и fast-frame DTO.
- Передавать в `CoreGameplay` типизированное состояние, не raw JSON.
- Строгую валидацию fast envelope сделать отдельной и явной, не парсить его как full snapshot.
- Не менять протокол v1 и не переписывать UI одновременно.

### Шаг B — intent gateway
- Инвентаризировать операции и reqid policy; сделать один клиентский gateway с типизированными командами и correlation.
- Оставить transport в `WorldSocketClient`; не дублировать сериализацию и не ломать legacy action semantics.

### Шаг C — выделение UI
- Выносить панели из `VerticalSliceController.OnGUI` в отдельные presentation components: Map, Inventory, Journal, Settings, Chat/Social, Tavern/Minigames.
- Панели получают read-only view models и callbacks, не JToken, не HTTP/WS clients и не прямой доступ к симуляции.

### Шаг D — streaming/origin
- Выделить WorldStreamingCoordinator только после документирования текущих ownership/cache invariants.
- Сохранить один canonical chunk cache, один layout cache, один entity registry и одну WorldPositionProjection/origin authority.
- Проверять chunk seam, layout overlap, collision и AOI совместно с серверными тестами.

## 10. Следующий ход

- ChatGPT: закончить просмотр всех runtime/presentation builders и map selectors, затем сделать один минимальный typed-state refactor без изменения wire.
- Claude: по задаче `task_ai/002-claude-server-world-audit.md` проверить player knowledge payload, manifest semantics, chunk content descriptors, frontier и NPC/AOI.
- Владелец: выполнить `task_human/001-blender-check.md` после установки Blender.

## Ограничения проверки

Это анализ исходников из GitHub `main`, а не Unity build/PlayMode. Unity README сам помечает полную Unity 6 verification checklist как BLOCKED до запуска Editor. Не считать карту, шрифты, дальний мир, market/horse UX исправленными этим аудитом.


## 11. Дополнительные presentation/input модули

| Класс | Файл | Роль / граница |
|---|---|---|
| Protocol DTO | `Networking/Protocol/WorldFrame.cs` | Typed snapshot graph (635 строк), включая Place/Layout/WorldPos и вложенные структуры. Это правильная модель для UI/gameplay, но `CoreGameplay` пока её не использует напрямую |
| World input | `Runtime/ClientInput.cs` | Единая Input System action map; отдаёт semantic actions, не решает игровые исходы |
| Interaction probe | `Presentation/WorldInteractionProbe.cs` | Короткий луч выбирает presentation target; комментарий прямо запрещает ему решать gameplay |
| Camera | `Presentation/PresentationCameraRig.cs` | Камера и режимы обзора; явно presentation-only, не меняет authoritative world position |
| Request correlation | `Runtime/RequestGate.cs` | Pure C# очередь result-bearing intents; строго один запрос in-flight, ждёт matching reqid из `me.result`, имеет timeout |
| Terrain mesh | `Presentation/WorldTerrainChunkBuilder.cs` | Делает визуальный mesh и статический MeshCollider для серверного height chunk; не создаёт географические данные |
| Wilderness landmarks | `Presentation/WorldWildSitePresentationBuilder.cs` | Рисует landmarks из WorldManifest, позиционируя относительно authoritative player WPOS |
| Atmosphere | `Presentation/WorldEnvironmentBuilder.cs` | 1 210 строк; локальный визуальный sky/weather/lighting/post-processing. Не является симуляцией климата/погоды |
| Prototype village visuals | `Presentation/ProceduralVillageBuilder.cs` | 4 478 строк; детерминированная disposable prototype geometry/textures, прямо помеченная как presentation-only, not authoritative and not collidable |
| Nearby settlement geometry | `Presentation/SettlementLayoutCache.cs` + layout builders | Кэш и визуальное отображение серверного Layout v2; POI coordinates берутся из layout, сервер всё равно валидирует действие |

### Важное уточнение по локальной генерации

Локальные `ProceduralVillageBuilder` и `WorldEnvironmentBuilder` действительно крупные, но их комментарии определяют их как presentation-only. Это само по себе не нарушение серверной архитектуры, если они используются для декора/временного preview и не определяют фактическую карту, коллизии, POI, ресурсы или NPC. Однако `ProceduralVillageBuilder.Build(..., "tutorial-preview")` вызывается из `VerticalSliceController.Awake` ещё до server snapshot; поэтому визуально он может создавать ощущение локальной заранее заданной деревни. Следующий безопасный шаг — явно разделить fallback/decorative preview от server-driven layout и удалить/скрыть декоративные элементы, которые дублируют серверную геометрию, но не заменять их новым локальным генератором мира.

### RequestGate и input — хорошие границы, которые нужно сохранить

`ClientInput` уже централизует ввод. `WorldInteractionProbe` только выбирает цель. `RequestGate` отдельно сериализует операции, где сервер возвращает только последний `me.result`, и потому не допускает нескольких result-bearing запросов одновременно. Эти классы не нужно поглощать новым «универсальным менеджером». При рефакторинге их границы сохраняются.

## 12. Итоговый статус аудита

Карта основных клиентских обязанностей и двух сетевых потоков составлена. Первичный refactor target — убрать raw JSON из `CoreGameplay` и передавать ему typed state, не меняя протокол v1. Далее — вынести UI из controller и уточнить player knowledge map contract совместно с Claude. Полный runtime acceptance всё ещё требует проверки в Unity 6000.6.7f1.
