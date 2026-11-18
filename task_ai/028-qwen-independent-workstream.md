!В работе
# Task 028 — Qwen: high-effort Unity world presentation and scalability

## Ownership
- Primary executor: Qwen (Unity client work packages in this task)
- Go/server work: permitted only through the separate explicit packages in `task_ai/029-qwen-go-server-workstream.md`
- Integration/review: ChatGPT
- Overall server architecture owner: Claude
- Owner acceptance: Kate
- Working branch: `agent/qwen-unity-workstream`
- Target: PR from this branch to `main`; never merge without Kate's approval.

## Mission

Take one of the most labor-intensive remaining Unity client workstreams and deliver it as small, reviewable PRs. The goal is to make the same server-authored world look and behave like a dense, continuous, believable place without inventing gameplay state on the client.

Read first:
- `AI_WORK_RULES.md`
- `3-dev/docs/СКЕЛЕТ-ЖИВОГО-МИРА.md`
- `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md`
- `3-dev/docs/UNITY-OPEN-WORLD-ARCHITECTURE.md`
- `3-dev/docs/adr/0004-seamless-world-streaming.md`
- `3-dev/docs/adr/0007-global-coordinates-seamless-lands-and-village-layouts.md`
- `3-dev/docs/adr/0008-hex-cells-over-global-chunks.md`
- `task_ai/027-developer-mode-grounding-overland-walk.md`
- `3-dev/comms/0293-claude-to-all-world-variety-cities-realms-v133.md`
- `3-dev/comms/0296-claude-to-chatgpt-pogload-synthetic-load.md`
- `3-dev/comms/0300-kate-to-claude-audio-lighting-ui-assets.md`
- `3-dev/comms/0305-chatgpt-to-claude-terrain-gather-presentation.md`
- `3-dev/docs/DEMO-ACCEPTANCE.md`

## Priority work packages

### QWEN-1 — Multi-storey cities and dense settlement rendering (START HERE)

Current gap: server v1.33 sends city layouts with `plan: "grid"`, multi-storey residential buildings with `storeys` and `height_m`, and land descriptors such as `yards` and forest-ring zones. Existing Unity presentation has historically rendered buildings as single-storey boxes. The server now provides much richer settlement variation; the client must represent it faithfully.

Tasks:
1. Audit current layout DTOs and all building/land presentation builders. Map every server field to its current renderer or document the missing mapping.
2. Implement multi-storey facade and roof proportions from server `storeys`/`height_m`; preserve building footprint and world coordinates. Avoid arbitrary floor counts or dimensions when server data exists.
3. Render `plan: "grid"` with appropriately dense streets/blocks while preserving server-provided footprints, entrances, roads and POIs. Do not invent additional functional buildings or NPCs.
4. Render `yards` as small garden/field areas near their server-provided homes and forest-ring zones from the actual descriptors. Do not scatter extra resource nodes or harvestables.
5. Check camera framing, collision bounds, facade openings, occlusion, LOD and material/texture scale for dense city blocks.
6. Add deterministic source-level regression checks for height/storey mapping, bounds and optional-field compatibility. Keep older rural layouts valid.
7. Provide before/after captures or a reproducible owner test checklist. Do not claim visual runtime acceptance without a real Unity Editor run.

Acceptance:
- Same layout data produces deterministic building dimensions and placement.
- Single-storey rural homes remain visually correct.
- Multi-storey city homes visibly use server-authored height/storey data.
- Dense grid layout remains navigable and does not cover or relocate POIs.
- Yard and forest-ring presentation uses only server-authored zones.
- No fake NPCs, doors, resources, roads, interiors or gameplay state.
- CI/source checks pass; Unity Editor/PlayMode status is reported honestly.

### QWEN-2 — Seamless overland client streaming and terrain continuity

This is a major follow-up, not permission to rewrite the world architecture.

Audit and improve the Unity side of:
- continuous player grounding against server `me.wpos.ground` and terrain heightfields;
- chunk transitions, negative coordinates, seam sampling and global/local anchor rebasing;
- settlement handover A → B → A without blank frames, stale colliders or duplicate entities;
- terrain, layout, POI and entity streaming over multiple 256 m chunks;
- road/trail rendering from server-authored geometry, exact shared endpoints and clipping;
- cache lifetime, cancellation, stale response rejection and bounded memory.

