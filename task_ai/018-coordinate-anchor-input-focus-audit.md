# 018 — Canonical settlement-centre coordinates and world-object input audit

**Owner:** ChatGPT — Unity client / UX; Claude — server coordinate audit and contract review  
**Date:** 2026-11-16  
**Status:** Audit + design proposal; no coordinate migration authorized yet.

## Owner decisions

1. Do not remove mouse controls. Keep mouse-look, LMB/RMB combat, map/UI pointer, and other established mouse gestures.
2. The reported problem is that world objects sometimes block or capture controls so the player cannot do anything. Fix the conflict, not the mouse.
3. Use the geographic centre of every settlement as its single canonical geographic reference point, so villages, towns, cities, POIs, roads and all other content can be located consistently.

## Current coordinate findings (source-level)

- `1-server/sim/worldpos.go` already defines `Anchor(w, settlement)` as the geographic centre of a settlement in global frame 0, in metres.
- `WorldManifest` publishes settlement-centre `e/n/u`; `AnchorView` publishes the same centre as `place.anchor` / `/api/layout`.
- `WorldPosOf` already derives a placed entity's global position as settlement anchor + its local `ScenePosC` offset. During a journey it interpolates between settlement anchors.
- `WorldPos` is the canonical global position, millimetre precision, with frame/chunk/layer semantics.
- But the legacy gameplay coordinate `ScenePosC.X/Y` remains settlement-local, while `SettlementC.X/Y` are generator/map-space inputs transformed by `Anchor()`. Layout v2 geometry and POI positions are also settlement-local.
- Therefore the project already has the right global anchor concept, but several coordinate spaces coexist and are easy to confuse. Do not blindly rename or reinterpret fields, and do not remove local coordinates needed for collision/movement inside a settlement.

## Proposed canonical rule

For every settlement (village, town, city, camp if represented as a settlement):
- Its geographic centre is its stable global anchor `WorldPos` in the planet's canonical frame.
- Every settlement-local point (building, door, POI, NPC parking spot, road attachment, river sample, spawn point) is explicitly represented as an offset from that centre, with documented axes and units.
- Its global point is derived by one shared transform: `global = settlement_anchor + local_offset` (including height policy).
- Inter-settlement roads and regional/world maps use global anchor/global points, never raw `SettlementC.X/Y` or another generator-space coordinate.
- Local physics/collision and local layout generation may continue using settlement-relative metres; the field/type names and DTO docs must make that unambiguous.
- Use typed/well-named concepts where possible: `WorldPos` (global), `SettlementLocalPos` (local), `SettlementAnchor` (global centre). Avoid ambiguous `x/y` without a declared coordinate space.
- The centre is the geographic/layout reference point, not necessarily the town square, spawn point, origin of a mesh, or the player's location.
- Unity Floating Origin/rebasing is a presentation transform only; it must not alter authoritative global coordinates.

## Claude review requested

Please audit all writers/readers/persistence and consumers of:
- `SettlementC.X/Y`, `ScenePosC.X/Y/Z`, `WorldPos`, `place.anchor`, `WorldManifest.settlements[].e/n/u`;
- Layout v2 buildings/roads/water/POIs/spawns;
- regional/world map, road graph, journeys/exits, terrain/chunk streaming, biome and vegetation placement;
- save/load and generated settlement seeds.

Return:
1. A coordinate-space inventory table (field, space, units, origin, writer, readers).
2. Whether all current settlement centre coordinates really denote geographic centres, including generated settlements.
3. Any mismatches/legacy exceptions and risk to persisted worlds.
4. A backwards-compatible migration plan (prefer additive explicit fields/versioning; no silent reinterpretation).
5. Required tests for round trips, centre/local/global conversion, negative coordinates, chunk boundaries, adjacent settlements, journey continuity and Unity floating-origin rebase.

Do not change server code or protocol in this review. Propose ADR/update only after findings are reviewed.

## Unity input / world-object audit

Mouse remains part of gameplay. Investigate why world objects have been experienced as blocking controls:
- distinguish a world collider physically obstructing movement from a UI/input layer capturing movement/look/buttons;
- check EventSystem/UI raycasts, cursor lock/unlock, camera rig capture, action-map enable/disable and gameplayInputBlocked transitions;
- check whether `WorldInteractionProbe` focus/highlight or raycast target resolution can capture input beyond E;
- check if `WorldInteractionProbe` should stop processing interaction while `CoreGameplay.CapturesInput` or external modal/UI capture is active (current Update invokes E target requests whenever InteractPressed);
- inspect collider layers and generated world object prefabs for accidental full-body blocking or excessive trigger volumes;
- make sure aiming at/looking at an object only changes a contextual prompt/highlight; it must not disable movement, camera, combat or other inputs.
- preserve E as contextual interaction; no click-to-interact requirement and no mouse removal.
- do not globally make buildings/trees non-colliding; fix only accidental blockers and use intended physical collision.

Deliver a small, testable diagnosis and fix proposal before broad changes. No server authority changes.

## Acceptance criteria

- Every settlement has one explicit, stable global centre anchor.
- Local coordinates remain usable but are always clearly identified as settlement-local.
- A POI/object/NPC global position can be reproduced from settlement anchor + local offset.
- Map, terrain, vegetation, roads and streaming agree on the same anchor.
- Looking at or aiming at world objects does not seize gameplay input.
- Mouse-look, LMB/RMB combat and normal movement remain usable when no modal UI/action is active.
- Modal UI captures only while genuinely open; closing it restores the intended controls.
- Unity Editor/PlayMode validation is recorded separately from source-level checks.
