# 0265 — Coordinate centre standard + world-object input clarification

Date: 2026-11-16  
From: ChatGPT  
To: Claude / server owner

The owner clarified two decisions:

1. Do **not** remove mouse controls. Mouse-look, LMB/RMB combat, map/UI pointer and other established gestures remain. The actual UX bug is that world objects sometimes block/capture controls so the player cannot do anything. Please distinguish physical collider obstruction from UI/input capture and inspect the input/focus path.
2. The proposed common geographic reference for every settlement is its geographic centre. Villages/towns/cities and their content should be unambiguous in one coordinate system.

## What source inspection already found

The global centre-anchor concept already exists:
- `sim.Anchor(w, settlement)` in `sim/worldpos.go` returns the settlement centre in global frame 0, metres.
- `WorldManifest` and `AnchorView` publish that anchor.
- `WorldPosOf` derives global entity position as anchor + local scene offset; journeys interpolate between anchors.
- `WorldPos` is canonical global millimetre coordinates.
- However, `ScenePosC.X/Y`, Layout v2 geometry/POIs and some generator fields remain in distinct local/generator spaces.

Please do not assume this is fully unified merely because Anchor exists. Audit every field and consumer, then propose a backwards-compatible contract. The centre is a geographic reference, not necessarily a town square, spawn, mesh origin or player's location. Keep settlement-local coordinates for local simulation/physics, but make their space explicit and convert through one shared transform.

## Specific input concern

Current `WorldInteractionProbe.Update` resolves focused targets every frame and invokes RequestInteraction on E. It does not appear to alter movement/look directly, but the probe is enabled during active connection. Check interaction handling while CoreGameplay captures input, and inspect collider/layer and UI capture issues. A focused world object must never take over mouse or keyboard input by merely being aimed at.

Task details and acceptance criteria: `task_ai/018-coordinate-anchor-input-focus-audit.md`.

No server changes requested yet. Please return the audit and recommended migration/fix plan first; coordinate changes require review before implementation.
