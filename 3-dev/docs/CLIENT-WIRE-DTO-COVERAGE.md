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
| `me.talk/invite_from/social_event/result/task/journal` | corresponding nested DTOs | talk, request result, opening task and journal | Typed |
| `me.coins/debt/wounds/bleeding/stash/pos/wpos/room` | `CharacterFrame` plus nested DTOs | status, movement, containers and room presentation | Typed |
| `me.drunk` (v1.12) | `CharacterFrame.Drunk` | compact status HUD and character summary | Typed in status-view slice |
| `me.feed[]` | `FeedEntryFrame` + `FeedFrameParser` | `ReadFeed`, confirmed combat/work/social feedback | Typed in feed slice; strict sequence and entry validation |
| `me.stats` | Missing `StatsFrame` | character attributes panel | **Gap** |
| `me.horse` | Missing `HorseFrame` and saddle-bag DTO | mount, stamina, location and horse inventory UI | **Gap** |
| `me.bag` | Missing `CarryBagFrame` | load/capacity/overload UI | **Gap** |
| `me.region` | Missing `RegionFrame`, node and road DTOs | regional map | **Gap** |
| `me.journey` | Missing `JourneyFrame`, event/options DTOs | road progress, scene and event choices | **Gap** |
| `me.quests` | Missing `QuestsFrame`, offer/active quest DTOs | needs/requests UI | **Gap** |
| `me.card_game` | Missing `CardGameFrame` and game-specific card DTOs | Ochko/Durak panel | **Gap**; preserve hidden-card privacy |
| `me.combat` | Missing `CombatFrame` and weapon submodel | stance, weapon, guard, arrows and combat HUD | **Gap** |
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
| `place.foes[]` | Missing `FoeFrame` | `SyncFoes`, combat targets and presentation | **Gap**; full and fast frames |
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
- Fast feed distinguishes omitted (do not process) from explicit null (no entries); event deduplication remains by server sequence in `CoreGameplay`.
- This is presentation-only state. Server simulation, intents, movement authority, result handling and wire v1 are unchanged.

## Safe migration order

1. Keep the full-frame parser strict and keep the exact validated token document until each legacy consumer is migrated.
2. Add DTOs only after checking both server serializer and shared fixture; preserve absent/null/empty distinctions. `me.feed[]` is the first completed example.
3. Keep fast updates as a distinct patch type. Presence must be represented explicitly; never merge a missing field as zero/false.
4. Migrate one small consumer at a time. Next candidates: `me.result`/RequestGate correlation, then combat, journey/quests, inventory/horse, and settlement market/site/foes/exits.
5. Preserve privacy-sensitive fields: especially card-game hidden cards, unknown NPC names, private room/chest state and non-public inventory.

## Position validation note

The parser already contains the seamless-scene boundary exception: `me.pos` may exceed the legacy ±150 m settlement footprint only when `me.wpos` and `place.anchor` are both present and the global/local relation agrees within 2 mm. Editor fixtures cover a valid x=151 m position and a mismatched global position rejection. Do not duplicate or weaken this guard; runtime behavior still requires Unity acceptance.

After the status view and feed migrations, 11 character sections remain without dedicated DTOs (`stats`, `horse`, `bag`, `region`, `journey`, `quests`, `card_game`, `combat`, `recipes`, `party`, plus other consumer-specific nested shapes as inventoried above); six settlement sections remain (`foes`, `exits`, `site`, `market`, `stable`, `tavern_counter`).

No wire field, server behavior, protocol version, persistence format, settlement geometry or navigation rule is changed by this inventory.
