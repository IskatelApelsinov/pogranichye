# Unity client: coverage of wire v1 DTOs and gameplay consumers

- Audit date: 2026-11-14
- Audited main: `a62c0e72c768a07802f7b85483124f15842a4b37`
- Sources: `1-server/server/server.go` (`stateJSON`, `fastJSON`), `2-client/game/Assets/_Project/Scripts/Networking/Protocol/WorldFrame.cs`, `WorldFrameParser.cs`, `FastFrameParser.cs`, `Runtime/CoreGameplay.cs`, and `3-dev/contract/fixtures/state_v1.example.json`.
- Contract authority: `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md`. This is an implementation inventory, not a wire-contract proposal.

## Full snapshot envelope

| Wire path | Current DTO | Current consumer | Semantics / gap |
|---|---|---|---|
| `v`, `alive`, `msg`, `day`, `year`, `season`, `hour` | `WorldFrame` | Controller; `CoreGameplay` uses `hour` for environment | Typed. `hour` is nullable/additive for older v1 snapshots. |
| `me` | `CharacterFrame` | Entity registry, controller, `CoreGameplay` | DTO covers only a subset of the server object. Unknown JSON fields remain in the validated token document for legacy selectors. |
| `place` | `PlaceFrame` | Controller, `CoreGameplay` | DTO covers only a subset of the server object. `place` may be absent/null outside a settlement or while travelling. |

## Character fields

| Wire path | DTO coverage | Actual `CoreGameplay` use | Status |
|---|---|---|---|
| `me.id/name/surname/nick/trait/origin/role/sex/age` | `CharacterFrame` | identity, UI and session-facing labels | Typed |
| `me.skills/desire/inv/world/known_places` | `CharacterFrame` plus nested DTOs | inventory, world list and knowledge UI | Typed |
| `me.health/hunger/energy/morale/mana/goal/act_code/mug` | `CharacterFrame` | status and activity presentation | Typed |
| `me.weave` | `WeaveFrame`, `WeaveNodeFrame`, `WeaveSchemaFrame` | weave UI and input | Typed |
| `me.talk/invite_from/social_event/task/journal` | corresponding nested DTOs | talk, opening task and journal | Typed |
| `me.result` | `ActionResultFrame` + `ActionResultFrameParser` | `ReadResult` / `RequestGate` | Typed in this slice; single latest server ACK slot, correlated by reqid |
| `me.coins/debt/wounds/bleeding/stash/pos/wpos/room` | `CharacterFrame` plus nested DTOs | status, movement, containers and room presentation | Typed |
| `me.drunk` (v1.12) | `CharacterFrame.Drunk` | compact status HUD and character summary | Typed in status-view slice |
| `me.feed[]` | `FeedEntryFrame` + `FeedFrameParser` | `ReadFeed`, confirmed combat/work/social feedback | Typed in feed slice; strict sequence and entry validation |
| `me.stats` | `StatsFrame` + `StatsFrameParser` | character attributes panel | Typed; six server stats validated, additive null/absence tolerated |
| `me.horse` | `HorseFrame` + `HorseFrameParser` | mount presentation, stamina, location and saddle-bag UI | Typed; server horse entity ID remains string; null means no owned horse |
| `me.bag` | `CarryBagFrame` + `CarryBagFrameParser` | carried-bag load/capacity/overload UI | Typed; additive absence/null tolerated for older v1 servers |
| `me.region` | `RegionFrame`, `RegionNodeFrame`, `RegionRoadFrame` + `RegionFrameParser` | regional map and road choices | Typed; privacy-safe states and edge references validated |
| `me.journey` | `JourneyFrame`, `RoadEventFrame` + `JourneyFrameParser` | road progress, scene and event choices | Typed; public `sim.JourneyView` only, full-frame-only |
| `me.quests` | `QuestsFrame`, `QuestFrame` + `QuestsFrameParser` | needs/requests UI | Typed; public `sim.QuestsView` only, full-frame-only |
| `me.card_game` | `CardGameFrame`, `CardGameCardFrame`, `DurakTablePairFrame` + `CardGameFrameParser` | Ochko/Durak panel and hotkeys | Typed in the card-game slice; validates public state and rejects hidden dealer-hand leaks; full-frame-only |
| `me.combat` | `CombatFrame` + `CombatFrameParser` | stance, weapon, guard, shield, light, arrows and combat HUD | Typed in this slice; exact server `CombatView` shape including `kills` map and integer `ko` counter validated |
| `me.recipes` | Missing recipe/needs DTOs | crafting panel | **Gap** |
| `me.party` | Missing `PartyFrame` and member/invite DTOs | party panel | **Gap** |

## Settlement fields

