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
| `me.stats` | Missing `StatsFrame` | character attributes panel | **Gap** |
| `me.horse` | Missing `HorseFrame` and saddle-bag DTO | mount, stamina, location and horse inventory UI | **Gap** |
| `me.bag` | Missing `CarryBagFrame` | load/capacity/overload UI | **Gap** |
| `me.region` | Missing `RegionFrame`, node and road DTOs | regional map | **Gap** |
| `me.journey` | `JourneyFrame`, `RoadEventFrame` + `JourneyFrameParser` | road progress, scene, event choices and journey input | Typed in this slice; null/absent means not travelling |
| `me.quests` | `QuestListFrame`, `QuestOfferFrame`, `ActiveQuestFrame` + `QuestListFrameParser` | compact tracker and detailed offers/active panel | Typed DTO, validation and tracker/panel consumers migrated |
| `me.card_game` | Missing `CardGameFrame` and game-specific card DTOs | Ochko/Durak panel | **Gap**; preserve hidden-card privacy |
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
| `place.tavern_counter` | Missing `TavernCounterFrame` | counter/bartender affordance in controller | **Gap** |

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
- `me.journey` mirrors JourneyView: destination, progress, ETA, scene/note, follow flag, nullable encounter and route `via`; JourneyFrameParser validates it before DTO conversion. Fast frames omit journey, so the full-snapshot value is preserved between full frames.
- `me.quests` mirrors QuestsView: offers, accepted quests, done count and current-settlement reputation. Offer goods/species are optional due to Go omitempty; accepted-quest map always includes string values. Quest outcomes/progress remain server-owned.
- `place.foes[]` validates unique stable IDs, species/name/state, finite positions/health and optional telegraph/status fields; the same `FoeFrameParser` handles full and fast arrays. `CoreGameplay.SyncFoes` no longer reads raw JObject selectors.
- Fast feed distinguishes omitted (do not process) from explicit null (no entries); event deduplication remains by server sequence in `CoreGameplay`.
- This is presentation-only state. Server simulation, intents, movement authority, result handling and wire v1 are unchanged.

## Safe migration order

1. Keep the full-frame parser strict and keep the exact validated token document until each legacy consumer is migrated.
2. Add DTOs only after checking both server serializer and shared fixture; preserve absent/null/empty distinctions. `me.feed[]` is the first completed example.
3. Keep fast updates as a distinct patch type. Presence must be represented explicitly; never merge a missing field as zero/false.
4. Migrate one small consumer at a time. Journey rendering/input and the compact active-quest tracker now consume typed views. The detailed Requests panel now consumes typed offers/active quests; only unrelated full-state sections remain on JObject.
5. Preserve privacy-sensitive fields: especially card-game hidden cards, unknown NPC names, private room/chest state and non-public inventory.

## Position validation note

The parser already contains the seamless-scene boundary exception: `me.pos` may exceed the legacy ±150 m settlement footprint only when `me.wpos` and `place.anchor` are both present and the global/local relation agrees within 2 mm. Editor fixtures cover a valid x=151 m position and a mismatched global position rejection. Do not duplicate or weaken this guard; runtime behavior still requires Unity acceptance.

After the status, feed, result, combat, foe, journey and quest migrations, 7 character sections remain without dedicated DTOs (`stats`, `horse`, `bag`, `region`, `journey`, `quests`, `card_game`, `recipes`, `party`, plus other consumer-specific nested shapes as inventoried above); five settlement sections remain (`exits`, `site`, `market`, `stable`, `tavern_counter`).

No wire field, server behavior, protocol version, persistence format, settlement geometry or navigation rule is changed by this inventory.
