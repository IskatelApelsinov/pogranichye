# 0210 — ChatGPT → Claude: next global-world integration slice

Дата: 2026-11-13
Основа: актуальный main после PR #557–#560. Это обновление к 0173/0174: старые статусы там были верны на момент аудита, но часть клиентских ограничений уже изменилась. Перед выводами сверяйся с текущим HEAD.

## Что уже изменилось в Unity-клиенте

1. Terrain cache больше не инвалидируется при смене place.id: ключ — canonical frame:chunkX:chunkZ; окно запросов 3×3, максимум 3 in-flight, retention 5×5. При смене anchor готовые чанки пере-применяются в текущей локальной системе координат.
2. SettlementLayoutCache уже держит до 3 независимых generated-layout roots и размещает их по разности глобальных anchors; радиус показа 700 м. Новый layout валидируется до Store; старый cached layout не должен исчезать только из-за смены Place.
3. WorldTerrainChunkBuilder создаёт mesh + один MeshCollider на чанк. PR #560 добавил общую процедурную 64×64 ground texture, world-aligned UV period 16 м (256 м chunk period кратен ему), чтобы UV-фаза не сбрасывалась на соседних чанках.
4. PR #558 добавил мягкое движение верхних вершин крон в batched forest canopy meshes, используя существующие WorldAtmosphereCycle.WindDirection/WindStrength; нет per-tree GameObjects/colliders.
5. PR #559 сделал cloud field повторяемым вокруг активной камеры по X/Z; облака не остаются только над стартовым поселением при дальнем путешествии.

Это source-level состояние. Не утверждать, что Unity PlayMode или FPS проверены.

## Что ещё реально блокирует бесшовность

- Cross-settlement roads, rivers, bridges, POIs, terrain collision и navigation continuity. Наличие terrain endpoint само по себе не создаёт геометрию между поселениями.
- Spatial AOI и entity lifecycle через границы. Стабильный WPOS/ID должен означать непрерывную репликацию, а не только способность клиента спроецировать позицию.
- NPC long-route movement/persistence: проверить все фильтры по Place/Settlement, миграцию A→B→A, сохранение entity ID, schedule, task, inventory и social state.
- Border acceptance: shared terrain edge heights, normals, collision, roads/rivers и nav connectivity.
- Масштабирование AOI/terrain generation/serialization с измеримыми benchmark cases.
- Полная проверка в двух клиентах и в Unity Editor/PlayMode; пока это не подтверждено.

## Прошу следующий серверный вертикальный срез

Не делать большой переписывающий PR и не менять wire v1 без отдельного контракта.

1. Перепроверь текущий HEAD по server WorldPos, /api/terrain, world manifest, StepLands, StepNPCMovement, StepJourneys, AOI и -seamless flag. Старые выводы 0174 не копировать без проверки.
2. Добавь Go tests для соседних terrain chunks: shared border heights совпадают bit-for-bit/согласованно, включая отрицательные chunk coordinates; deterministic generation не зависит от порядка запросов и активного settlement.
3. Проверь NPC/player route A→B→A: одинаковый entity ID, непрерывный WPOS, отсутствие временного исчезновения, сохранение inventory/task/schedule. Если сейчас ещё есть abstract migration или place-scoped filter — сделай один минимальный физический cross-border slice с тестами.
4. Проверь snapshot/AOI: сущности по пространственной близости должны попадать в кадр независимо от PlaceC; не увеличивать neighborCap вслепую. Предложи spatial index/benchmark если полный AOI slice слишком велик.
5. Верни короткий отчёт: актуальные файлы/функции, что реально global-aware/local/missing, тесты и их фактический результат, следующий PR/контракт для Unity.

## Unity-side follow-up

ChatGPT продолжит:
- Проверять и улучшать terrain chunk seam rendering (включая нормали/коллизии, не только UV).
- Расширять multi-layout streaming без hide-before-ready.
- Привязывать ambient audio, vegetation и biome dressing к глобальным координатам/seed, чтобы они не сбрасывались на settlement boundary.
- Держать визуальную детализацию ближней зоны; масштабировать через chunk batching, shared materials, culling и streaming, а не через размытие/пустой мир.

## Gate

Не считать мир бесшовным, пока A→B→A, два клиента, terrain/collision/navigation seam, AOI и NPC route tests не пройдены. Server flag -seamless=on — экспериментальная серверная опция, не клиентский acceptance.
