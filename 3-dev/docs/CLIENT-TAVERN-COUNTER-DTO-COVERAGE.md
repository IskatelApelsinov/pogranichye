# Client DTO coverage — tavern counter and beer interaction

Date: 2026-11-14  
Status: client DTO and contextual UI slice implemented in stacked PR #642; Unity runtime remains unverified.

## Existing server contract (no new wire fields)

Server already emits additive `place.tavern_counter` from `sim.TavernCounterView`:

```json
{
  "x": -33.0,
  "y": 20.0,
  "radius": 2.2,
  "bartender_present": true
}
```

- `x/y/radius` are server-computed settlement-local coordinates, in the same coordinate space as `me.pos`.
- `bartender_present` currently means the settlement's live keeper entity has `act_code == "keep_tavern"`; Grok found this does **not yet prove physical proximity to the counter** because the keeper AI target is still the exterior tavern door. The client label therefore says “на смене”, not “стоит за стойкой”. Claude task #013 asks to align keeper target and actual presence semantics.
- The field may be null/absent if no tavern geometry is available or when reading an older additive v1 frame.
- The client never sends the counter coordinates to the server. `BuyBeer` independently revalidates position, keeper, stock, money and reqid.

Server implementation references: `1-server/sim/tavern.go`, `1-server/sim/layout.go`, `1-server/server/server.go`; cross-system explanation: `3-dev/comms/0224-claude-to-chatgpt-tavern-counter-status-and-0222-plan.md`.

## Client changes

- Added `TavernCounterFrame` and strict `TavernCounterFrameParser`.
- `PlaceFrame.TavernCounter` maps `place.tavern_counter`; `WorldFrameParser` validates it.
- Parser accepts absent/null for backward-compatible v1 frames, but rejects malformed objects, non-finite coordinates, invalid radius and non-boolean keeper state.
- `WorldLayoutPresentationBuilder` aligns the generated visual bar with the server counter centre (visual bar centre offset 1.0m and frontage width capped at 3.6m, keeping the full player-facing bar frontage within the 2.2m server radius); the starter visual bar is already offset 1.25m, also within range. The menu explains missing counter data, keeper absence, empty stock or insufficient coins. It no longer hard-blocks the buy request solely from the client's local distance projection: it sends the existing request and lets the server make the authoritative counter-radius decision, avoiding false negatives from slightly divergent Unity/server coordinates.
- The older `CoreGameplay` panel now uses the same typed counter state instead of presenting a buy button merely because the player is near the exterior tavern POI.
- Co-drink invite UI is shown only in the tavern vicinity and only for neighbors the server marks as controlled players. The invite/accept/decline requests still use the shared RequestGate and server reqid/result; server decides final eligibility and outcomes.
- Drinking a confirmed mug sends the existing `consume` intent from either the tavern panel or the Inventory tab. Client does not clear the mug or apply morale/drunk locally; the next server snapshot is authoritative.

## Server status update — 2026-11-16 (Claude, tasks 012/013)

- **Task 013 (keeper target)**: `targetFor` for `role=="tavernkeeper" && act=="keep_tavern"` now returns `Layout.TavernCounter()` (falls back to the old exterior door POI only if a layout has no counter geometry). The keeper AI physically walks to the counter, not the door. Test: `sim.TestTavernkeeperWalksToCounterNotDoor` (materialized body via `StepNPCMovement`, no teleport) — asserts the resting `ScenePos` is `insideTavernBuilding` and within ~8m of the counter centre.
- **Task 012 (serving hours)**: `bartender_present` (and `BuyBeer`'s server-side gate) now means **"on shift"**, exactly as the client label already says — not "physically at the counter this instant". Precisely: `bartender_present == true` when the settlement's keeper is alive, has role `tavernkeeper`, **and** (`act_code == "keep_tavern"` **or** the world hour is in `[10, 22)`). Outside that daytime window, `act_code` alone decides (still refuses rest/eat/sleep ticks at night, honest schedule). This is additive/behavioral only — no wire shape change; `BuyBeer` and `TavernCounterView` both gained an explicit `hour int` parameter fed from `server.runner.Clock.Hour()`. Tests: `sim.TestBuyBeerDayVsNight`, `sim.TestBuyBeerKeeperBusy` (now asserts the night-refusal case specifically), `sim.TestCoDrinkDoesNotNeedKeepTavern` (confirms co-drink's looser `atTavern` path is intentionally untouched — the day/night window only gates `BuyBeer`/the counter label, per comms 0242's recommendation **not** to tighten co-drink to match).
- Both changes are server-only; `go build/vet/test ./... -count=1` green (`1-server`), commit/PR referenced in `3-dev/comms/`.

## Important contract-document mismatch to reconcile

The current `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md` §3.2 still describes `buy` as a proximity check to the old exterior `tavern` POI (≤2.5m). The later merged server implementation described by Claude in comms 0224 instead requires the geometric `TavernCounter()` radius (2.2m) and a keeper whose current `act_code` is `keep_tavern`. Grok's comms 0244 further found the keeper AI still targets the outer door, so `bartender_present` may be true while the physical counter is empty; see Claude task #013. This client PR does not change server behavior or silently rewrite the contract; comms 0247 asks Claude to confirm and align the canonical contract/fixture note.

Co-drink intentionally remains on the server's broader `atTavern` check (within the 2.5m exterior POI reach or inside the tavern building), not the stricter beer-purchase counter check. The client mirrors that UI affordance using the server POI and `WorldLayoutFrame.Buildings`; the server still decides the action. Do not conflate the two.

## Validation

- Added `TavernCounterFrameParserCheck.cs` for valid mapping, absent/null compatibility, malformed type, missing field, invalid radius, wrong keeper type and non-finite coordinates.
- Unity-independent CI runs this parser test.
- No Unity Editor compile, PlayMode, physical door traversal, actual server purchase or two-Unity-client co-drink is claimed as tested. The buy UI and inventory consume action were extended in the 2026-11-18 Unity integration PR; runtime acceptance is still required.


## CI result

The parser and related pure C# checks passed in runs #43641435028, #43641448658 and #43641455630 (24 workflow steps each). This does not prove Unity compilation, door traversal, keeper movement or a two-client co-drink session.


## Latest CI — 2026-11-14

All 24 workflow steps passed in run #43641807521, including the dedicated tavern counter parser regression runner. This run followed the F3 diagnostic context addition. The CI does not compile the full Unity project or verify runtime movement, keeper arrival or co-drink.


## Interaction and readability overlap

The E/Interact action was already present (ClientInput + WorldInteractionProbe + WorldPoi/WorldNpc); this PR does not create a duplicate input system. It makes the interaction prompt wrap and adds shared initial typography tokens. Server result/status labels now wrap the full reason. Generated-layout POIs are not yet generally wired into WorldPoi; that remains in ChatGPT task 014, with only server-supported actions allowed.


## Latest CI — 2026-11-14

All 24 workflow steps passed in run #43642750564, including tavern parser regression checks and the existing client pure-C# regressions. This does not compile Unity MonoBehaviours or validate PlayMode.

A follow-up generated-layout E/Interact allow-list and target-builder slice is now added to PR #642. Its new dedicated CI runner is pending; see `CLIENT-GENERATED-POI-INTERACTION.md` and comms 0248.
