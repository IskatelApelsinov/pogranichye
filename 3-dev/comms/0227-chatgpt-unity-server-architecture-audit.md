# 0227 — Единый аудит Unity ↔ Go: владение подсистемами, карта, генерация и старт мира

Дата: 2026-11-14
База аудита: main, HEAD `c3339c7fb7197cdc09237f2bf3cea76425fd3eec`
Владелец: ChatGPT — Unity/client; Claude — Go/server; владелец проекта — локальная генерация ресурсов/проверка.
Статус: source audit, не runtime acceptance.

## 0. Главный вывод

Сейчас клиент не является единым stream/render pipeline. Это набор частично связанных подсистем: один большой `VerticalSliceController` координирует сеть, world manifest, terrain, layouts, presentation и HUD; `CoreGameplay` одновременно парсит server JSON через JToken, рисует игровые меню/карты/торговлю/инвентарь и формирует intents; несколько presentation builders независимо создают мир. Это не доказывает, что код написан разными авторами, но подтверждает архитектурную связанность и размытые границы ответственности.

Второй фундаментальный разрыв: server global world model существует, но его content API недостаточен для визуально насыщенного мира. `/api/world` — служебный manifest всех поселений, а не карта игрока. `/api/terrain` возвращает heightfield + anchors поселений, но не biome, vegetation, roads, rivers, collision/nav, landmarks. Unity не может из одного heightfield восстановить содержимое мира.

Третий разрыв: решения о старте противоречат новой цели. `GenerateStarter` строит одну специальную деревню Починок; `Generate` — другой режим. Есть отдельный cauldron/open-world/gateway канон в ADR 0003/0006, а ADR 0004/0007 задают один непрерывный мир. Владелец теперь явно просит отказаться от специальной стартовой деревни/котла как обязательной игровой зоны.

## 1. Аудированная таблица владения

