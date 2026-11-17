# Generated Layout v2 — E/Interact POI targets

Date: 2026-11-17  
Status: base E probe in PR #642/#646; contextual activity and horse integration in PR #697; Unity runtime acceptance pending.

## Finding

The first-person E probe already exists:
- `ClientInput` supplies InteractPressed.
- `WorldInteractionProbe` ray/sphere casts against short-range colliders, respects solid geometry occlusion and selects `WorldPoi` or `WorldNpc`.
- `WorldPoi` sends only an existing goal/action through `VerticalSliceController`; it never performs the action locally.

But generated settlements use `WorldLayoutPresentationBuilder`, while the actual `WorldPoi` marker objects were created only by `ProceduralVillageBuilder` for the tutorial village. The prototype village is disabled for generated layouts, so the generated layout had no corresponding POI hit targets. Context menu actions remained available, but the classic E prompt did not cover the generated server layout.

## Bounded implementation

Base implementation added:
- `WorldLayoutPoiInteractionPolicy`: explicit allow-list from existing Layout v2 POI IDs to already-supported server goals/actions.
- `WorldLayoutPoiTargetBuilder`: creates short-range invisible collider targets at exact server-provided settlement-local POI coordinates for both starter and generated layouts.
- `WorldLayoutPresentationBuilder` calls the target builder for the active server layout.
- Pure C# tests cover supported mappings and fail-closed behavior for unknown/null IDs.

2026-11-17 contextual follow-up:
- `granary` is now an E target and opens the stable/workshop panel; this was missing from the original allow-list, which made horse purchase/mount UX undiscoverable.
- `hearth` opens the server-reported recipe list for the hearth.
- `smithy` opens one contextual panel with `act:forge` plus recipes whose `me.recipes[].here` is true.
- `garden` offers the existing `gather`, `farmwork`, and `apiary` actions from one E entry point.
- The horse panel exposes `buy/mount/dismount/feed/stow/unstow`; cargo transfers include `good` and `qty` and are confirmed by the server.
- The client uses `me.recipes[].can/here` for affordance state and sends `op:"craft", item:<recipe-id>, reqid`; server validation remains final.

| Server POI | Existing intent |
|---|---|
| `tavern` | open tavern panel; no character-goal change |
| `smithy` | context menu: `act:forge` + server-available `craft` recipes |
| `market` | open market panel; no character-goal change |
| `garden` | context menu: `act:gather`, `act:farmwork`, `act:apiary` |
| `woods` | context menu: `forest` → `chop`; `copse` → `gather_brushwood`; additionally `hunt/fight` |
| `river` | action `fish` |
| `mine` | action `mine` |
| `granary` | horse/stable panel + recipes at `granary` |
| `hearth` | recipes at `hearth` |

No new wire field or server action is introduced. POIs absent from the server layout are not created. Unsupported locations are not made interactive. The target collider is presentation-only; E still flows through the existing selection handler and current server intent/RequestGate path. Server validation remains final.

The server's current `Layout.JSON()` does not publish `interactions[]`, and `server.go` does not dispatch `op:"interact"`. The client therefore never sends that unsupported operation. If a future layout includes descriptors, the client uses their server-authored coordinates/title/availability only when `target_id` maps to a known v1 goal/action; otherwise it fails closed. Legacy POI mapping remains the compatibility fallback.

## Explicit exclusions

- No invented interactions for well/house/door. Granary is now supported because `place.stable` and horse intents already exist on the server.
- No local resource, quest, inventory or world-state mutation.
- No new POI coordinates; no tutorial fallback for generated layouts.
- No automatic action on approach; player must aim and press E.
- This does not solve physical tavern door traversal or keeper movement to the counter.

## Validation

A dedicated pure C# regression runner is wired into Unity-independent CI. This does not compile Unity MonoBehaviours or prove a generated settlement works in PlayMode. Owner acceptance must walk to each visible POI, aim at its physical point and press E; confirm the server receives only the mapped existing intent and returns its real reason/result.


## CI result — 2026-11-15

All 23 Unity-independent workflow steps passed in run #43643297028, including the new generated-layout POI interaction allow-list runner. This validates the pure mapping policy only; Unity target placement, raycast occlusion and PlayMode remain unverified.


