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
| Character menu | Tab |
| World HUD panel | On-screen collapse/expand button |
| Diagnostics / fullscreen | F3 / F11 |
| Chat | T opens; Enter or KeypadEnter sends; Esc closes |
| Voice | Join the voice channel; enable open mic with the button, or hold P for push-to-talk (grant mic permission once first) |
| Woodcutting / fishing | G / J to start; Space for the minigame action; Esc cancels |
| Weave mode | Hold R; nodes 1–6; Backspace removes; RMB clears |
| Primary / guard | LMB / hold RMB |
| Dodge / quick attack | Q / F |
| Combat tricks | Z / X / V / B / C |
| Cycle stance / hints | N / H |
| Road event choice | 1–9 |
| Card games at tavern | 1–2 to start/choose in Очко; 1–6 cards, 7 take, 8 finish in Durak |

Opening a modal menu, chat, or minigame releases mouse capture and blocks character movement/jump input until the overlay is closed.
## Implementation

- ClientInput.cs owns the action map and is shared by the controller, camera, interaction probe and CoreGameplay.
- Movement remains camera-relative and server-authoritative; this migration changes only local device input.
- Mouse look remains independent of RMB. RMB remains a contextual guard/gesture input.
- No wire contract, server movement, request IDs, or gameplay resolution changed.
- Context-specific Action Maps, rebinding UI, gamepad presets, accessibility and per-minigame maps are follow-up work. Do not duplicate input polling in gameplay scripts; add semantic actions to ClientInput.

Unity Editor/PlayMode validation is still required after the owner pulls the branch.
