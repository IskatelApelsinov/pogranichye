!В работе
# 014 — ChatGPT: generated-layout E/Interact + readable text

**Owner:** ChatGPT (Unity)  
**Priority:** client follow-up after tavern counter slice  
**Spec:** `3-dev/docs/СЕЛО-И-КЛИЕНТ-UX.md`

## Goal

Make the existing classic E/Interact probe work against server-provided POIs in generated settlements, without inventing local gameplay.

## Implemented in PR #642

1. Confirmed `ClientInput` + `WorldInteractionProbe` + `WorldPoi/WorldNpc` already implement E, short-range ray/sphere targeting and focus prompt. A later source audit found that the probe mask excluded layer-2 building occluders; the follow-up fix is in PR #646.
2. Found generated layouts lacked `WorldPoi` targets because only tutorial `ProceduralVillageBuilder` created them and the prototype village is disabled for generated settlements.
3. Added `WorldLayoutPoiInteractionPolicy` explicit allow-list for seven existing server POIs and existing v1 goals/actions.
4. Added `WorldLayoutPoiTargetBuilder` presentation-only colliders at exact server Layout v2 coordinates; wired only for `layout.IsGenerated`.
5. Added pure C# allow-list regression tests and a dedicated Unity-independent CI step (all 23 steps passed in workflow run #43643297028).
6. Tavern status and E prompt/reason text wrapping plus shared typography tokens are included in the same PR.

## Authority and scope

- Unknown/unsupported POIs fail closed.
- No new intent, wire field, server-side action or local outcome.
- No tutorial-coordinate fallback for generated settlements.
- A player must aim and press E.
- Server response/reason remains authoritative.

## Source audit follow-up — 2026-11-15

- Verified the three goals in `mind/decide.go`, the four actions in `server/server.go`, and the generated keys/coordinate placement in `sim/layout_gen.go`.
- Found that generated building and tavern wall colliders use Unity layer 2 (Ignore Raycast), while the E probe used `Physics.DefaultRaycastLayers` and therefore excluded those walls from occlusion.
- PR #646 adds layer 2 to both interaction ray and sphere-cast masks without changing camera collision, trigger selection, server authority or wire v1.
- Found that generated `woods` may be a `copse`, not a real `forest`: `chop` would be rejected. The builder now maps `forest → chop` and `copse → gather_brushwood` from the zone containing the server POI; missing/ambiguous zone metadata fails closed.
- POI targets are skipped when the authoritative `WorldLayoutFrame.IsBlocked` says their point is blocked.
- Pure C# regression checks cover both woodland variants and the raycast mask.

## Remaining

- Claude/Grok independent review of the mapping and source audit.
- Unity Editor 6000.6.7f1 compile and PlayMode acceptance in a generated settlement.
- Walk to all seven POIs in both a forest and copse-only generated settlement; verify aimability, wall/closed-door occlusion, doorway access, trigger walk-through, and that woods yields wood in forest but brushwood in copse.
- Full IMGUI typography normalization remains separate; this is not claimed complete.

## Acceptance

Only mark complete after owner tests E at generated tavern/smithy/market/garden/woods/river/mine and attaches Console/F3 evidence. CI alone is not runtime acceptance.


CI evidence for PR #646: [run #400 passed all 26 Unity-independent steps](https://github.com/IskatelApelsinov/pogranichye/actions/runs/43749470229), including 14 POI mapping checks and 5 raycast-mask checks. Unity Editor/PlayMode acceptance remains outstanding.
