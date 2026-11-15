# Generated Layout v2 — E/Interact POI targets

Date: 2026-11-15  
Status: source implementation added to stacked client PR #642; Unity runtime acceptance pending.

## Finding

The first-person E probe already exists:
- `ClientInput` supplies InteractPressed.
- `WorldInteractionProbe` ray/sphere casts against short-range colliders, respects solid geometry occlusion and selects `WorldPoi` or `WorldNpc`.
- `WorldPoi` sends only an existing goal/action through `VerticalSliceController`; it never performs the action locally.

But generated settlements use `WorldLayoutPresentationBuilder`, while the actual `WorldPoi` marker objects were created only by `ProceduralVillageBuilder` for the tutorial village. The prototype village is disabled for generated layouts, so the generated layout had no corresponding POI hit targets. Context menu actions remained available, but the classic E prompt did not cover the generated server layout.

## Bounded implementation

Added:
- `WorldLayoutPoiInteractionPolicy`: explicit allow-list from existing Layout v2 POI IDs to already-supported server goals/actions.
- `WorldLayoutPoiTargetBuilder`: creates short-range invisible collider targets at exact server-provided settlement-local POI coordinates for generated layouts only.
- `WorldLayoutPresentationBuilder` calls the target builder only when `layout.IsGenerated`.
- Pure C# tests cover all seven mappings and fail-closed behavior for unsupported `stable`, `granary`, `apiary`, unknown and null IDs.

| Server POI | Existing intent |
|---|---|
| `tavern` | goal `socialize` |
| `smithy` | goal `work` |
| `market` | goal `eat` |
| `garden` | goal `work` |
| `woods` | action `chop` |
| `river` | action `fish` |
| `mine` | action `mine` |

No new wire field or server action is introduced. POIs absent from the server layout are not created. Unsupported locations are not made interactive. The target collider is presentation-only; E still flows through the existing selection handler and current server intent/RequestGate path. Server validation remains final.

## Explicit exclusions

- No invented interactions for stable/granary/well/house/door.
- No local resource, quest, inventory or world-state mutation.
- No new POI coordinates; no tutorial fallback for generated layouts.
- No automatic action on approach; player must aim and press E.
- This does not solve physical tavern door traversal or keeper movement to the counter.

## Validation

A dedicated pure C# regression runner is wired into Unity-independent CI. This does not compile Unity MonoBehaviours or prove a generated settlement works in PlayMode. Owner acceptance must walk to each visible POI, aim at its physical point and press E; confirm the server receives only the mapped existing intent and returns its real reason/result.


## CI result — 2026-11-15

All 23 Unity-independent workflow steps passed in run #43643297028, including the new generated-layout POI interaction allow-list runner. This validates the pure mapping policy only; Unity target placement, raycast occlusion and PlayMode remain unverified.
