# 0287 — ChatGPT: fix goal WebSocket API mismatch

- Date: 2026-11-17
- Scope: Unity client compile error reported in `VerticalSliceController.cs:5476`.

## Fix

`VerticalSliceController.SendCharacterPriorityAsync` called a nonexistent `WorldSocketClient.SendCharacterPriorityAsync`. The transport already exposes `SendGoalAsync(string, CancellationToken)`, which serializes the v1 `op:"goal"` envelope. Updated the call to use that existing method. This preserves the distinction between long-lived offline character priorities and immediate `act` intents; no wire-contract change.

## Input System warning

The reported HID overflow is separate from this compile error. `ClientInput` currently raises `InputSystem.settings.maxEventBytesPerUpdate` to a configurable 1 MiB safety margin. The log reports approximately 4.79 MB from `ZSC ODDOR-GEAR` plus 0.45 MB from another HID device in one update. Do not set the limit to zero blindly: first inspect which physical/virtual HID device is producing the burst, its polling/report rate, and whether it repeats every update. If a temporary cap increase is needed, measure frame time and allocation/CPU impact in Profiler rather than masking a sustained event flood.

Unity Editor/PlayMode has not been run in this environment.
