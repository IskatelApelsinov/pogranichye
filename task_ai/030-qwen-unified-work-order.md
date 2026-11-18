# Qwen — unified work order (Unity client + Go server)

**Branch:** `agent/qwen-unity-workstream`  
**Repository:** `IskatelApelsinov/pogranichye`  
**Your role:** independent implementation engineer for the assigned Unity client and Go server work below. Work in this branch and open focused PRs. Do not merge PRs or release to `main`.

## Start here

1. Read `AI_WORK_RULES.md`, `task_ai/README.md`, `3-dev/comms/README.md`.
2. Read the detailed specifications:
   - `task_ai/028-qwen-independent-workstream.md` — Unity/client work QWEN-1…QWEN-5.
   - `task_ai/029-qwen-go-server-workstream.md` — Go/server work QWEN-S1…QWEN-S3.
   - Read the linked comms messages and current contracts/ADRs those tasks reference.
3. Inspect the current `main` and this work branch before editing. Treat the repository's current code and contracts as the source of truth; older task notes may be stale.
4. Post a short ownership/status note in `3-dev/comms/` if any task overlaps active work. Confirm server ownership with Claude before touching shared simulation or contract behavior.

## Complete work portfolio

### Unity client
- **QWEN-1 — Dense, multi-storey settlements:** map and render server-provided settlement layout descriptors, including storeys, height, grid plans, yards and forest-ring data. Preserve authoritative footprints, roads, entrances and POIs.
- **QWEN-2 — Seamless open-world streaming:** fix terrain height continuity, chunk transitions, negative coordinates, world/local origin rebasing, settlement A→B→A transitions, server road/trail geometry, request cancellation, stale responses, caching and memory behavior.
- **QWEN-3 — Multiplayer scalability:** profile and improve the client for 1/10/25/50/100 visible players: allocations, interpolation, animation, pooling, draw calls, LOD, UI and terrain. Record reproducible measurements; do not claim unrun Unity tests.
- **QWEN-4 — Multi-level interiors:** implement only the client architecture and rendering for actual server descriptors. Do not fabricate rooms, floors, entrances or interiors.
- **QWEN-5 — Wildlife and flora presentation:** render only server-authoritative wildlife, plants and gatherable resources with real IDs and descriptors. Coordinate with the server owner; do not create local fake spawns.

### Go server
- **QWEN-S1 — Server-authoritative fishing (first server priority):** audit the current fishing flow and existing product/contract notes first. Move bite timing, hook window, RNG, catch result, depletion/regrowth and energy cost to the server. The client displays phases and sends attempts only. Preserve request idempotency and handle duplicates, timeouts, reconnects, stale attempts, leaving the spot, insufficient energy and concurrent casts. Add deterministic unit and end-to-end tests. Agree on any wire-contract/fixture/ADR change with Claude and ChatGPT before implementation.
- **QWEN-S2 — City economy production chains:** audit existing inventories, production, wages, workplaces, markets and caravans. Add real finite-input/output production for 2–3 currently non-producing professions, with capacity and cadence. Prevent free goods, negative stock and runaway money/items. Add deterministic multi-day and save/load tests. Coordinate player-visible contract changes.
- **QWEN-S3 — Soak, persistence and recovery:** extend existing `pogload` and current test infrastructure; do not create a competing load-test framework. Cover reconnect churn, mixed activities, save/load under load, deterministic continuation, world expansion and bounded caches/sessions/goroutines. Make runs reproducible and clean up temporary saves.

## Execution order and parallelism

1. **Begin with QWEN-S1 and QWEN-1.** First audit both areas and report findings, risks, current ownership and a concrete implementation plan. For fishing, send the design to Claude/Kate and wait for agreement on shared contract changes.
2. Continue with QWEN-2 and QWEN-S2 after the initial audits. QWEN-3 and QWEN-S3 follow once measurable baselines and existing infrastructure are understood.
3. QWEN-4 and QWEN-5 may proceed when the required server descriptors/contracts exist. If they do not, prepare a client-side adapter/design and list the exact server fields needed; do not invent data.
4. You may work on Unity and isolated Go packages in parallel only when files, ownership and contracts do not conflict. Keep each feature in a separate focused PR; do not combine unrelated work into one giant PR.
5. Rebase/refresh against current `main` before each PR. Report changed files, behavior, tests run, tests not run, known limitations and any required decisions.

## Non-negotiable engineering rules

- The server is authoritative for world state, time, NPCs, players, entities, coordinates, resources, economy, fishing outcomes, persistence and simulation. Unity is a presentation/input client, not a second simulation.
- Never invent authoritative NPCs, animals, plants, resources, roads, terrain features, settlement population, buildings, interiors, loot or coordinates. Render real server data only.
- Preserve existing wire JSON names and semantics. Technical identifiers in code must be English. Do not casually rename contract fields or introduce parallel representations.
- Use existing patterns for DTOs, validation, request gates, idempotency, persistence, logging and tests.
- Shared simulation, API, wire contract, fixtures, persistence architecture, ADRs and cross-agent ownership must be coordinated with Claude and ChatGPT before changes.
- Wildlife/ecology is currently assigned to Claude in `task_ai/025-server-wildlife-and-flora.md`; do not modify its Go implementation unless ownership is explicitly transferred. QWEN-5 is client presentation only unless reassigned.
- Server multi-level spaces are assigned elsewhere; do not take over server-side implementation without explicit transfer.
- Do not rewrite systems wholesale. Prefer small, reviewable, backward-compatible changes.
- Run relevant Go checks: `go test ./...`, `go test -race ./...`, `go vet ./...` where applicable. For Unity, run available static/build checks and clearly distinguish them from tests that require the Unity Editor or runtime.
- Never claim a test, profiler run, Unity compile or runtime verification that you did not actually perform.
- Do not merge PRs, push directly to `main`, or make release decisions. Kate approves merges/releases.

## Definition of done

A task is complete only when its behavior is implemented, covered by appropriate tests, documented where needed, and submitted as a focused PR with:
- a concise problem/solution summary;
- key implementation decisions and files changed;
- exact test commands and truthful results;
- compatibility/contract impact;
- remaining risks or follow-up work.

If blocked by missing server data, product decisions or ownership conflicts, stop before fabricating a workaround. Write a concise handoff naming the exact missing decision/data and continue with independent tasks.
