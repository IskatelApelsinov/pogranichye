# 0217 — ChatGPT: regression coverage for signed terrain chunk seams

- Дата: 2026-11-13
- От: ChatGPT
- Связано: ADR 0007, ADR 0008 (RFC), comms 0173, 0210, PR #456/#463/#580

## Finding

`TerrainChunk` in `1-server/sim/worldpos.go` samples the deterministic global terrain function at canonical chunk origins. Adjacent chunks should therefore share exactly the same edge samples. The existing `TestTerrainIsDeterministicFlatInVillagesAndSeamless` checked one horizontal and one vertical neighbor at step 16, but did not cover all supported grid steps, negative X/Z chunk indices, request-order independence, or independence from the player's active settlement.

## Change

Added `TestTerrainChunkBordersAreDeterministicForSignedCoordinates` to `worldpos_test.go`:
- checks horizontal and vertical shared edges at 8/16/32/64 m sample steps;
- includes negative chunks, zero-boundary crossings and positive chunks;
- compares exact edge samples (the API's rounded height values);
- requests the same chunks in forward/reverse order and compares all height samples;
- changes only the test player's `PlaceC.Settlement` and confirms the same canonical chunk heights remain identical.

A small test helper reports the first differing sample.

## Architecture

- Test-only; no runtime behavior or wire fields changed.
- Keeps ADR 0007's 256 m square chunk and 64 m vertical layer model.
- Does not change the settlement-local 300×300 m Layout v2 geometry, POI keys, road graph or server authority.
- Does not claim full terrain normal/collider continuity in Unity; this verifies server height samples only.
- Player/NPC A→B→A lifecycle and spatial AOI remain separate acceptance gaps from comms 0210/0173.

## Validation

GitHub Go CI is expected to run on this PR. No Go result is claimed until that run completes. Unity Editor/PlayMode is not relevant to this server-only test.

Merge не выполнялся.