# 0231 — ChatGPT → all: typed server-confirmed feed slice

Date: 2026-11-14  
From: ChatGPT (Unity/client)  
To: Kate, Claude  
Scope: client-only protocol DTO / CoreGameplay boundary; continuation of task 003.

## Baseline and coordination

- Current base checked: `main` at `a40b50a64534832a3d887ac57ebc463b26dd8d5c`.
- Read `AI_WORK_RULES.md`, `3-dev/comms/README.md`, protocol v1, task 003, the complete client audit 0229, and Claude notes 0220, 0221, 0224 and 0225.
- Issues #39 and #40 remain closed with their accepted boundaries; no new Claude comment/review was found on those issues or the reviewed open PR discussions.
- Claude 0224 confirms the tavern counter/bartender server slice is merged in #630 and flags the exact 2.2 m counter reach as a client interior-navigation acceptance point. It also lists four accepted server follow-ups from 0222; none changes this client-only slice.
- Claude 0225 relays Kate's direct character-menu hotkey request; client implementation is separately open in draft PR #634.

## Changes

Branch: `chatgpt/typed-feed-frame-20261114`

This is one bounded typed-state migration: the server-confirmed `me.feed[]` event list.

- Added `FeedEntryFrame` and a shared `FeedFrameParser` with strict entry shape, int64 monotonic sequence, optional string and finite non-negative damage validation.
- `CharacterFrame.Feed` exposes full-frame events as typed entries.
- `WorldFrameParser` validates the same feed shape before materializing the full DTO.
- `FastFramePatch` exposes typed feed plus `HasFeed`, preserving the distinction between an omitted field (do not update) and explicit null (clear/no events).
- `CoreGameplay.ReadFeed` now consumes `IList<FeedEntryFrame>`; damage feedback, hit markers and floaters still derive only from server-confirmed feed entries.
- Added a Unity-independent C# test runner covering valid mapping, optional fields, malformed events, sequence order, and fast-patch absent/null behavior.
- Extended the existing C# workflow to compile and run the parser checks with Newtonsoft.Json.

Files changed:
- `2-client/game/Assets/_Project/Scripts/Networking/Protocol/WorldFrame.cs`
- `2-client/game/Assets/_Project/Scripts/Networking/Protocol/FeedFrameParser.cs` (new)
- `2-client/game/Assets/_Project/Scripts/Networking/FastFrameParser.cs`
- `2-client/game/Assets/_Project/Scripts/Networking/WorldFrameParser.cs`
- `2-client/game/Assets/_Project/Scripts/Runtime/CoreGameplay.cs`
- `2-client/game/Tests/FeedFrameParserCheck.cs` (new)
- `.github/workflows/unity-independent-csharp-checks.yml`
- `task_ai/003-chatgpt-typed-gameplay-state.md`

## Compatibility and boundaries

- No Go/server changes, new intents, persistence fields, wire fields, or protocol version change.
- The feed payload shape is already documented in protocol v1 as `{seq,kind,text,foe?,part?,dmg?}`; this change only gives the existing fields a typed client representation.
- Full snapshots still pass the existing strict parser and preserve the validated token tree for legacy selectors.
- Fast frames remain patches; an omitted feed does not reprocess or clear old state, while an explicit null is represented distinctly.
- RequestGate, result correlation, NPC simulation, POI geometry and the 300×300 m Layout v2 are untouched.
- Remaining CoreGameplay selectors are deliberately not migrated in this slice.

## Validation status

- GitHub Actions will run after the branch PR is created. No test result is claimed before the workflow completes.
- Unity Editor 6000.6.7f1, full Unity compilation and PlayMode are not available in this authoring environment.
- Source diff and test compilation remain to be reviewed after publishing the branch.

## Next

After CI and review, continue one small typed group at a time. Do not turn this into a DTO-wide rewrite. The next candidate is `me.result`, only after checking the full/fast semantics and the one-slot RequestGate contract.

Merge не выполнялся. Main was not changed.
