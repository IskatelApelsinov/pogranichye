# 0235 — ChatGPT → Kate/Claude: typed JourneyView and QuestsView

- Date: 2026-11-14
- From: ChatGPT
- To: Kate, Claude
- Consolidated PR: #638
- Related: task_ai/006, docs/CLIENT-JOURNEY-QUEST-DTO-COVERAGE.md

## Server contract audit

This client slice was checked against current Go view builders:
- `sim.JourneyView` is the only source for `me.journey`. It intentionally projects destination, progress, ETA, scene, note, follow, pending event and via names. Internal `JourneyC.Plan` and exact event timing are not exposed.
- `sim.QuestsView` is the source for `me.quests`. It returns public offers, active progress/readiness, done count and current-settlement reputation.
- `QuestOffers` serializes the public Quest fields. Internal numeric Giver/Settlement IDs, Base, Closed and Healed are not exposed.
- The inspected fast frame does not carry journey or quests. Do not add them to fast frames without a separate server contract decision.

## Client changes

- Typed JourneyFrame/RoadEventFrame and QuestFrame/QuestsFrame.
- Shared full-frame validation before DTO materialization.
- CoreGameplay road overlay, event choice handling, active quest tracker, offers/active menu consume typed views.
- The opening tutorial `me.task` remains a separate system.
- Player choices and quest actions remain server intentions; no local outcome prediction.

## Boundaries

No Go changes, no protocol/wire changes, no local travel/quest simulation, no hidden server bookkeeping in DTOs. ADR 0006 entry modes remain unchanged.

## Validation

Pure C# runner and Editor full-frame fixture cases are added; all 20 CI steps passed in run #43638240668. Unity Editor/PlayMode not run. Unity Editor/PlayMode not run. See `task_human/005-verify-typed-journey-quests.md`.

— End of 0235
