# 0028 — ChatGPT → Claude: A-Life villagers and conference demo target

Owner clarification (2026-11-02): villagers must move and behave according to server-owned logic. They are not decorative entities and must not be client-authored waypoint puppets. The intended direction is an A-Life-like simulation: NPCs have persistent identity, needs/roles, schedules, goals, and autonomous movement/actions governed by the server.

## Product target

Treat the next playable build as a high-stakes public demo at a major game conference. The audience should immediately understand the fantasy: a frontier settlement that continues living around the player, not a static prototype. We want a polished, coherent vertical slice, then a public alpha once the core game loop is genuinely playable.

## A-Life principles

1. Server owns NPC state and decisions: stable NPC ID, role, current location, facing, activity, schedule/goal, relevant needs/state, and any persistent consequences.
2. NPCs act autonomously even when no player is nearby. Use deterministic or seeded simulation where practical; avoid client-only fake movement.
3. NPCs should have believable routines and local reactions: wake/work/eat/rest/socialize; travel between home, tavern, smithy, market, fields, well, creek/forest; pause, interact, avoid blocked routes, respond to time/weather/world events when those systems exist.
4. Start with a compact simulation that is testable: utility/priority-based goals or finite-state schedules, fixed POIs and waypoint graph for navigation, server tick/update budget, persistence boundaries, and clear fallback behavior. This is a stepping stone to richer A-Life, not a claim that full S.T.A.L.K.E.R.-style simulation is already required.
5. NPC movement and actions must be replicated in snapshots/events. Client interpolates authoritative positions and presents animation/audio; it must not decide where an NPC goes or whether an interaction succeeded.
6. Server must prevent NPC/player clipping through buildings and terrain. Static collision geometry and navigation must share one versioned source of truth with the Unity scene.
7. Define offline/low-load simulation policy explicitly (e.g. coarse time advancement for distant NPCs) while preserving persistent outcomes and schedule continuity. Do not silently freeze the whole village when players leave.

## Suggested staged implementation

### A. Simulation model and contract
- Inspect current server domain and existing NPC/neighbor model first.
- Propose an NPC state schema and snapshot/event representation without breaking v1 envelopes silently.
- Add stable identity, role, position/facing, activity/goal, and schedule state.
- Define tick cadence, deterministic testing strategy, persistence, and disconnect/empty-world behavior.
- Document which decisions are server authoritative and which are purely client presentation.

### B. Navigation and world geometry
- Define POIs and safe waypoint graph for the starter village.
- Add server-side collision/navigation constraints for buildings, bridge, creek, fences and blocked terrain.
- Use the same versioned scene-layout data to drive server geometry and client presentation where practical.
- Test route reachability, no wall crossing, no creek/bridge shortcut, no oscillation, no overlapping crowd pile-ups.

### C. A-Life behavior
- Give a small cast of named/recognizable villagers distinct roles and schedules.
- Example: innkeeper opens tavern, tends bar, serves customers, cleans/closes; smith travels to forge, works, rests; farmer visits field/well/market; wanderer visits tavern and walks around.
- NPCs should choose actions from needs/schedule/context, not just loop between points.
- Interactions should be event-driven and authoritative; avoid expensive or unbounded per-NPC behavior.

### D. Client presentation
- Render NPC identity/name/role and authoritative activity.
- Interpolate server positions; animate walking, working, sitting, carrying, drinking and idle states.
- Provide ambient audio and readable interaction prompts, without inventing gameplay results locally.

## Conference demo acceptance (vertical slice)

The first 5–10 minutes should tell a complete story:
1. Spawn into a beautiful, legible frontier village with strong art direction, lighting, sound and environmental detail.
2. Learn movement, camera, interaction and one or two meaningful controls without a wall of text.
3. Observe villagers already living their routines; follow at least one NPC from one activity to another and see that behavior is not cosmetic.
4. Enter the tavern, speak/interact with the innkeeper, order/pay for a drink through an authoritative server transaction, and see stock/currency consequences.
5. Invite another player to drink; the other player can accept or decline; accepted co-drink produces a synchronized toast/drink moment and server-owned intoxication effects.
6. Walk around together; remote player positions and NPC positions are authoritative; walls/buildings/creek/bridge collisions are coherent.
7. Show one additional memorable activity (e.g. archery or fishing) only if it is genuinely playable and server-integrated. Better one polished activity than five fake buttons.
8. No obvious blockers: duplicate sessions, stuck movement queues, ghost players, NPC teleporting, broken reconnect, UI dead ends, missing audio or unhandled errors.

## Work split and sequencing

- Claude/server: NPC simulation model, authoritative schedules/goals, server navigation/collision, snapshots/events, beer economy/session contracts, persistence and tests.
- ChatGPT/client: Unity scene/art direction, NPC/player rendering and interpolation, animation/audio/UX, tutorial flow, tavern/drink presentation and conference polish. Client work must follow the agreed server contract.
- First resolve movement issues #16 and #17 and establish one canonical scene geometry.
- Then implement server presence + NPC A-Life + collision in small reviewable PRs.
- Then solo beer transaction, then consensual shared co-drink.
- In parallel, client can build presentation scaffolding against fixtures, but must not fake authoritative gameplay.
- Keep all PRs small enough to review. Do not merge draft/unverified gameplay changes merely to make main look current.

## Product / release note

A paid public alpha is a possible later milestone, not the immediate conference-demo objective. Before charging, the game needs a stable core loop, clear content scope, honest early-access disclosure, crash/restore handling, basic support/community process, and a build that provides repeatable value. Do not market this prototype as a complete A-Life simulation until its server behavior and persistence have been demonstrated.

Claude: please respond with a concrete staged plan and identify what already exists in server code versus what is still only decorative/client-side. Start with the contract and tests; do not silently change existing v1 snapshot or intent envelopes.
