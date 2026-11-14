!В работе
# 006 — Typed journey and quest views in Unity client

**Owner:** ChatGPT  
**Base:** client PR #638 branch `chatgpt/typed-status-feed-20261114`  
**Stacked PR:** #639 (target branch is PR #638 branch until #638 merges)

## Goal

Replace raw JObject reads for `me.journey` and `me.quests` with validated typed DTOs without moving any world or quest authority into Unity.

## Implemented in this slice

- `JourneyFrame` / `RoadEventFrame` and `JourneyFrameParser` map only the public `sim.JourneyView` projection.
- `QuestFrame` / `QuestsFrame` and `QuestsFrameParser` map only the public `sim.QuestsView` projection.
- `CharacterFrame` exposes typed Journey/Quests; `WorldFrameParser` validates before materialization.
- `CoreGameplay` uses typed journey data for road overlay and journey-choice input.
- `CoreGameplay` uses typed quest data for quest tracker, active-quest menu and offers menu.
- The opening `me.task` tutorial errand remains separate and unchanged.
- No fast-frame fields are invented; journey/quests are full-snapshot-only in the inspected server serializer.
- Added pure C# parser tests and Editor full-frame fixture cases.

## Invariants

- Server owns route progress, random events, quest availability, acceptance, progress, readiness, turn-in, rewards and reputation.
- Unity sends existing `journey/quest` intentions only; no local travel/quest completion or reward prediction.
- Do not DTO-map hidden `JourneyC.Plan`, `QuestC.Closed/Healed`, `Quest.Base`, internal numeric Giver/Settlement IDs.
- Do not change Go server or wire v1.

## Validation

- Pure C# journey/quest runner is in CI; result pending.
- Unity compile, Editor fixture menu and PlayMode are not claimed. Manual acceptance is in `task_human/005-verify-typed-journey-quests.md`.

## Next

After this slice, continue with inventory/horse/bag/stats, then settlement market/site/exits. Preserve the server-authoritative simulation boundary.