## Independent source audit — 2026-11-15

The original seven mappings were checked against current server code; the 2026-11-17 follow-up additionally verified the horse, forge, fieldwork, apiary and recipe operations:

- `mind/decide.go` defines the existing `eat`, `work` and `socialize` goals.
- `server/server.go` dispatches the existing `chop`, `gather_brushwood`, `fish` and `mine` actions.
- `sim/layout_gen.go` emits the seven matching keys. Building POIs are assigned to the generated outside door-approach point; `woods` is placed in its forest/copse zone, `garden` in its field/meadow zone, `mine` at its generated mine point, and `river` on the bank or at the generated pond. These coordinates are settlement-local, matching `WorldLayoutFrame` and `me.pos`.

### Occlusion defect found and fixed in PR #646

`WorldLayoutPresentationBuilder` deliberately places generated building shells and tavern wall segments on Unity's built-in Ignore Raycast layer (2). `PresentationCameraRig` uses that layer for camera collision, but `WorldInteractionProbe` used `Physics.DefaultRaycastLayers`, which excludes layer 2. Thus the probe's hit-ordering logic was correct, but it could not see those solid walls; E could select a target through a generated building or tavern wall.

PR #646 adds the Ignore Raycast bit to both the raycast and sphere-cast interaction mask. It retains `QueryTriggerInteraction.Collide`, so the presentation-only POI triggers remain selectable; `FindTargetBeforeObstacle` still skips unrelated triggers and stops at the first solid collider. The camera's layer-2-only collision mask is unchanged.

Pure C# regression checks verify both the zone-aware woodland action mapping and that the interaction mask preserves the default mask while adding only Unity's built-in Ignore Raycast bit. These are source-level regression checks, not a Unity physics simulation.

### Remaining owner acceptance

In Unity 6.7, walk to a generated settlement and test all nine targets. Confirm that each target is aimable from its approach, a solid wall/closed tavern door blocks E through the wall, the actual doorway remains usable, and the trigger volume does not obstruct movement. Also verify the server's real result/refusal for each mapped intent. No wire or server change is proposed.


### Zone-aware woodland intent

A second mapping issue was found in the server generator/action pair. `sim/layout_gen.go` always emits the `woods` POI, but the matching zone is either `forest` (when the settlement has the `woods` feature) or a small `copse` (when it does not). The server explicitly separates these actions: `ChopWood` requires the real `woods` feature, while `GatherBrushwood` requires the player to be inside a `copse`.

The original static mapping sent `chop` for both layouts, so a generated settlement with only a copse exposed an E target that the server correctly rejected. PR #646 now resolves the zone containing the server's `woods` POI: `forest → chop`, `copse → gather_brushwood`; missing or ambiguous zone metadata fails closed. The target builder also skips any POI for which the authoritative `WorldLayoutFrame.IsBlocked(x,y)` reports a blocked point.

The regression runner now covers both woodland cases and the fail-closed missing/unknown-zone cases. No new action or wire field is added; `gather_brushwood` is already handled by the v1 `op:"act"` dispatcher. The E prompt title for a copse is explicitly `Рощица · хворост`, so the visible small grove does not advertise timber chopping.


## PR #646 CI follow-up — 2026-11-15

- [Unity-independent C# checks run #400](https://github.com/IskatelApelsinov/pogranichye/actions/runs/43749470229): **success**, all 26 steps completed.
- Generated POI action policy: **14 checks passed** (forest/copse split and fail-closed cases included).
- Interaction raycast mask policy: **5 checks passed**.
- The first run (#397) stopped at a stale assertion that expected finite negative character stats to be rejected. That expectation contradicted main commit f00bf1d; the test now verifies the intended finite-negative compatibility while retaining non-finite rejection.
- This validates pure C# policies only. Unity Editor, physics raycasts and PlayMode remain unverified.


### Character goals are not POI interactions

The character UI sends `op:"goal"` for long-lived priorities such as work, food, rest and socializing. Current `server.applyIntent` stores the goal and sets `GoalUntil` far into the future; the v1 handler does not return a correlated `reqid` acknowledgement. Therefore the client reports only that the request was sent, not that it was confirmed. E at a tavern or market opens the corresponding panel and does not mutate this priority.
