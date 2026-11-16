# Task 027 — developer mode, ground following and overland walking

## Ownership
- Unity client: ChatGPT
- Go server, account authorization, time controls and world geometry: Claude
- Handoff: 3-dev/comms/0276-chatgpt-to-claude-dev-mode-grounding-and-overland-walk.md

## Scope

### Developer mode
- [ ] Allowlist developer accounts by immutable numeric account ID from validated token/session; default deny.
- [ ] Publish additive me.dev capabilities.
- [ ] Server-validated time pause/set/step/speed multiplier commands.
- [ ] Server-validated movement speed multiplier for only the authorized test character.
- [ ] Audit log, limits, reqid idempotency, reset controls.
- [ ] Unity developer tab only when server says me.dev.enabled.
- [ ] Owner creates a dedicated account and supplies its numeric account ID for allowlist.

### Terrain grounding (client first pass)
- [x] Stop resetting player root Y to zero every moving frame.
- [x] Apply sampled server terrain height relative to current settlement anchor during movement and while idle.
- [ ] Unity Editor test: flat ground, uphill/downhill, side slope, chunk seam, settlement handover, jump.
- [ ] Compare player root Y to server me.wpos.ground and mesh.

### Overland walk / frontier
- [ ] Server E2E test using ordinary movement intents, not teleport/travel/goto.
- [ ] Verify WPos continuity and settlement handover A→B→A.
- [ ] Verify terrain chunk requests and content while crossing multiple 256 m chunks.
- [ ] Verify neighbor settlement layout is requested, loaded and rendered at correct global offset.
- [ ] Fix gaps/blank frames, stale layout collision and entity presentation during handover.
- [ ] Replace straight-line road graph chords with server-authored terrain-following road/trail polylines.
- [ ] Add stable road kinds (road/trail/pass/bridge/ford only when backed by world data).
- [ ] Chunk clipping must preserve exact shared endpoints.

## Current evidence
- Existing Go tests: TestSeamlessLandsKeepWorldPosition, TestSeamlessRoundTripPreservesIdentityAndState in 1-server/sim/worldpos_test.go.
- These prove server-side coordinate handover, not Unity runtime streaming.
- TerrainChunkUsing returns server heights and content descriptors.
- chunkRoads currently derives straight clipped segments between settlement anchors, not a true route.
- Unity README states only one settlement layout is active at a time; Editor/PlayMode remains unverified.

## Rules
- Server remains authoritative for clock, movement, speed, road geometry, terrain, and settlement handover.
- No client-generated authoritative roads, trails, collision or settlement geometry.
- No dev permission based on login name or client-supplied fields.
- Do not claim Unity route is tested until PlayMode/Editor evidence is recorded.
