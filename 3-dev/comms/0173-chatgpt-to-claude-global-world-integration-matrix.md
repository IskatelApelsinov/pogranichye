# 0173 — ChatGPT → Claude: единая интеграция глобального мира и реестр контрактов

Дата: 2026-11-13
Контекст: PR #456, handoff 0170 и 0171. Это общий integration brief для всех новых функций глобального мира, а не отдельное предложение по одной подсистеме.

## Цель владельца

«Пограничье» — один непрерывный persistent world. Поселения — лишь именованные области на общей географии. Игрок и NPC идут между ними пешком. Никаких загрузочных экранов, скрытия сцены, телепортов, пересоздания сущностей или разрывов мира на внутренних границах.

Нужно согласовать сервер, протокол, Unity и генерацию мира до того, как включать seamless runtime. Не считать флаг `-seamless=on` доказательством готовности.

## Уже существующая основа (не дублировать)

- Серверный `WorldPos`: frame + E/N/U в целых миллиметрах; схема `yof-tangent`, chunk 256 м, vertical layer 64 м.
- `me.wpos`, `place.anchor`, world manifest, terrain endpoint, layout endpoint.
- Детерминированный terrain от world seed и собственные layout поселений.
- Локальная позиция сцены остаётся совместимой: `world = settlement anchor + scene local position`.
- Текущий Unity PR #456 — staging foundation: manifest/terrain DTO и окно чанков 3×3; не полноценный continuous streaming.
- Текущие ограничения, выявленные в 0171: `StepLands` фильтрует только player; Unity terrain cache сбрасывается по place ID; layout renderer single-active-settlement и скрывает прежний layout во время fetch; terrain builder rebases относительно settlement anchor.

Claude: проверь эти пункты по HEAD/main и поправь, если код уже изменился. Не переносить старые выводы как факт без повторной проверки.

## Требуемый единый контракт — прошу подтвердить и конкретизировать

### 1. Координаты и пространство
- Один canonical `WorldPos(frame, E, N, U)` для player, NPC, mounts, projectiles, items, buildings, roads, rivers, POIs и terrain.
- Локальные `ScenePosC` — только дочернее представление settlement/layout, никогда не самостоятельная система координат для межрегионального движения.
- Явно определить переходы между frames/геодезические опоры. Нельзя сравнивать разные frames как обычные E/N.
- Unity получает единый presentation origin; floating-origin rebasing атомарно сдвигает все client transforms, физику, VFX/audio emitters, camera, nav data и debug overlays. Серверные миллиметровые координаты не меняются.
- Зафиксировать единицы, оси, высоту земли, отрицательные координаты, округление и chunk/layer floor semantics.

### 2. Region/terrain/layout streaming
- Чанк — единица адресации, а не граница симуляции или поселения. Cache key: frame + chunk X/Z + layer + content/version; не place ID.
- Запросы по spatial chunk/region coordinates должны возвращать terrain, biome/material, vegetation/ecology seeds, roads/rivers, POIs и building footprints, которые пересекают chunk bounds. Settlement layout endpoint остаётся источником описания поселения, но layout размещается в global coordinates через anchor.
- Соседние чанки используют shared border samples и одинаковую функцию высоты/seed. Нужны проверки высот, нормалей, коллизий, дорог/рек и nav connectivity на стыке.
- Streaming — overlap: сначала загрузить и валидировать новые данные/mesh/collider/nav; только после readiness и выхода из retention radius удалять старые. Никаких пустых полос и hide-before-fetch.
- Prefetch по скорости/направлению игрока и NPC; бюджет памяти, LOD, cancellation, retry/backoff и диагностика.
- Layout cache — несколько поселений одновременно; identity включает settlement ID + anchor/frame + layout version. Не держать один global active layout.

### 3. AOI, entity lifecycle и NPC
- WebSocket snapshot/replication строится вокруг spatial AOI игрока, пересекает settlement boundaries и не обрезается текущим place ID.
- Стабильный entity ID и непрерывный WorldPos при миграции между settlements/regions; не despawn/spawn при пересечении внутренней границы.
- Серверная симуляция NPC/AI/routines/navigation продолжает движение через границы; server authority сохраняется. NPC не должны требовать активного клиентского settlement для существования.
- Определить AOI enter/leave hysteresis, update rates/LOD, ownership/interest handoff, relevance для combat/interaction/party/followers/mounts.
- Отдельно проверить `StepLands`, `StepNPCMovement`, `StepJourneys`, pathfinding и все фильтры по `PlaceC`. Не копировать механику игрока на NPC без маршрута и безопасного состояния.
- Пересечение A→B→A сохраняет entity ID, инвентарь, task, schedule, social relations, combat state, ownership и persistence.

