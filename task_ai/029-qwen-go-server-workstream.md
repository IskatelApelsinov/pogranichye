!В работе
# Task 029 — Qwen: isolated Go server work packages

## Ownership and coordination
- Primary executor: Qwen for the explicitly listed packages in this task.
- Server architecture owner / mandatory technical handoff: Claude.
- Independent review and client integration: ChatGPT.
- Product decisions and merge approval: Kate.
- Working branch: `agent/qwen-unity-workstream`.
- Go code and Go tests are in scope. This is not a client-only assignment.
- Claude retains ownership of the overall simulation architecture and any unassigned server subsystem.
- Do not edit a subsystem currently being changed by Claude. Confirm ownership in `3-dev/comms/` before implementation.
- PRs target `main`; do not merge or release.

## Read first
- `AI_WORK_RULES.md`
- `3-dev/docs/СКЕЛЕТ-ЖИВОГО-МИРА.md`
- `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md`
- `3-dev/docs/adr/` (all active ADRs relevant to the package)
- `3-dev/comms/README.md`
- `3-dev/comms/0301-kate-to-claude-server-space-window-apertures.md`
- `3-dev/comms/0302-kate-to-claude-server-authoritative-fishing.md`
- `3-dev/comms/0293-claude-to-all-world-variety-cities-realms-v133.md`
- `3-dev/docs/НАГРУЗКА-POGLOAD.md`
- `task_ai/024-server-stamina-and-movement-profile.md`
- `task_ai/025-server-wildlife-and-flora.md`

## Server work packages

### QWEN-S1 — Server-authoritative fishing (START HERE)

Kate explicitly identified a gameplay-authority mismatch in comms 0302:
- Unity currently chooses a random bite delay and local 1.2-second hook window.
- Server `FishCatch` validates location/stamina and grants a fixed catch, but does not know whether the player hooked at the right time.
- A client can therefore fake the minigame outcome or the timing has no server meaning.

Steps:
1. Audit current `1-server/sim/actions.go`, `FishCatch`, action dispatch, RequestGate/reqid patterns, frame result payloads, persistence and fishing-related tests. Confirm the mismatch against current main; do not assume the 2026-11-18 note is still current.
2. Write a short design/handoff to Claude and Kate before changing wire behavior. Propose a server-owned fishing session/state machine: cast/start, bite window, hook attempt, resolved outcome, expiry/cancel.
3. The server owns RNG, bite timing, accepted timing window, catch result, resource depletion/regrowth and energy cost. Client only displays phases and sends an attempt.
4. Use existing idempotency/RequestGate/ACK conventions. Duplicate reqid must not grant a second catch or charge energy twice.
5. Define reconnect behavior, timeout, stale attempts, player leaving the valid fishing area, simultaneous requests and session cleanup.
6. Keep server state bounded; no unbounded per-player fishing sessions or timers.
7. If a wire change is required, coordinate additive v1 contract, ADR, fixture and Unity DTO changes with ChatGPT before implementation. Do not rename existing actions/IDs silently.
8. Add deterministic tests for too-early, valid, too-late, expired, duplicate reqid, reconnect, leaving the spot, insufficient energy and repeated casts.
9. Add an end-to-end server test that demonstrates the actual accepted/rejected flow, not only helper-level tests.

Acceptance:
- Client timing cannot determine the catch outcome.
- Server is sole authority for bite timing and catch/reward.
- Repeated request is idempotent.
- No session/resource/energy leaks after expiry, reconnect or cancellation.
- Contract docs/fixtures/tests match the implementation.
- `go test ./...`, `go test -race ./...`, `go vet ./...` pass.
- Unity integration requirements are handed to ChatGPT in a numbered comms file.

### QWEN-S2 — City economy: make newly added professions produce real goods

Context from Claude v1.33 handoff (comms 0293):
- Three generated capital cities have around 70 houses each and ~160 residents across 15 professions.
- Taxes and food/raw-material caravans already move between cities and villages.
- New professions (e.g. traders, guards, carpenters, leatherworkers, potters, bakers, scribes, jewelers) currently receive wages but do not produce their own goods, except for existing implemented professions such as blacksmith, weaver, cook and herbalist.

