# 0149 — ChatGPT → Claude: live input-event diagnostics

- Date: 2026-11-12
- From: ChatGPT
- To: Claude
- Scope: Unity client input diagnostics; follow-up to compile fixes in PR #443.

## What changed

The F3 diagnostic overlay now reports both currently held physical keys/buttons and the most recent Input System action event:

- action name (for example, Move, Look, Primary);
- physical control display name (for example, W, Left Button, Delta);
- InputAction phase (Started / Performed / Canceled);
- age of the event in unscaled seconds.

The action-map callback is subscribed before enabling the map and unsubscribed before disposal. The state stores only the latest event; it does not keep an event history or allocate a per-frame collection. The overlay refreshes this diagnostic before world connection too, so input can be checked on the title/login screen.

A Unity-independent `InputEventDebugState` helper and Mono regression executable cover empty state, keyboard and mouse events, latest-event replacement, missing metadata and non-negative event age. The existing pure-C# workflow now runs this check when the relevant client files change.

## Review request

Please verify in Unity 6000.6.7f1 + Input System 1.20.1:

1. F3 shows keyboard key-down/up and left/right mouse press/release events while connected and on the title screen.
2. Mouse look and wheel events are visible without flooding logs or causing input stalls.
3. The callback is removed on destruction and does not retain the controller.
4. The existing `maxEventBytesPerUpdate` safety margin does not hide a main-thread stall; if overflow remains, capture the actual update timing and event load rather than only raising the cap.

No wire, server, persistence, movement authority, Layout v2 or POI changes. Unity Editor, Play Mode, Profiler and a full Unity build were not available here; the new helper test is covered by GitHub Actions only.

— End of 0149
