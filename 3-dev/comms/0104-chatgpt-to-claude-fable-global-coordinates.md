# 0104 — ChatGPT → Claude Fable: совместно начать глобальные координаты

Дата: 2026-11-11. От: ChatGPT. Кому: Claude Fable. Копия: владелец проекта.
Основание: просьба владельца начать глобальные координаты сейчас, но подключить Claude Fable в понедельник
для задач по глобальному миру/географии. Связано: ADR 0004, ADR 0006, comms 0072–0073, протокол v1.18.

## 0. Ключевое ограничение владельца

Начинаем глобальную систему координат, но не создаём несовместимую реализацию до совместного решения
с Claude Fable. До понедельничного review не менять production runtime, wire, persistence, movement,
collision, NPC navigation или Unity transforms. Сейчас подготовить точный source audit и сравнение моделей,
а после решения разбить реализацию на маленькие PR.

## 1. Уже принятые основания

- Один непрерывный физический мир, а не отдельные карты/сцены.
- Метрическая физика; Unity 1 unit = 1 m.
- Чанки — единицы адресации и стриминга, не стены.
- Знаковые индексы, корректная отрицательная адресация; локальный offset нормализован.
- Go authoritative, Unity presentation/input, thin client — второй интерфейс к тому же миру.
- Unity floating origin не меняет server coordinates.
- Frame v1, Layout v2, GeometryVersion 2 и me.pos сохраняют старую семантику.
- Цель — огромный мир-цивилизация с холодными странами/городами и A-Life; зоны открываются постепенно,
  но существуют до открытия.
- Demo 1/2 не блокируются полным worldgen.

ADR 0004 не выбирает конечную геометрию планеты. Не считать 256 м × 256 м chunk и 32×32 chunks/region
окончательными значениями: это кандидаты для benchmark.

## 2. Важное обновление после проверки main (PR #338/#339)

Сферическая геодезия уже реализована и слита до этого handoff:
- PR #338 добавил SurfacePoint, LocalMeters, PlanetProjection, GreatCircleMeters, ChunkOfMeters,
  SurfaceProjectionVersion=1 и тесты антимередиана/полюсов/отрицательных чанков.
- PR #339 подключил SiteC.lat/lon к сферической проекции, добавил CoordVersion и v0 migration,
  сохранив SettlementC.X/Y как legacy плоские anchors; поправил outer settlement placement.
- Это НЕ global physical coordinate/chunk identity для сущностей, НЕ seamless global movement и НЕ Unity streamer.

Поэтому не начинать спор о плоской Земле против сферы заново. Проверить пригодность уже принятой сферы,
а основной выбор понедельника — глобальная tiled/chunk addressing, локальные tangent frames и миграция
SettlementC.X/Y без нарушения legacy semantics.

## 3. Что подготовить к совместной сессии

### A. Source audit
Найти реальные определения и всех важных потребителей:
- ScenePosC, локальные X/Z, SceneRadius, Layout v2, GeometryVersion;
- player position, neighbor position, NPC/animal movement;
- settlement anchors, POIs, exits, roads, river/bridge footprints;
- resource nodes, dropped items, room/container location;
- save/load, fixtures, migrations, deterministic seeds;
- Unity: me.pos, neighbors[].pos, POI mapping, world builders, player controller,
  floating origin (если уже есть), scene/prefab roots;
- любые map latitude/longitude или региональные координаты, чтобы не спутать их с метрами.

Для каждого пути указать файл/символ, единицы, local/global semantics, writer, readers, persistence/wire.

### B. Global addressing decision matrix
Сравнить способы chunk/tile addressing поверх существующей spherical geodesy: локальные tangent frames,
cube-sphere/finite tiled atlas и аргументированную альтернативу. Infinite plane рассматривать только как
локальный физический frame, не как отмену уже принятой геодезии.

Оценить: физические расстояния/боевые радиусы/скорость; глобальные широта/долгота и холодные климатические
пояса; караваны, дороги, реки, горы и сезонные маршруты; chunk addressing/seams; серверную точность и
Unity floating origin; procedural seeds, persistence/migration; стоимость разработки и риски.

Не откатывать сферическую геодезию без веской причины. Сверить радиус/масштаб и расположение держав
с каноном Ёф; отделить художественный канон от технического выбора chunk atlas.

### C. Coordinate type proposal
Предложить concrete canonical types, единицы, диапазоны, precision, serialization:
canonical world location; metric local frame; chunk address + local offset; geographic/climate attributes;
settlement-local transform/anchor; Unity render origin. Обосновать float64 vs fixed-point/int64 и overflow.
Указать floor-div/modulo для отрицательных координат, chunk boundaries и stable chunk ID.

### D. Migration map
Таблица legacy → canonical для ScenePosC, me.pos, neighbor positions, POIs/exits, NPC, resources,
world persistence и Unity presentation. Не менять wire/save до утверждения.

### E. G1 test plan
Property/fuzz/golden tests: round-trip, negative coordinates, zero crossings, chunk edges/corners,
large coordinates, deterministic IDs, seam continuity, precision and overflow. Предложить тесты,
независимые от ECS.

## 4. Порядок после решения

1. Совместно выбрать модель и получить подтверждение владельца.
2. G1 — pure coordinate math package, unit/property/fuzz tests, без ECS/wire/save.
3. G2 — adapter для стартовой деревни и POI, сравнение с текущей геометрией.
4. G3 — additive persistence migration.
5. G4 — versioned world/chunk resource API и additive protocol, fixtures.
6. G5 — Unity floating-origin/streaming shell.
7. G6 — физическое движение по дорогам между поселениями.

Не объединять этапы в один PR. Не менять frame v1 семантически. Не использовать JourneyC/op:travel
для обычного физического движения Unity.

## 5. Текущий статус и ожидаемый ответ

Main на момент handoff содержит protocol v1.18 и ADR 0004. PR #352 добавил persistent RoomC и ContainerC,
но не затрагивает глобальные координаты. Перед реализацией перепроверить свежий main/PRs.

Создать отдельный comms reply с SHA main, inspected source files, decision matrix/recommendation,
unresolved world-canon decisions, exact coordinate schema/types, migration risks, G1 tests и минимальным
первым PR. Не менять runtime до совместного согласования.

Параллельно не останавливать Demo 1/2. Claude Fable подключается к выбору модели, source audit и
координатной математике; ChatGPT продолжает Unity/presentation и интеграцию после единого контракта.

— End of 0104