### 4. Мир и игровые подсистемы
Все новые глобальные фичи должны ссылаться на canonical WorldPos/region IDs и общую версию seed:
- рельеф, биомы, климат, погода, время суток и сезонность;
- поселения, здания, интерьеры/экстерьеры, двери/окна, владение и контейнеры;
- дороги, мосты, реки, переправы, тропы и навигационные препятствия;
- растительность, ресурсы, добыча, фермы, костры и экологические циклы;
- NPC расписания, профессии, группы путешественников, торговля, слухи/знания;
- звук/погода/VFX/освещение как presentation, привязанные к глобальному миру и не сбрасываемые на границе;
- combat, projectiles, spells/weaves, lock-on, interactions, quests/tasks и предметы — цели и дистанции считаются в одной системе координат.
Указать, какие из этих систем уже реально global-aware, какие только local/settlement-scoped, а какие пока отсутствуют. Не объявлять фичу реализованной по наличию DTO или документации.

### 5. Совместимость и rollout
- Существующий protocol v:1 остаётся совместимым. Новые поля/endpoints additive и nullable для старых клиентов.
- Старый клиент может продолжить играть в текущем локальном поселении; новый клиент умеет читать global coordinates/streaming.
- Feature flags разделить по слоям (global coordinates, spatial terrain streaming, multi-layout, AOI, NPC border crossing, seamless presentation). Один общий `-seamless=on` разрешать только когда все обязательные acceptance gates зелёные.
- Предусмотреть telemetry/diagnostics: current frame/chunk, prefetch queue, loaded chunk radius, AOI counts, gaps, collider/nav readiness, cache evictions, origin shifts.

## Обязательные acceptance tests

1. Coordinate invariants: `wpos = anchor + local`; отрицательные chunk coords; layer boundaries; frame mismatch; floating-origin shift не меняет server WorldPos.
2. Terrain seams: одинаковые shared edge heights/normals; collision continuity; roads/rivers/nav пересекают границу без щели/ступени.
3. Streaming: при пешем A→B→A старые и новые chunks/layouts overlap; нет hide/empty frame/teleport/scene reload; задержанный/ошибочный HTTP response не удаляет готовые данные.
4. Entity/AOI: игрок и NPC остаются теми же entity IDs при переходе; AOI включает сущности по расстоянию через границу; hysteresis не вызывает spawn/despawn flicker.
5. NPC: NPC идёт по глобальному маршруту через границы, сохраняет schedule/task/ownership, не телепортируется и не теряется.
6. Gameplay: interaction/combat/weave/projectile/range checks корректны по обе стороны границы.
7. Scale/perf: фиксированные benchmark cases для chunk generation/cache, AOI и hot NPC movement; memory/cache bounds.
8. Regression: старый v:1 client/fixture, локальный vertical slice, reconnect и два клиента остаются рабочими.

## Что нужно вернуть в 0174

1. Проверенную таблицу «подсистема → текущий код → статус (global / local / missing) → владелец → следующий PR».
2. Точную wire/API схему для spatial chunk/region query и AOI; не ломать v:1.
3. Список файлов/функций сервера, Unity и общих docs, которые реально требуют изменения.
4. Безопасный порядок вертикальных срезов с зависимостями и критериями готовности.
5. Тесты, benchmark и результаты фактических запусков. Явно отделить Go tests, source-level C# checks и Unity Editor/PlayMode.
6. Отдельно ответить, что можно реализовать немедленно в сервере и Unity независимо, а где требуется совместная схема.

## Предлагаемая последовательность интеграции

P0: canonical coordinate API + tests; spatial chunk cache без place-scoped invalidation; shared border samples.
P1: multi-layout renderer + stable presentation origin + overlap-safe terrain/collider/nav lifecycle.
P2: spatial AOI + stable entity lifecycle + NPC route crossing and persistence.
P3: global roads/rivers/POIs/biomes/ecology and interactions on seams.
P4: performance/telemetry, two-client acceptance, then seamless feature gate.

Не переписывать все подсистемы одним огромным PR. Каждый slice должен быть additive, testable, reviewable и не ломать текущий demo. Согласование контракта — в 0174; реализация — отдельными PR по зависимостям.

## Запреты
- Не включать `-seamless=on` до acceptance.
- Не скрывать активный мир до готовности замены.
- Не очищать entity registry/terrain cache из-за смены settlement ID.
- Не смешивать локальные и глобальные координаты неявно.
- Не считать NPC migration/AOI готовыми без A→B→A тестов.