Steps:
1. Re-audit current production, inventory, market, wages, caravan and NPC activity code. Map each profession to existing recipes, inputs, outputs, workplace, stock and consumers.
2. Do not implement a separate economy. Extend the current authoritative systems and deterministic tick/seed patterns.
3. Select a small first tranche of professions with clear existing item definitions and inputs. Prefer carpenters, leatherworkers, potters and bakers only where item IDs/recipes already exist or can be added consistently.
4. Implement production as server simulation, with finite inputs, output inventory, work cadence, capacity and meaningful shortages. Do not create goods from nothing or allow negative stock.
5. Connect outputs to existing market/trade/caravan flows. Avoid a fake visual-only production statistic.
6. Ensure NPCs without a valid workplace/inputs behave honestly (idle, seek input, unavailable) rather than silently producing.
7. Add deterministic economy tests across many days and save/load; verify stock conservation, no negative inventories, no runaway money/items, and that towns remain viable.
8. Report any missing item definitions or design decisions rather than inventing a parallel item taxonomy.

Acceptance:
- At least 2–3 selected professions create real server-owned inventory goods from finite inputs.
- Produced goods enter existing trade/market paths and can be consumed or purchased using existing systems.
- Multi-day simulation shows no item/money creation exploit or negative stocks.
- Deterministic results for the same seed and action sequence.
- Existing village/city economy tests remain green.
- Any new player-visible data is coordinated with ChatGPT; no contract change without agreement.

### QWEN-S3 — Server simulation soak, persistence and recovery

This package extends existing tools; do not duplicate `pogload`.

Steps:
1. Audit `1-server/cmd/pogload`, `3-dev/docs/НАГРУЗКА-POGLOAD.md`, existing scale tests, persistence/save-load and CI workflow.
2. Identify gaps not already covered by current tests: long-running mixed activity, repeated reconnect, save/load during load, deterministic continuation after reload, world expansion while clients move, bounded caches/sessions and goroutine cleanup.
3. Add repeatable soak scenarios to existing `pogload` or existing test harness. Use real authenticated WS clients where testing transport behavior; use sim-level tests for deterministic world simulation.
4. Add a save/reload scenario with a dedicated temporary save file and explicit cleanup. Compare stable world/economy/NPC invariants before and after reload.
5. Add race/leak checks where meaningful. Avoid fragile wall-clock performance thresholds before collecting baseline.
6. Provide a manual/nightly run recipe and structured report. Never target production or user saves.

Acceptance:
- Repeated soak and save/reload runs are reproducible from documented commands.
- No stale sessions, duplicate body control, unbounded resource growth or simulation divergence after reload.
- Functional checks are automated; performance numbers are reported with environment and commit SHA.
- Existing `pogload` functionality is extended, not forked into a second tool.

## Explicitly not assigned / avoid overlap

- Wildlife/ecology implementation is already assigned to Claude in `task_ai/025-server-wildlife-and-flora.md`. Do not edit it unless Claude/Kate explicitly transfer the task.
- Server multi-level spaces and movement through doors/stairs remain under existing task 022 and ADR process.
- Spawn mode selection (demo cauldron vs open-world) requires Kate's product decision; do not implement a guessed UX.
- Offline catch-up simulation is an architectural decision; research and document only if Kate/Claude explicitly asks.
- Water geometry and road/trail/bridge/pass semantics are documented gaps; do not invent a new world model without an ADR.
- Do not rewrite Go server architecture, ECS, persistence or protocol wholesale.

## Workflow
1. Start with QWEN-S1. Confirm current status and ownership with Claude in comms before code.
2. Keep S1, S2, S3 as separate PRs.
3. Each PR includes focused tests and exact Go commands/results.
4. Send ChatGPT a client-facing handoff for any DTO/contract changes.
5. No PR merge/release without Kate's approval.

## Report
For each package: findings, exact paths/symbols, design decision, files changed, tests actually run, commit SHA, PR URL, migration/compatibility notes, client handoff, remaining risks.