| Wire path | DTO coverage | Actual `CoreGameplay` use | Status |
|---|---|---|---|
| `place.id/plan/name/tier/food/bread/bread_price/tavern/beer/beer_price` | `PlaceFrame` | POI context, market/tavern and labels | Typed |
| `place.neighbors[]` | `NeighborFrame`, `NeighborGearFrame` | resident identity, gear and presence | Typed; stable string IDs and optional WPOS must remain intact |
| `place.life` | `SettlementLifeFrame` and nested activity/stock DTOs | settlement activity and stock summary | Typed |
| `place.anchor` | `WorldAnchorFrame` | world/local projection | Typed |
| `place.rooms` | `PlaceRoomsFrame`, `PlaceRoomEntryFrame` | public room vacancy summary | Typed |
| `place.foes[]` | `FoeFrame` + `FoeFrameParser` | `SyncFoes`, combat targets and presentation | Typed in this slice; stable string IDs and full/fast shape validated |
| `place.exits[]` | Missing `ExitFrame` | `SyncExits`, road choices | **Gap** in full-frame DTO; current server fast frame does not send exits |
| `place.site` | Missing `SiteFrame` | features, terrain, climate, specialty and state | **Gap** |
| `place.market[]` | Missing `MarketItemFrame` | stock, buy/sell prices and ownership UI | **Gap** |
| `place.stable` | Missing `StableFrame` and horse-offer DTO | horse availability and price | **Gap** |
| `place.tavern_counter` | `TavernCounterFrame` + `TavernCounterFrameParser` | beer purchase affordance and keeper status | Typed; additive nullable v1 view, client-only hint, server revalidates |

## Fast frame: exact current server shape

The current `Server.fastJSON` implementation emits:

- Terminal envelope: `{v:1, fast:true, alive:false}` when the controlled entity no longer exists. It must continue through the normal full-frame lifecycle.
- Live envelope: `v`, `fast:true`, `alive:true`, `me`.
- Live `me` fields: `pos`, `mana`, `combat`, `feed`, `result`, `wounds`; `health` and `energy` are included when vitals exist.
- `place` is included only when the player is in a settlement and not on a road. Its current fields are `neighbors` and `foes`. Fast neighbors intentionally contain only `id` and `pos`; `me.id` is intentionally absent.
- `me.combat` uses the same `CombatView` object in full and fast frames. `place.foes` uses the same `FoesView` array in full and fast frames; foe IDs are stable string entity IDs.
- `me.result` is present as object or null in the inspected server fast shape; it remains a single latest-result slot, not a queue.
- The parser currently accepts optional `place.exits` for additive compatibility, but the inspected server `fastJSON` does not emit it.

`FastFrameParser` validates the envelope and selected container/ID types, then preserves the partial `me/place` objects. It is not a typed full snapshot. `CoreGameplay.ApplyFastFrame` shallow-merges only properties present in the patch; absent full-frame properties must never be reset to DTO defaults.

## Typed consumers migrated in the current slice

The compact `DrawStatus` HUD now reads an immutable `GameplayStatusView`, and the server-confirmed feed is mapped to typed entries:

- Full snapshot source: `me.health`, `me.energy`, `me.mana`, `me.drunk`, `me.pos.stam`.
- Fast patch source: `me.health`, `me.energy`, `me.mana`, and `me.pos.stam`.
- A missing fast property preserves the previous value.
- An explicit JSON null is treated as zero, matching the previous `Num(JToken, key)` selector after shallow merge.
- `me.drunk` is not in the fast server shape and therefore remains from the last full snapshot.
- `me.feed[]` validates positive strictly increasing `seq`, non-empty `kind`, string `text`, optional string `foe/part`, and finite non-negative `dmg`; the same parser is used by full and fast frames.
- `me.result` is validated as `{reqid:string, ok:boolean, reason:string}` or null. Full snapshots map it through `CharacterFrame.Result`; fast patches expose `HasResult/Result` so omission and explicit null remain distinct. `RequestGate` still accepts only the matching in-flight reqid.
- `me.combat` is validated against the server's complete `CombatView` shape; full DTO and fast patch expose `CombatFrame`.
- `place.foes[]` validates unique stable IDs, species/name/state, finite positions/health and optional telegraph/status fields; the same `FoeFrameParser` handles full and fast arrays. `CoreGameplay.SyncFoes` no longer reads raw JObject selectors.
- Fast feed distinguishes omitted (do not process) from explicit null (no entries); event deduplication remains by server sequence in `CoreGameplay`.
- This is presentation-only state. Server simulation, intents, movement authority, result handling and wire v1 are unchanged.

## Safe migration order

1. Keep the full-frame parser strict and keep the exact validated token document until each legacy consumer is migrated.
2. Add DTOs only after checking both server serializer and shared fixture; preserve absent/null/empty distinctions. `me.feed[]` is the first completed example.
3. Keep fast updates as a distinct patch type. Presence must be represented explicitly; never merge a missing field as zero/false.
4. Migrate one small consumer at a time. Stats, carried bag, horse and regional map now have typed DTOs and typed consumers; next candidates: inventory/card-game/recipes/party, then settlement market/site/exits/stable/tavern counter.
5. Preserve privacy-sensitive fields: especially card-game hidden cards, unknown NPC names, private room/chest state and non-public inventory.

## Position validation note

