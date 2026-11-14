# 0235 — Independent review brief for Grok: Unity client wire DTOs

- Date: 2026-11-14
- Prepared by: ChatGPT
- Reviewer: Grok (independent consultant)
- Scope: review only; do not edit repository files or propose a redesign without evidence.
- Main PR under review: https://github.com/IskatelApelsinov/pogranichye/pull/638

## Canonical project invariant

Пограничье is a persistent, server-authoritative living-world simulation. Go server owns world state, NPCs, journeys, quest offers/progress/outcomes, economy, movement and persistence. Unity is a client/projection: it sends intentions and renders confirmed server state. Do not move simulation authority, quest completion, journey progression, NPC spawning or economy into Unity.

Both ADR 0006 entry modes remain intact. Protocol v1 remains additive and backward-compatible. Full frames and fast patches are different contracts; omitted fast fields must preserve the previous full state, and explicit null must remain distinguishable from omission.

## Review targets

1. PR #638: https://github.com/IskatelApelsinov/pogranichye/pull/638
2. DTO inventory: 3-dev/docs/CLIENT-WIRE-DTO-COVERAGE.md
3. Parser/DTO sources:
   - 2-client/game/Assets/_Project/Scripts/Networking/WorldFrameParser.cs
   - 2-client/game/Assets/_Project/Scripts/Networking/FastFrameParser.cs
   - 2-client/game/Assets/_Project/Scripts/Networking/Protocol/WorldFrame.cs
   - 2-client/game/Assets/_Project/Scripts/Networking/Protocol/CombatFoeFrames.cs
   - 2-client/game/Assets/_Project/Scripts/Networking/Protocol/JourneyQuestFrames.cs
   - 2-client/game/Assets/_Project/Scripts/Networking/Protocol/HorseStatsFrames.cs
   - 2-client/game/Assets/_Project/Scripts/Runtime/CoreGameplay.cs
4. Server schema sources:
   - 1-server/server/server.go: stateJSON / fastJSON
   - 1-server/sim/journey.go: JourneyView / RoadEvent
   - 1-server/sim/quests.go: Quest / QuestOffers / QuestsView

## Questions to answer

A. Contract fidelity
- Does each new DTO match the actual Go serializer types, optional fields, omitempty behavior, null behavior and numeric ranges?
- Are any required fields accidentally treated as optional or vice versa?
- Are there any places where the client parser rejects valid additive v1 data or accepts malformed data that could corrupt presentation?

B. Full/fast merge correctness
- Do full snapshots and fast patches remain distinct?
- Can absent fast fields accidentally reset full state?
- Is explicit null handled intentionally?
- Does the single latest me.result slot remain correlated by reqid without pretending to be an event queue?

C. Authority/privacy
- Does any new client code predict quest/journey progress or completion?
- Does any code create local authoritative NPC/foe entities or mutate world/economy outcomes?
- Are hidden card hands, unknown NPC names, private room/chest data and unknown map information protected?

D. Runtime and performance risks
- Any avoidable allocations/parsing duplication in hot paths?
- Any unsafe assumptions about null arrays, old v1 servers, AOI removal or stable IDs?
- Any clear compile-time issue in the C# changes?

## Required review format

Return findings only, ordered by severity:
- Critical / High / Medium / Low
- Exact file and method/property
- Concrete failing payload or execution sequence
- Why it violates the current server contract or project invariant
- Minimal safe correction

Then list:
- verified-safe areas;
- unverified runtime assumptions;
- questions that require a server owner (Claude) rather than a client edit.

Do not suggest broad rewrites, local simulation, wire version bumps, or parallel world-state caches. If there are no findings, say so explicitly and state what you did not verify.

## Status at preparation

- CI run #43636660993: all 18 Unity-independent C# regression steps passed for the previous typed status/feed/result/combat/foe slice.
- Journey/quest DTO parser and consumer changes are in PR #638; consolidated workflow #43638240668 passed all 20 steps, including the journey/quest parser runner. Stats/bag/horse scaffolding is present but not wired; see task #007.
- Unity Editor 6000.6.7f1 compile, shared fixture menu and PlayMode have not been run by ChatGPT and must not be reported as passed.
