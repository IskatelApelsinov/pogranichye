!В работе
# 007 — Typed stats, carried bag and horse views

**Owner:** ChatGPT  
**Scope:** Unity client / protocol v1 full-frame DTOs and presentation.  
**Server source audit:** `1-server/sim/stats.go`, `1-server/sim/horse.go`, `1-server/server/server.go` (`stateJSON` and `fastJSON`).  
**Coverage inventory:** `3-dev/docs/CLIENT-WIRE-DTO-COVERAGE.md`.

## Goal

Finish the existing typed DTO slice for `me.stats`, `me.bag` and `me.horse`, preserve backward compatibility with older additive v1 frames, and remove any remaining raw JObject access for these three sections from UI consumers.

## Actual server contract

- `me.stats` = `sim.StatsView(e)`: six finite non-negative numbers: `strength`, `agility`, `endurance`, `intellect`, `perception`, `will`.
- `me.bag` = `sim.BagView(e)`: `load`, `cap`, `over`. It summarizes the character's carried `econ.Stash`; it is not a physical backpack and is separate from the horse saddle bag.
- `me.horse` = `sim.HorseView(w,e)`: null if the player has no horse; otherwise stable string entity ID, name, stamina 0..100, mounted/here booleans, separate saddle-bag goods map, bag load and capacity.
- The three fields are emitted by current full `stateJSON`. They are not emitted by current `fastJSON`.
- Since protocol v1 is additive, absent/null stats and carried-bag summary are tolerated for older v1 servers. Horse null/absence means no horse. If a field is present with the wrong shape/type, reject the full frame.

## Implemented in this slice

- Reused existing `StatsFrame`, `CarryBagFrame`, `HorseFrame` and strict validators.
- Corrected the character summary to consume `_stats` rather than the stale raw JObject property access.
- Existing inventory and mount presentation consumers already use typed `_bag`/`_horse` fields populated from `frame.Me`.
- Added pure C# regression cases for the six stats, carried load summary, horse identity/stamina/mounted/location and saddle bag, null/absence compatibility, and malformed numeric/type cases.
- Added the runner to the Unity-independent C# CI workflow.

## Authority boundaries

The server remains authoritative for stat training, carried load, horse ownership, location, stamina, saddle-bag contents, stable stock, purchase, mounting, feeding and transfers. Unity only displays server-confirmed views and sends existing `horse` intentions. No local ownership, stamina, stock or inventory mutation was added.

The horse model is a presentation projection, not a locally spawned world entity. It must not be used to infer that the horse exists in the current settlement when `here=false`.

## Validation

- CI regression runner added; result pending for this commit.
- Unity Editor 6000.6.7f1 full compile, fixture menu and PlayMode remain unverified.
- Do not mark complete until CI is green and the owner performs the Unity runtime acceptance below.

## Next

Next DTO candidates: `me.region`, `me.card_game`, `me.recipes`, `me.party`; then settlement `place.stable`, `place.site`, `place.market`, `place.exits` and `place.tavern_counter`. Audit exact server views before mapping. Do not expose hidden card hands or private inventory.