The parser already contains the seamless-scene boundary exception: `me.pos` may exceed the legacy ±150 m settlement footprint only when `me.wpos` and `place.anchor` are both present and the global/local relation agrees within 2 mm. Editor fixtures cover a valid x=151 m position and a mismatched global position rejection. Do not duplicate or weaken this guard; runtime behavior still requires Unity acceptance.

After the status, feed, result, combat, foe, journey, quest, stats, horse and carried-bag migrations, 3 major character sections remain without dedicated DTOs (`card_game`, `recipes`, `party`, plus other consumer-specific nested shapes as inventoried above); five settlement sections remain (`exits`, `site`, `market`, `stable`, `tavern_counter`).

No wire field, server behavior, protocol version, persistence format, settlement geometry or navigation rule is changed by this inventory.


## Stats, carried bag and horse (server v1.12–v1.13)

- `me.stats` comes from `sim.StatsView(e)`: six keys `strength/agility/endurance/intellect/perception/will`, all finite non-negative numbers. The server view always emits all six keys; client tolerates absent/null for additive compatibility with older v1 servers.
- `me.bag` comes from `sim.BagView(e)`: `load`, `cap`, `over`. This is the character's carried `econ.Stash` load summary, not a physical backpack and not the horse's saddle bag. Server emits the object on current versions; client tolerates absent/null on older v1 servers.
- `me.horse` comes from `sim.HorseView(w,e)`: null if no owned horse; otherwise `id` (stable string entity ID), `name`, `stam` (0..100), `mounted`, `here`, `bag` (separate saddle-bag goods map), `bag_load`, `bag_cap`. All fields are emitted by the current server view.
- The typed fields are assigned from each validated full snapshot in `CoreGameplay.ApplyFrame`. They are absent from the inspected fast frame, so fast patches preserve the latest full values and do not synthesize a horse or bag state.
- The inventory UI uses the typed stats, carried-bag summary and horse DTO; mount visuals use only the server-confirmed `HorseFrame.Mounted` flag. Buy/mount/dismount/feed/stow/unstow remain server intentions; no local stock, stamina, ownership or inventory mutation is performed.
- `place.stable` remains a separate untyped settlement view in this slice; its stock/price UI is still read from the validated full-frame token tree.


## Regional map knowledge projection (v1.12)

- `me.region` comes from `sim.RegionView(w,e)`; current server returns `nodes[]` and `roads[]`, or two empty arrays when no settlement anchor is available.
- Each node contains string `id`, numeric `x/y`, state in `here|visited|known|unknown`, and string `name/tier`. For `unknown`, server intentionally sends empty `name/tier` while retaining coordinates and the road frontier.
- Each road contains string endpoint IDs `a/b` and numeric `len`. Both endpoints must be among the visible region nodes.
- Client validator checks unique IDs, exactly one `here` node when the node list is non-empty, known state values, no unknown-place name/tier leak, and non-duplicate edges with valid endpoints.
- Missing/null `me.region` is tolerated for older additive v1 snapshots. A present object must contain both arrays.
- `CoreGameplay.DrawRegion/DrawRegionMap` now consumes `RegionFrame`; map labels and route choices remain based solely on server-supplied knowledge. No developer manifest is introduced into this map.


## Card-game DTO consumer update — 2026-11-15

The card-game slice is now typed end-to-end for its selected consumer: `WorldFrameParser` validates the public card-game view, and `CoreGameplay` stores/reads `CardGameFrame` for the game panel and hotkeys. The raw JObject adapter remains for other un-migrated gameplay groups. See task 003 and the card-game parser regression suite. No fast-frame patch carries `card_game`; the last full-frame state is preserved until the next full snapshot.


Card-game parser regression run: [#43755452041](https://github.com/IskatelApelsinov/pogranichye/actions/runs/43755452041) passed. Unity runtime acceptance is still pending.

## Market and progression completeness audit — 2026-11-16

Server source review:
- `1-server/server/server.go:stateJSON` publishes `place.market = sim.MarketView(se,e)` and `me.stats = sim.StatsView(e)`.
- `1-server/sim/craft.go:MarketView` builds one row for every distinct good with settlement stock >= 1 and every distinct good in the player's stash >= 1, excluding beer (beer is intentionally handled by the physical tavern counter). Each row contains `good`, `stock`, `price`, `sell`; rows are sorted by good key. The server does not impose a six-row cap.
- `1-server/sim/stats.go:StatsView` currently emits exactly six core attributes: `strength/agility/endurance/intellect/perception/will`. This is the complete current `me.stats` contract, not an arbitrary client subset.
- `me.skills` is a separate dynamic map keyed by server skill IDs. It must be rendered dynamically; do not confuse skills with the six core attributes.

Unity presentation correction in progress:
- Market UI now iterates the complete received `place.market[]` list inside a scroll view instead of relying on a short visible-row limit.
- Each market row uses server `good/stock/price/sell` and current server stash quantity; beer remains in the tavern flow.
- Character sheet retains the six server core attributes and adds every numeric `me.skills` entry in sorted key order, including additive/previously unknown skill IDs.
- No Go server or wire contract changes are part of this client task.

Runtime status: source review only; Unity Editor compile and PlayMode acceptance remain pending.
