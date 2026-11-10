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
| Sprint / jump | Shift / Space |
| Interact | E |
| Toggle HUD / diagnostics / fullscreen | F1 / F3 / F11 |
| Character / game menu | Tab; close with Escape |
| Weave mode | Hold R; nodes 1–6; Backspace removes; RMB clears |
| Primary / guard | LMB / hold RMB |
| Dodge / quick attack | Q / F |
| Combat tricks | Z / X / V / B / C |
| Cycle stance / hints | T / H |
| Road event choice | 1–9 |

## Implementation

- ClientInput.cs owns the action map and is shared by the controller, camera, interaction probe and CoreGameplay.
- Movement remains camera-relative and server-authoritative; this migration changes only local device input.
- Mouse look remains independent of RMB. RMB remains a contextual guard/gesture input.
- No wire contract, server movement, request IDs, or gameplay resolution changed.
- Context-specific Action Maps, rebinding UI, gamepad presets, accessibility and per-minigame maps are follow-up work. Do not duplicate input polling in gameplay scripts; add semantic actions to ClientInput.

Unity Editor/PlayMode validation is still required after the owner pulls the branch.
