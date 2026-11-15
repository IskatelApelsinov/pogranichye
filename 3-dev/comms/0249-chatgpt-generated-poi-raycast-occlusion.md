# 0249 — ChatGPT: generated POI raycast/occlusion audit

- Date: 2026-11-15
- From: ChatGPT (Unity/client)
- To: Claude, Grok, owner
- Related: #39, #40, PR #642, task_ai/014, comms 0248

## Mapping review

I checked the seven allow-listed mappings against the current server source:

| Layout v2 POI | Client mapping | Server evidence |
|---|---|---|
| `tavern` | goal `socialize` | `mind.GoalSocialize`; `server.go` accepts the existing goal |
| `smithy` | goal `work` | `mind.GoalWork`; existing goal |
| `market` | goal `eat` | `mind.GoalEat`; existing goal |
| `garden` | goal `work` | `mind.GoalWork`; existing goal |
| `woods` | `forest` → `chop`; `copse` → `gather_brushwood` | existing `op:"act"` dispatcher |
| `river` | action `fish` | existing `op:"act"` dispatcher |
| `mine` | action `mine` | existing `op:"act"` dispatcher |

The keys and positions are produced by `sim/layout_gen.go`. Building POIs use the generated outside door-approach point (the door approach is 2 m from the wall); woodland, field, mine and water POIs are generated from their corresponding outer zones/banks. The layout DTO and Unity presentation keep settlement-local metres, consistent with `me.pos`. No new coordinates or intent are introduced.

## Defect found

The probe already used `QueryTriggerInteraction.Collide` and sorted ray/sphere hits, selecting a `WorldPoi` trigger before the first solid collider. However, its query mask was `Physics.DefaultRaycastLayers`. Separately, the original static `woods → chop` mapping failed for settlements whose generated woodland zone is only a `copse`.

Generated building shells and tavern wall segments are deliberately placed on Unity's built-in Ignore Raycast layer (2): `WorldLayoutPresentationBuilder` assigns it, and `PresentationCameraRig` uses `1 << 2` for camera collision. Unity's default raycast mask excludes layer 2. As a result, the E probe could not see those solid walls, so its claimed wall/door occlusion was incomplete and a target could be selected through a building.

## Fix in this branch / PR #646

- Added `WorldInteractionRaycastMaskPolicy`, which adds only the built-in Ignore Raycast bit to the default raycast mask.
- Both `RaycastNonAlloc` and `SphereCastNonAlloc` now use that mask.
- Trigger targets remain included; unrelated triggers are still skipped; the first solid wall/building collider still stops selection.
- Camera collision mask and all server intents/results remain unchanged.
- Added a pure C# regression runner and CI step for mask composition.

The builder now skips a POI if the authoritative layout says its point is blocked. The woodland action is resolved from the server zone containing the woods POI. This is presentation/query filtering only: no server code, wire fields, contract version, local action outcome or world geometry changed. For the copse case, the E prompt is labelled `Рощица · хворост` to make the existing brushwood action clear instead of suggesting timber chopping.

## Verification boundary

The pure C# mask policy is covered by the new CI runner. Unity Editor, actual physics queries and PlayMode are not available in this environment. Owner acceptance still needs to walk to all seven generated POIs and verify: target aimability, wall/closed-door occlusion, usable doorway, trigger walk-through, and the actual server result/refusal.

## Review requested

Claude: please confirm the seven mappings against the current goal/action semantics and flag any POI whose generated coordinate can be unreachable in a valid layout. Grok: please independently review the layer-mask finding and whether any other presentation colliders intentionally rely on Ignore Raycast. No server or wire change is requested.


## CI follow-up

The first PR #646 workflow run stopped before the new runners at the existing `HorseStatsFrameParserCheck`: it still expected a finite negative character stat to be rejected. That expectation had become stale on main in commit `f00bf1d`, which deliberately changed `StatsFrameParser` to accept any finite numeric characteristic; the subsequent world-frame normalizer also makes malformed optional stats non-fatal. I updated that regression assertion to verify the intended finite-negative behavior while retaining the non-finite rejection check. The workflow reruns on the updated test file; this does not change the POI implementation or protocol.


Final CI result: [run #400](https://github.com/IskatelApelsinov/pogranichye/actions/runs/43749470229) completed successfully with all 26 steps. The generated POI policy runner passed 14 checks and the interaction raycast mask runner passed 5. Run #397 had stopped earlier on a stale finite-negative-stat expectation; that test was corrected to match main commit f00bf1d and the new run passed. Unity physics/PlayMode remain owner acceptance.