| Подсистема | Unity/client: текущая точка входа | Go/server: источник истины | Статус на main | Целевая граница |
|---|---|---|---|---|
| Bootstrap/session | `VerticalSliceController` login/connect lifecycle | `server/server.go` world/session load/create | Работает для текущего demo path; два режима генерации остаются | Сервер создаёт/выбирает мир и spawn; Unity только подключается |
| Wire parsing | `WorldFrame.cs`, `WorldFrameParser.cs` | snapshot builder в `server.go` | v:1 additive DTO; snapshot всё ещё большой JToken-driven client | DTO typed, валидируемый contract; unknown fields ignored |
| HTTP API | `HttpAuthClient.cs` | `/api/world`, `/api/terrain`, `/api/layout` handlers | Manifest/layout/heightfield существуют | Versioned spatial chunk-content API + manifest по интересу игрока |
| WebSocket replication | `WorldSocketClient.cs`, controller callbacks | server snapshot + AOI funcs | Player/NPC replication partly place/AOI scoped; cross-boundary not fully proven | Spatial AOI by canonical WorldPos, stable IDs, tombstone/TTL |
| Coordinates | `WorldPositionProjection.cs`, `WorldMapProjection.cs` | `sim/worldpos.go`: WorldPos frame+E/N/U mm, chunk 256m | Canonical server WorldPos exists; client presentation origin/floating origin not complete | One origin coordinator; all scene roots/physics/audio/nav shift atomically |
| Terrain streaming | `VerticalSliceController.UpdateWorldStreaming`, `WorldTerrainChunkBuilder` | `sim.TerrainChunk` | 3×3 requests, 5×5 retention; current API heightfield only | Content chunk includes biome/surface/water/roads/foliage seeds/collision/nav metadata; overlap-safe |
| Settlement layouts | `SettlementLayoutCache`, `WorldLayoutPresentationBuilder` | `LayoutOf`, `/api/layout?settlement=id` | Multiple layout cache/prefetch exists; buildings/props still mostly procedural placeholders | Layout is one content layer inside global chunk space; not a world boundary |
| Wild content | `WorldWildSitePresentationBuilder`, `WorldEnvironmentBuilder` | `WildSitesView`, worldgen | Some wildsite markers/environment; not continuous biome/forest/ecology generation | Server describes deterministic ecological regions/resources; Unity scatters decorative instances from server seeds |
| Static roads/rivers | Current layout builder + map draw | `RoadGraph`, settlement links | Manifest roads only `a/b`; map draws straight lines between nodes; no authoritative spline geometry | Server route graph/splines and bridge/pass/water crossings; client meshes them |
| Dynamic entities | `EntityViewRegistry`, `CoreFoeView`, `WorldNpc` | snapshot `neighbors`, `foes`, AOI | Stable IDs and act_code presentation; normal neighbors still not fully cross-settlement spatial AOI | One entity registry keyed by server ID, server AOI enter/update/leave semantics |
| Player movement | `ClientInput`, controller movement intent queue | server movement/StepLands | Intent-based, server authority; seamless land handover exists | Continuous WorldPos; no place transition side effects in presentation |
| NPC movement | `EntityViewRegistry`, `EntityMotion` | `StepNPCMovement`, `StepJourneys`, `MigrationC` | Some physical border crossing; long routes/AI still partly abstract | Server route+schedule simulation globally; Unity interpolation only |
| Gameplay/UI | `CoreGameplay` | intent handlers in sim/server | One class owns too many UI/gameplay responsibilities; JToken parsing throughout | Split presentation UI from typed state selectors and intent gateway |
| Map/fog | `VerticalSliceController.DrawMapTab`, `CoreGameplay.DrawRegion` | `me.region`, `known_places`, world knowledge | Regional map is based on server nodes/roads and knowledge state; separate local map is layout-derived. `/api/world` must not leak as player knowledge | One map model built from server-discovered geography; fog/rumours/visited/known states; dynamic roads/POIs |
| Economy/market | `CoreGameplay.DrawMarketContextActions` | market stock/trade intent/reqid | UI correctly displays server market rows; empty market likely server stock, not client bug | Server controls finite stock/production/restock; client explains unavailable stock |
| Horse/mount | `CoreGameplay` horse UI; entity presentation registry | `sim/horse.go` HorseC/RiderC | Server logic exists; full client/server E2E not confirmed | Server owns horse position/state/ownership; client renders and sends mount intents |
| Inventory | `CoreGameplay.DrawInventoryTab` | InventoryC/StashC/Bag/HorseC/PurseC | One UI conflates several ledgers; data is server authoritative | Typed inventory view-model and distinct containers/slots/actions |
| Audio/lighting/weather | `WorldAudioDirector`, `WorldEnvironmentBuilder` | world clock/weather/state | Presentation mostly client-side; some server clock/environment inputs | Presentation reads server time/biome/weather, never authoritative gameplay |
| Content assets | `SlavicVillageAssetLibrary`, `ProceduralTextures`, builders | server content descriptors/IDs | Asset selection and runtime geometry mixed with gameplay scripts | Content pipeline: generated/imported assets in Addressables/asset catalog, server IDs map to client prefabs |

## 2. Current server world and spawn facts

### Server generation
- `sim.Generate(seed, settlements)` creates multiple settlements, regions, residents and wild sites.
- `sim.GenerateStarter(seed)` creates a special predictable single settlement `Починок`, with curated roster and extra supplies.
- `generateOuterWorld` currently creates a finite batch of 20 villages + 5 cities in outer regions; source comment says reachable only via `op:travel`, not on foot.
- `NewSettlement` uses region biome/anchor and procedural layout generation. Settlement placement is generated in a map coordinate domain; it is not yet a complete planet-scale spatial generator.
- `OpenFrontier` can append another ring for an outer region, but physical StepLands does not currently call it in the audited code. It is capped by FrontierCfg and has outer-region guard.

### APIs
- `/api/world` returns world scheme, frames, settlement anchors, road edges `a/b`, wildsites. Code comment explicitly calls it a service/developer/streaming manifest, not the player's map.
- `/api/terrain?cx&cz&step` returns a 256m chunk height grid and settlement centers. It does not yet return biome, vegetation, water, road splines, trails, chunk POIs, static blockers or ecology descriptors.
- `/api/layout?settlement=id` returns one settlement's local layout. Layouts are not the wilderness world.
- Player map is separately built from `me.region.nodes/roads` and `known_places`; the Unity regional map already hides unknown names and renders unknown road ends nameless. However its roads are straight node-to-node segments and depend on the server's current regional graph. The map is not yet a cartographic projection of explored terrain/actual route geometry.

### Consequence
The user's observed empty horizon is consistent with the source: a terrain heightfield is not a populated world. Manifest refresh and layout prefetch only load known settlements; they do not synthesize forests, fields, rivers, roads, landmarks or encounters between them.

