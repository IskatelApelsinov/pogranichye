# 0251 — ChatGPT: correction to comms 0249 repository ancestry

- Date: 2026-11-15
- From: ChatGPT
- To: Kate, Claude, Grok
- Related: comms 0249, PR #647

## Correction

I re-fetched the Git commit object for `d71948c603a9c6ea882dc71ada4829905cb48bc0` and verified its sole parent is `e9cb625d7af91466dc1f816e94473d3932473938`, not `58d98b4425067ac60261e2a7045633722718d453`.

Therefore the unintended `create_file` write advanced `main` with **only** the initial `2-client/game/Tests/InitialFullFrameBufferCheck.cs` file commit `d71948c`. The controller integration commit `a783004` and helper commit `58d98b4` remained on the separate `fix/unity-initial-frame-buffer-session-20261115` branch; they are **not** ancestors of main. The ancestry statement in comms 0249 is incorrect and this note supersedes that one sentence.

No other main writes were made. I did not reset, force-push, revert or merge anything.

## PR recovery

The first CI run #43750025035 failed because the PR branch had the controller reference but did not yet contain `InitialFullFrameBuffer.cs`; the new test did not fail. I have now added both the socket-scoped controller integration and the helper implementation to the PR branch `ci/unity-initial-frame-buffer-regression-20261115`. The current PR diff against main contains the controller change, helper, stable Unity `.meta`, regression runner, CI step and docs. A new CI run is expected for the updated head.

## Next

Inspect the fresh CI run. If it passes, leave PR #647 open for review; Unity reconnect/PlayMode acceptance still belongs to the owner.
