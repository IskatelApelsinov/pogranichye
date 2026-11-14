!В работе
# 008 — Typed regional map knowledge projection

**Owner:** ChatGPT  
**Scope:** Unity client / `me.region` full-frame DTO and map UI.  
**Server source:** `1-server/sim/roads.go:RegionView`, `1-server/server/server.go:stateJSON`.  
**Coverage:** `3-dev/docs/CLIENT-WIRE-DTO-COVERAGE.md`.

## Server contract

Current `RegionView` emits:
- `nodes[]`: string `id`, numeric `x/y`, `state` = `here|visited|known|unknown`, string `name/tier`.
- `unknown` nodes deliberately have empty `name/tier`; their coordinates and road frontier may be visible.
- `roads[]`: string endpoint IDs `a/b`, numeric non-negative `len`.
- Every road endpoint is in the visible node set. Non-empty node lists contain exactly one `here` node.
- If the current settlement cannot be resolved, server returns empty node and road arrays.
- Older additive v1 servers may omit the entire `me.region` field.

## Implemented

- Added `RegionFrame`, `RegionNodeFrame`, `RegionRoadFrame` and strict `RegionFrameParser`.
- `CharacterFrame.Region` and full-frame validation are wired.
- `CoreGameplay.DrawRegion/DrawRegionMap` now consume typed DTOs rather than raw JObject/JArray selectors.
- Privacy guard rejects any unknown node carrying a name or tier.
- Regression runner and Unity Editor fixture cases cover mapping, empty/absent compatibility, duplicate IDs, hidden-name leak, missing current node, invalid road endpoints, negative length and non-finite coordinates.

## Authority and privacy

Go remains authoritative for map knowledge and road graph. Unity only renders `me.region` and sends existing journey intentions. Never draw unknown settlement names, query the developer world manifest to fill missing map data, or locally add roads/settlements.

## Validation

- All 21 Unity-independent CI steps passed in run #43638994608, including the regional map runner: https://github.com/IskatelApelsinov/pogranichye/actions/runs/43638994608
- Unity Editor 6000.6.7f1 full compile, fixture menu and PlayMode remain unverified.

## Next

Continue with the inventory/equipment projection or public party view. Card-game DTOs require explicit hidden-hand privacy review before implementation.
