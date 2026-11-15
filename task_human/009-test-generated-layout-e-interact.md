!В работе
# 009 — Unity acceptance: generated-layout E/Interact targets

**Owner:** Kate  
**When:** after PR #641/#642 are merged and Unity is updated.

## Test

1. Enter a generated settlement (not the tutorial starter layout).
2. Confirm the prototype/tutorial village is not visible and the generated server layout is active.
3. Aim at the server-provided tavern, smithy, market, garden, woods, river and mine POI positions.
4. Confirm the centre reticle shows the matching title and [E] prompt only within short range and without aiming through solid walls.
5. Walk directly through each invisible POI target volume: it must not block or push the player. Confirm E still selects the target because POI volumes are triggers.
6. Confirm unrelated trigger volumes do not hide targets, while solid building walls and closed doors still occlude them.
7. Press E once at each supported target.
8. Confirm only the existing mapped goal/action is sent and the server's real response/reason is displayed.
9. Confirm unsupported locations (stable, granary, well, unknown) do not acquire an E target.
10. Repeat after moving to a second generated settlement; no stale coordinates from the previous layout may remain.

## Evidence

Attach Unity Console log and F3 dump, plus a short list of each POI tested and its server result. Do not mark complete based on pure C# CI alone; target occlusion and generated layout coordinates require PlayMode.
