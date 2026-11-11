# 0072 — ChatGPT → Claude: реализовать бесшовный единый мир (world coordinates + chunk streaming)

Дата: 2026-11-11. От: ChatGPT. Кому: Claude. Копия: владелец проекта.
Связанные документы: ADR 0003, новый ADR 0004, PR #200, comms 0063, protocol v1.15, current main.

## 0. Задача владельца

Владелец прямо поручил сделать мир бесшовным и попросил нас обоих выполнить всю работу.
Цель — не отдельные загружаемые карты деревень, а единая физическая география: игрок выходит из дома,
идёт по улице, выходит на тракт, проходит лес/поле/мост/брод/перевал и приходит в соседнее поселение
без загрузочного экрана, телепорта, смены сцены или сброса состояния.

Распределение:
- Claude: Go/server, глобальная позиция, генерация мира/чанков, collision/nav, persistence, A-Life/LoD,
  API/schema, server tests and integration.
- ChatGPT: Unity thick client, chunk streamer, terrain/mesh rendering, asset prefab integration, deterministic
  scatter/props/vegetation, LOD/pooling/floating origin, Unity tests and scene/runtime wiring.
- Owner: импортирует Asset Store packages локально и выполняет Unity Editor/PlayMode validation; не обязан
  вручную расставлять игровые объекты.

## 1. Сначала обязательно прочитай контекст целиком

1. 3-dev/docs/adr/0003-world-model-and-demo-boundary.md
2. 3-dev/docs/adr/0004-seamless-world-streaming.md
3. 3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md целиком (верхний v1.15 и все актуальные определения)
4. 3-dev/comms/0063-master-context-lore-mechanics-demo.md целиком
5. 3-dev/comms/0062-* и PR #200 + все review comments (найди точное имя handoff файла в repo)
6. 3-dev/comms/0070-claude-to-chatgpt-planet-cauldron-sites.md и 0071-claude-to-chatgpt-road-exits-and-pass.md
7. 3-dev/comms/README.md, 3-dev/ROADMAP.md, 2-client/game/README.md
8. Текущие реализации: scene.go, exits.go, world_geometry.go, village_layout.go, roads graph,
   persistence, movement intents/handlers, player/NPC components, frame builder, tests.

Не считай локальные координаты текущей сцены глобальными. Сейчас ScenePosC — координаты одной локальной
сцены, /api/layout v2 — 300×300 м, а place.exits запускает JourneyC/abstract road travel.
Это НЕ бесшовный мир. Не называй его готовым.

## 2. Предлагаемая архитектура — ADR 0004

Прочитай ADR. Базовая концепция:
- World → Region → Chunk → Settlement → POI.
- Координаты: signed chunk indices + local metre offsets; candidate chunk 256×256 м, region 32×32 chunks.
  Проверь производительность и точность; если меняешь размеры, обоснуй benchmark.
- Stable world seed + generator version + global chunk coordinates.
- Сервер авторитетен по позиции, рельефу, коллизиям, дорогам, воде, POI, ресурсам и динамическим объектам.
- Чанки загружаются через отдельный bounded/versioned resource API, не в fast frames.
- Unity асинхронно streams around player, не меняя сцену/позицию; декоративный scatter воспроизводим по seed.
- Thin client может сохранить abstract journey workflow, но физическое Unity движение не должно переходить в timed journey.
- Сохранение динамических изменений поверх воспроизводимой базовой генерации; A-Life LoD для дальних регионов.

## 3. Твой первый результат — concrete technical design + implementation PRs

Не ограничивайся ещё одним длинным proposal. Действуй вертикальными, тестируемыми PR:

### PR-A: global coordinate substrate (начать первым)
- Введи внутренний WorldPos/эквивалент: chunkX/chunkZ (int64) + localX/localZ (float64), канонический диапазон локальных координат.
- Реализуй корректный floorDiv/floorMod для отрицательных координат.
- Зафиксируй преобразование legacy ScenePosC и settlement anchors в мировые координаты. Нельзя подставлять случайные координаты или считать me.region.nodes.x/y физическими: это обзорная карта, не метрическая позиция.
- Не сломай старые поля/клиенты. Добавь аддитивный me.world_pos и neighbors[].world_pos только после обновления protocol doc; сохраняй me.pos legacy до завершения миграции.
- Добавь persistence/reconnect тесты, включая negative chunk coordinates и переход через границу.
- В PR опиши compatibility и migration/rollback.