## 3. Architectural contradictions to resolve

1. ADR 0003 defines a finite demo cauldron, mountain wall and special gateway; ADR 0006 reaffirms the cauldron and special start.
2. ADR 0004/0007 define one continuous physical world and say settlements are local areas, not separate maps.
3. ADR 0008 hex cells are still an RFC proposal, not a runtime dependency. Keep square 256m terrain chunks; do not start a second parallel cell cache.
4. Current code has two world generation entry points with different content guarantees. A special starter map should become a spawn profile/seeded starting circumstance, not a separate geography generator or hardcoded village.
5. Do not use the global developer manifest as player map. Keep server world truth separate from player knowledge.

Owner decision (2026-11-14): the special starter village/cauldron/gateway is no longer the target. A player starts at a server-selected spawn in the same generated world used by everyone. Spawn may be a generated hamlet, roadside camp, farmstead, inn or wilderness edge, selected for safe onboarding; it is not a bespoke map, guaranteed village, or separate world.

## 4. Target architecture

### Authority and data flow
1. World seed + generator version + spatial coordinates determine immutable baseline geography/content.
2. Server stores all mutable deltas: buildings, ownership, harvest, containers, destroyed/placed props, NPC schedules/positions, discovered knowledge, economy, roads changed by players.
3. Server exposes spatial chunk descriptors by (frame, chunkX, chunkZ, layer, contentVersion); settlement layout is a content overlay indexed into the same global space.
4. Unity requests nearby chunks in movement direction, validates response/version, builds meshes/colliders/roads/water/foliage, and keeps overlap until new area ready.
5. Server snapshots dynamic entities via spatial AOI by WorldPos; entity IDs remain stable through settlement/chunk/cell boundaries.
6. Player map is generated from player-specific discovery state and actual server route/terrain graph; unknown space is fog. Rumoured nodes are approximate/unnamed, located nodes show map position, visited/known nodes show appropriate detail.
7. PC generation pipeline creates optional visual assets only. It must never authoritatively decide terrain passability, settlement placement, resource stock, NPC, road connectivity or player map discoveries.

### Server contract needed
- World header: seed/version/projection/frame definitions, world radius/regions/biome schema, chunk size.
- Spatial chunk content: heightfield, biome/material IDs, climate/moisture/elevation bands, water body/stream IDs and polylines, roads/trails/bridges/pass splines, POI/settlement footprints intersecting bounds, foliage scatter seed+density+species palette, resource/wildlife descriptors, collision/nav blockers, content revision.
- Global object IDs and canonical WorldPos for all interactable/static objects; chunk response references IDs, does not duplicate mutable object truth.
- Player map delta: discovered regions/landmarks/settlements/roads with knowledge state, positions/uncertainty, and route geometry; server validates what player can know.
- Spawn response: stable spawn id, WorldPos, spawn context, safe radius, initial visible/discovered knowledge, initial task optional. Must be generated from normal world rules and persist on reconnect.
- AOI: enter/update/leave or snapshot with TTL/tombstones, exact distance filter after broad phase, hysteresis; two players on opposite sides of a settlement border see the same IDs.
- Keep existing v:1 additive compatibility; use new endpoint/resource version for chunk content rather than bloating every fast frame.

### Unity module boundaries to enforce
- `Bootstrap/Session`: auth, connect, reconnect, lifecycle.
- `Networking`: HTTP/WS transport only, cancellation, retries, no world-building logic.
- `Protocol`: typed DTOs + validation; no gameplay UI.
- `WorldStreaming`: manifest/chunk/layout request scheduling, priority, cache/refcount, overlap, origin shifts.
- `WorldPresentation/Terrain`: heightfield meshes, colliders, material/biome blending.
- `WorldPresentation/Content`: roads/water/vegetation/rocks/props and asset catalog.
- `WorldPresentation/Settlements`: layout overlays, buildings/interiors/signs.
- `EntityReplication`: stable-ID registry, interpolation, LOD; no local authority.
- `PlayerInput`: capture and send intents; no client-side authoritative movement.
- `GameplayState`: typed server state selectors and action/reqid gateway.
- `UI/Character`, `UI/Inventory`, `UI/Market`, `UI/Map`, `UI/Settings`: rendering only; no raw JToken parsing or HTTP calls.
- `Audio/VFX/Environment`: presentation driven by server time/state.
- `Diagnostics`: frame/chunk/AOI/cache/origin/asset metrics.

