# Unity Input System migration

Date: 2026-11-10

The thick client now uses the Unity Input System package (com.unity.inputsystem 1.20.1) and a shared semantic action component, ClientInput.

## One-time editor setting

After pulling this branch, let Unity resolve the new package. If Unity asks to enable the new input back end, choose Yes and allow the Editor to restart.

If no prompt appears, set:

1. Edit → Project Settings → Player.
2. Other Settings → Configuration → Active Input Handling.
3. Select Input System Package (New).
4. Restart the Unity Editor when prompted.

The project currently does not version-control a full ProjectSettings/ProjectSettings.asset; this setting is therefore a local Editor setting and must be applied once on each fresh checkout.

## Current bindings

| Action | Binding |
|---|---|
| Move | WASD or arrow keys |
| Look | Mouse delta |
| Zoom | Mouse wheel |
| Sprint / jog | Shift / Alt |
| Jump | Space (presentation only; in a minigame Space performs its primary action) |
| Interact | E |
| System menu | F1 or Esc; Esc first closes chat/minigame when one is active |
| Character menu | Tab opens/closes; I opens inventory, J opens journal/quests, M opens map |
| World HUD panel | On-screen collapse/expand button |
| Diagnostics / fullscreen | F3 / F11 |
| Chat | T opens; Enter or KeypadEnter sends; Esc closes |
| Voice | Join the voice channel; enable open mic with the button, or hold P for push-to-talk (grant mic permission once first) |
| Woodcutting / fishing | G / K to start; Space for the minigame action; Esc cancels |
| Weave mode | Hold R; nodes 1–6; Backspace removes; RMB clears |
| Primary / guard | LMB / hold RMB |
| Dodge / quick attack | Q / F |
| Combat tricks | Z / X / V / B / C |
| Cycle stance / hints | N / H |
| Road event choice | 1–9 |
| Card games at tavern | 1–2 to start/choose in Очко; 1–6 cards, 7 take, 8 finish in Durak |

F3 diagnostics show the physically held keyboard/mouse buttons, the latest semantic action and (while the overlay is open) raw Input System event traffic by device: bytes, event count, largest event and the configured per-update budget. This helps identify a noisy HID device when Unity reports that it is discarding input events. The counters cover events delivered to the Input System event callback; they are diagnostic, not a guarantee that events discarded before callback delivery are counted. The configured budget is not raised automatically beyond the existing 1 MiB default.

Opening a modal menu, chat, or minigame releases mouse capture and blocks character movement/jump input until the overlay is closed.
## Implementation

- ClientInput.cs owns the action map and is shared by the controller, camera, interaction probe and CoreGameplay.
- Movement remains camera-relative and server-authoritative; this migration changes only local device input.
- Mouse look remains independent of RMB. RMB remains a contextual guard/gesture input.
- No wire contract, server movement, request IDs, or gameplay resolution changed.
- Context-specific Action Maps, rebinding UI, gamepad presets, accessibility and per-minigame maps are follow-up work. Do not duplicate input polling in gameplay scripts; add semantic actions to ClientInput.
- Device-level raw event diagnostics subscribe to `InputSystem.onEvent` only while F3 is visible, aggregate the most recent processed frame in a bounded six-device summary, and unsubscribe on destruction. They do not change the input budget or filter device events.

Unity Editor/PlayMode validation is still required after the owner pulls the branch.
