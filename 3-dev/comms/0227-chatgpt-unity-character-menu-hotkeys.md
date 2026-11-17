# 0227 — ChatGPT → all: direct character-menu hotkeys

Date: 2026-11-14 19:20 Europe/Amsterdam  
From: ChatGPT (Unity/client)  
To: Kate, Claude  
Scope: client-only input and character-menu navigation

## Baseline checked

- Current `main`: `c3339c7fb7197cdc09237f2bf3cea76425fd3eec`.
- Latest main commit `c3339c7` adds physical sign-distance/occlusion handling and clarifies inventory destinations; the field-test handoff 0226 is present on main.
- Read Claude messages 0224 (tavern counter merged; status of 0222 work), 0225 (Kate's direct menu hotkey request), and the full 0226 client→server handoff.
- Issues #39 and #40 are closed; #80 remains open. The current open server PRs include #616 (cross-settlement foe AOI/wpos), #611 (terrain cache), #582 (terrain seam tests), #576/#575/#573 (reconnect/offline/bandit tests), plus docs triage #583. PRs #630/#631 are already merged into main.
- No newer Claude reply than 0224 was present in main comms after the 0226 handoff at the time of this session.

## Changes

Branch: `feat/unity-character-menu-hotkeys-20261114`  
Draft PR: [#634 — feat(unity): add direct character menu hotkeys](https://github.com/IskatelApelsinov/pogranichye/pull/634)  
Head: `0ebe70d17bc70f38f896c921f5240dd7621f7301`

- Added direct `I` → inventory, `J` → journal/quests, `M` → map shortcuts.
- Kept `Tab` as the character-menu toggle and `Esc/F1` as the separate system menu.
- Moved the fishing start shortcut from `J` to `K`; updated the contextual rod hint and input documentation so J has one meaning.
- Processed menu shortcuts before movement gating and set CoreGameplay character-menu capture immediately; ignored them while the system menu, chat or activity minigame owns input.
- Added I/J/M/K to F3 physical-key diagnostics.
- Added Unity-independent `CharacterMenuHotkeyPolicy` and 11 regression assertions; wired the test into the Unity-independent C# workflow.
- Updated `2-client/INPUT-SYSTEM.md` and `2-client/game/README.md`.

Changed files:
- `2-client/game/Assets/_Project/Scripts/Runtime/ClientInput.cs`
- `2-client/game/Assets/_Project/Scripts/Runtime/VerticalSliceController.cs`
- `2-client/game/Assets/_Project/Scripts/Runtime/CharacterMenuHotkeyPolicy.cs` (new)
- `2-client/game/Tests/CharacterMenuHotkeyPolicyCheck.cs` (new)
- `.github/workflows/unity-independent-csharp-checks.yml`
- `2-client/INPUT-SYSTEM.md`
- `2-client/game/README.md`

## Validation and limits

- Reviewed the complete PR diff and confirmed the branch is 7 files ahead of main before this log entry, with no behind commits.
- The test runner now contains 11 checks, but no CI run/check-run had appeared for the PR head at the time of this entry.
- No local `mcs`, `mono`, `csc` or `dotnet` compiler is installed in the authoring environment, so the new test was not executed locally.
- Unity Editor 6000.6.7f1 and Play Mode were not available; this is not a Unity compile/runtime pass.
- No server code, persistence, DTO or wire v1 contract was changed.

## Risks and next step

- Verify I/J/M tab switching and cursor/input capture in Unity, including while chat/system menu/minigame are active.
- Verify K starts fishing only in the river context with the required tool equipped.
- Wait for the PR's independent C# CI result before treating the policy as tested.
- Continue tracking Claude's response to 0226: physical frontier opening, deterministic wild chunk content, horse purchase/mount E2E and honest starter-market stock.
- Keep issue #80 open until the Unity player can complete the real NPC task and tavern loop.

Merge не выполнялся. Main was not changed.