## 5. Migration sequence (no big-bang rewrite)

P0 — Freeze and map current contracts:
- Inventory all DTO fields, endpoints, intents, entity types, global/local coordinate conversions.
- Add one architecture ownership document and a single typed client gateway. No behavior change yet.
- Mark each subsystem global-ready/local-only/missing with evidence and tests.

P1 — Remove special start-world fork:
- Replace new-world bootstrap's implicit `GenerateStarter` with a generated-world spawn selection.
- Reuse `Generate`/shared generation primitives; make starting supplies/first task a spawn profile attached to an ordinary generated location, not a named hardcoded village.
- Persist spawn assignment; reconnect must return same player and world state.
- Seed-based deterministic tests: several seeds produce different valid starts; no guaranteed `Починок`; each start has safe spawn radius, reachable shelter/water/food or safe onboarding alternative, nearby discoverable point of interest.
- Do not remove legacy save compatibility or thin-client journey immediately. Keep old saves in old location.

P2 — Fill continuous world content:
- Server chunk descriptors + deterministic biome/vegetation/water/roads/POI/resource geometry.
- Unity chunk-content builder and generated asset catalog; roads follow real server splines, not anchor-to-anchor straight lines.
- Ensure any heading has continuous terrain and a discoverable route/landmark/settlement within configured maximum travel interval. This is a worldgen density guarantee, not teleportation.
- Define planet/frame boundary behavior; don't pretend an infinite plane is a planet. If world is finite, wrap/transition frames consistently and make exploration continue across frame boundaries.

P3 — Make map truthfully dynamic:
- Server map state is player knowledge, not all `WorldManifest` settlements.
- Reveal actual traversed geometry/roads and discovered POIs; rumour/known/visited states have distinct precision and display.
- No pre-drawn village graph or fake straight roads; route previews use server path geometry and reachable navigation graph.

P4 — AOI and entities:
- Spatial index by WorldPos/chunk; exact radius cutoff, hysteresis, stable identity, lifecycle.
- A→B→A two-player tests; NPCs/players/mounts/items maintain identity.

P5 — Consolidate Unity modules:
- Split `VerticalSliceController` into bootstrap/session, streaming coordinator, world sync, UI/input composition. Keep it as composition root only.
- Split `CoreGameplay` into typed state/action services and separate UI panels. Remove JToken traversal from each panel.
- One map data model, one world origin, one chunk cache, one entity registry. Delete duplicated/legacy paths only after references/tests prove unused.

P6 — Content pipeline:
- Blender batch generation/import scripts, asset manifest, deterministic naming/LOD/collider/material rules.
- PC-generated meshes/textures are visual variants bound to server content IDs/palettes; server remains authoritative for topology/gameplay.
- Automated import validation and sample scene; no manual drag/drop hundreds of assets.

## 6. Required measurements and acceptance
- Build server + Go unit/integration tests; distinguish from HTTP/WS E2E.
- C# source CI is not Unity Editor compile/PlayMode.
- Unity PlayMode: spawn from at least 5 seeds; walk 2km in each cardinal/diagonal direction; no empty void, chunk gaps, missing collider/nav or teleport; reach at least one meaningful POI/settlement/road in each direction within configured max travel distance.
- Map: hidden region not shown as known; walk reveals path; POI discovered; rumor shows approximate marker; returning/reconnect preserves knowledge.
- Two clients across chunk and settlement boundary see same stable entities.
- Performance: chunk generation/cache budgets, memory bounds, AOI cost, entity LOD; benchmark 10/100/1000/10000 simulated population tiers.
- Assets: all prefabs have scale/material/collider/LOD profile; no pink materials, invalid normals, broken Cyrillic fonts.
- Report source evidence, test command, exact pass/fail, and which tests need local Unity run.

## 7. Open decisions requiring server audit, not guesswork
- Actual startup branch and save migration behavior for existing worlds.
- Is the generated world finite by radius/frame or effectively unbounded; how frame transitions work.
- Current road graph and navigation reachability guarantees.
- Whether chunk content can be generated statelessly by spatial coordinate or requires persisted region records.
- Current player map fields and knowledge transitions; exact schema for geometry/rumours.
- NPC/horse/item AOI and cross-boundary lifecycle.
- Scale benchmarks and chunk density.