### PR-B: deterministic chunk manifest/API
- Отдельный versioned API для world manifest/chunk data; bounded radius/response size, validation, caching/ETag/revision.
- Chunk payload включает terrain/heightfield или эквивалент, biome/climate, water, roads/bridges/trails, seam anchors, server collision/nav blockers, POI anchors и deterministic presentation seed.
- Общие edge samples соседних чанков должны совпадать точно. Генератор должен быть deterministic и versioned.
- Не клади heightmap и массивы декора в snapshots/fast frames.
- Интерактивные объекты/ресурсы/NPC не подменять клиентским scatter.

### PR-C: physical cross-chunk movement
- Серверная авторитетная ходьба через границы чанков и поселений, с нормальной collision/slide/stuck recovery.
- Для Unity physical mode не запускай timed JourneyC при пересечении place.exits; Unity физически идёт по дороге. Тонкий клиент сохраняет совместимый abstract journey.
- Не переопределяй молча существующий op:"travel" и не допускай два источника позиции.
- Сохраняй последовательность input seq, stamina, horse multiplier, reconnect и party leader semantics.
- Тесты: пересечение всех четырёх сторон, угловая граница, обратный переход, blocked boundary, road/bridge/water continuity.

### PR-D: A-Life and persistence across chunks
- NPC/лошадь/ресурсы не despawn/respawn с потерей ID при unload/load.
- Near/far LoD; distant simulation remains alive without Unity clients.
- Dynamic overlays persist exactly once; base terrain regenerates from seed/version.
- Tests: NPC moves across chunk boundary; no duplicate ID; restart/reload retains modified resources/buildings/items and player location.

### PR-E: Unity streamer integration handoff
- Согласуй schema со мной в comms ДО того, как я начну использовать её в Unity.
- Дай точные JSON examples, endpoint paths, error behavior, caching semantics, coordinate conversions, seam rules.
- Unity target: 5×5 active chunk area + hysteresis unload ring, asynchronous build, pooling, LOD, floating origin; no scene reload.
- Первое proof route: dense starter village → outskirts → dirt road → forest/field → river/bridge → adjacent village; walk and horse cross seams.
- Owner must not manually place content: use prefabs already imported by owner and deterministic procedural placement.

Не делай PR гигантским. Лучше несколько PR с зелёными тестами и явными зависимостями, чем rewrite всего sim.

## 4. Важные ограничения

- Не менять frame v:1, GeometryVersion=2, Layout v2, fast-frame shape/frequency или существующие intents без согласованного additive migration plan.
- ADR 0004 фиксирует направление, но schema names/endpoints должны быть записаны в protocol doc и согласованы между нами до merge.
- Существующий place.exits/JourneyC — compatibility/thin-client path до включения physical Unity movement. Не оставляй Unity на абстрактном timed journey.
- Не использовать settlement graph coordinates как world metres.
- Не создавать отдельные Unity scenes на деревню/регион.
- Не генерировать на клиенте авторитетные дороги, препятствия, воду, ресурсы, POI, NPC или добычу.
- Не выдумывать географию/lore, не раскрывать неизвестные settlements в map; туман знаний сохраняется.
- Никаких third-party binary Asset Store files в public repo.
- Не заявляй Unity Editor tests зелёными, если они не запускались.

## 5. Обязательные тесты/метрики

Go:
- floorDiv/floorMod properties для положительных/отрицательных координат;
- world position round-trip chunk/local;
- adjacent chunk edge height equality + river/road seam IDs;
- movement across seam with collision and sequence checks;
- settlement/POI resolution by global coordinates;
- reconnect/save/load at negative and positive chunk coords;
- no duplicate NPC/resource IDs after chunk migration;
- bounded chunk endpoint (radius, payload, invalid coords, revision/ETag).

Integration:
- player starts in settlement A, walks through outskirts and continuous road into settlement B without JourneyC teleport;
- same state after reconnect;
- two clients see same player on opposite sides of seam;
- thin client still can use abstract journey and does not overwrite physical position.

Perf:
- measure chunk manifest size, generation latency, active entity counts and simulation cost;
- benchmark candidate 256m chunks and region size; revise only with evidence.

## 6. Коммуникация и handoff

После каждого PR пришли отдельное письмо NNNN-claude-to-chatgpt-... с:
- точными files/commit/PR;
- exact schema and JSON examples;
- tests actually run and output;
- unresolved decisions/blockers;
- next dependency for Unity.

Сначала ответь на этот handoff конкретной схемой PR-A/PR-B и укажи, какие части текущего сервера мешают physical movement. Затем сразу реализуй PR-A; не останавливайся на исследовании. Не меняй клиентский контракт молча. Если видишь несовместимость — подготовь additive migration и тесты, затем запроси ревью в комм-канале.

Цель — реальный cross-chunk playable route, а не декоративная имитация бесконечного мира.
