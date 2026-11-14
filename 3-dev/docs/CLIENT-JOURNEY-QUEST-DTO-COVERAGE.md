# Client DTO coverage — JourneyView and QuestsView

Date: 2026-11-14  
Branch/PR: `chatgpt/typed-journey-quests-20261114` (stacked on client PR #638)

## Authority and privacy boundary

The Go server owns journey progression, route/event decisions, quest offers, accepted quests, progress, readiness, rewards and reputation. Unity displays the latest full-frame projection and sends player choices/accept/turn-in/drop intentions. It never advances travel, completes quests, grants rewards or changes reputation locally.

Only the public view builders are modeled:
- `sim.JourneyView` — not the internal `JourneyC` component or its `Plan`.
- `sim.QuestsView` — not internal `QuestC` fields such as `Closed`, `Healed`, `Base`, numeric `Giver` or numeric `Settlement`.

This avoids exposing hidden route plans or server-only quest bookkeeping.

## Wire → DTO → client consumer

| Wire path | Public server shape | DTO | Client consumer |
|---|---|---|---|
| `me.journey` | `to:string`, `to_name:string`, `progress:number[0..1]`, `eta:number>=0`, `scene:string`, `note:string`, `follow:bool`, `event:object|null`, `via:string[]` | `JourneyFrame` | road overlay, journey-choice input |
| `me.journey.event` | `kind:string`, optional/empty `foe:string`, `text:string`, `options:string[]|null` | `RoadEventFrame` | event text and choice buttons |
| `me.quests.offers[]` | `id/kind/via/giver/giver_name/text:string`, optional `good/species:string`, `qty:int>=0`, `coins:number>=0` | `QuestFrame` | quest offer menu and accept intention |
| `me.quests.active[]` | same public quest fields plus `have:int`, `ready:bool`, `settlement_id/name/realm_name:string` | `QuestFrame` | active quest menu, tracker, turn-in/drop intentions |
| `me.quests.done` | non-negative int | `QuestsFrame.Done` | completed-quest count |
| `me.quests.rep` | signed int | `QuestsFrame.Reputation` | local settlement reputation summary |

## Full/fast semantics

These sections currently appear in the full state view, not in the inspected fast frame:
- Full snapshots replace `JourneyFrame` and `QuestsFrame`.
- Fast patches omit them and therefore do not clear or locally advance them.
- A missing/null journey clears the road overlay only when a new full snapshot says the character is no longer on a journey.
- Quest progress/readiness/reward values are copied from the server; UI buttons only send intentions.

## Validation

- Journey progress is finite and in [0,1]; ETA is finite and non-negative.
- Journey event and options are type-checked; `via` is a string array.
- Quest offers and active arrays are required when `me.quests` exists.
- Quest IDs/kind/via/text are non-empty strings; giver strings may be empty; good/species are optional strings.
- Quantity/done are non-negative integers; active have is a signed integer; ready is boolean; coins finite and non-negative; reputation is a signed integer.
- Active quest settlement snapshot fields are required strings, even when empty.
- Unknown additive fields are ignored.

## Compatibility

No server serializer, server logic, protocol version, intent, persistence, route generation, quest progression or reward logic changes. Both entry modes from ADR 0006 remain unchanged.