Start from existing ADRs and code. `task_ai/027` already lists shared work; Qwen owns only Unity client code. If server road polylines or another field are missing, document the exact contract gap and send a comms handoff to Claude. Do not generate authoritative roads or terrain locally.

Acceptance:
- A reproducible test plan covers flat/uphill/downhill/side-slope, chunk seam, negative coordinates and settlement handover.
- No visible ground-height discontinuity or stale world geometry in tested transitions.
- Existing global coordinate/anchor conventions remain unchanged.
- No client-side authoritative world content.
- Performance/memory impact is measured or explicitly marked unmeasured.

### QWEN-3 — Unity client scalability for 1/10/25/50/100 visible players

Server load harness `pogload` already exercises real WebSocket clients, but it does not measure Unity rendering. Build a reproducible client profiling/acceptance path and fix proven client bottlenecks.

Audit:
- per-frame allocations and LINQ/string/JSON churn;
- neighbor/player interpolation and animation updates;
- GameObject creation/destruction and pooling;
- draw calls, batches, material instances, skinned meshes and LOD;
- UI refresh frequency and layout rebuilds;
- terrain/chunk renderer and collider rebuild frequency;
- network RX/TX and snapshot processing cost.

Deliver a stable test scene/configuration, profiler capture instructions, metrics table and targeted optimizations. Do not optimize blindly or raise caps to hide missing AOI. Do not claim 100-player performance until measured in Unity with a reproducible client setup.

Acceptance:
- Baseline and after metrics for 1/10/25/50/100 clients/visible actors, where the local setup permits.
- Frame-time p50/p95, main/render thread, memory, draw calls/batches and GC allocations.
- No loss of server authority, animation correctness or spatial culling.
- Any unavailable measurement is explicitly marked pending owner runtime test.

### QWEN-4 — Multi-level interiors (blocked on server descriptors)

Audit the current `/api/spaces` DTO and Unity interior renderer. Existing server descriptors cover only selected spaces (e.g. tavern, tower, cave); do not fabricate interiors for ordinary homes. Propose a client renderer architecture that can consume future server-authored room/floor/door/window descriptors. Coordinate with Claude before any contract change. This package is research/design until the server supplies the necessary data.

### QWEN-5 — Server-authored wildlife and flora presentation (blocked on server payload)

Review `task_ai/025-server-wildlife-and-flora.md` and current terrain/gather DTOs. Build only the client integration that is supported by actual server descriptors and stable IDs. Distinguish decorative flora from harvestable nodes. No locally spawned animals, fake populations, loot or gathering targets. Coordinate the DTO handoff with Claude.

## Working rules

- Start with QWEN-1. Do not attempt all packages in one giant PR.
- Before editing, inspect current `main` and this branch; the task may have been partly implemented since its last note.
- Keep all technical identifiers (types, methods, properties, fields, locals) in English. Preserve existing JSON wire keys exactly.
- Respect server authority. Unity is a renderer/input client, not a second simulation.
- Do not alter Go server code under this client task; use task 029 for explicitly assigned Go work. Do not change API schemas, wire contracts, fixtures or ADRs without explicit handoff and agreement with Claude/Kate.
- Avoid broad refactors. Prefer one coherent feature/fix per PR.
- Add or extend source-level tests where the repository's Unity-independent workflow can validate the logic.
- State exact checks run. CI success is not proof of Unity Editor/PlayMode success.
- Do not commit secrets, tokens, credentials or local machine files.
- Do not merge PRs or release builds. Ask for ChatGPT review and Kate's merge decision.

## Handoff/report format

For each work package:
1. Findings and exact files/classes involved.
2. Scope selected and out-of-scope items.
3. Implementation and tests.
4. Commit SHA and PR URL.
5. Known limitations and owner-side Unity test steps.
6. Any server/contract dependency, written as a new numbered `3-dev/comms/NNNN-qwen-to-claude-...` handoff.

## Current status

!В работе — QWEN-1 is the first assigned package. No code implementation is recorded yet.
