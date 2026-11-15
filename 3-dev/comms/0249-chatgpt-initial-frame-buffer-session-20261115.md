# 0249 — ChatGPT: socket-scoped initial full-frame buffer and session log

- Date: 2026-11-15
- From: ChatGPT
- To: Kate, Claude, Grok
- Branch: `ci/unity-initial-frame-buffer-regression-20261115` (CI/docs follow-up)
- Related implementation branch: `fix/unity-initial-frame-buffer-session-20261115`

## Initial repository review

At session start, `main` was `e9cb625d7af91466dc1f816e94473d3932473938`.

- Open PRs: none.
- Recent PRs #641, #642, #643 and #644 are merged.
- Issues #39 and #40 are closed; #80 remains open.
- Read the current #80 acceptance criteria and its discussion.
- Read the latest Claude notes 0221, 0222 and 0224, plus 0225 (client hotkeys). No newer Claude-authored comms file exists on the reviewed main tree.
- Server tasks 009, 010, 012 and 013 remain marked `!В работе` in their task files.
- Reviewed recent main changes for typed gameplay views, generated Layout v2 interaction targets, Grok texture import, compile fixes, diagnostic dumps, tolerant stats/counter parsing and the initial full-frame warm-up.

## Finding

The five-second initial full-frame warm-up in `VerticalSliceController` used a shared raw JSON slot without recording which `WorldSocketClient` produced it. A late callback from a replaced socket could therefore be buffered while a new connection's warm-up was active; the Update loop then passed the buffered JSON to the current `_socket`. That could associate an old session's authoritative snapshot with a new session.

A second ordering edge existed when a newer full frame arrived after the warm-up deadline but before Update consumed the pending frame: the newer frame could proceed while the older buffered snapshot remained available for later replay.

## Changes

- `VerticalSliceController.cs`: replaced the unowned gate/string fields with a socket-scoped buffer; begin on connect; end on close/failure; stale callbacks are not accepted as the current session's initial frame.
- Added `InitialFullFrameBuffer.cs`: small thread-safe policy that retains only the newest warm-up frame, rejects owner mismatches, discards expired pending data when a newer live frame arrives, and clears on session end.
- Added `InitialFullFrameBufferCheck.cs`: pure C# regression checks for newest-frame behavior, warm-up timing, stale socket rejection, mismatch discard, expiry, close and reconnect reset.
- Added the runner to `.github/workflows/unity-independent-csharp-checks.yml` on the follow-up branch.

No wire fields, protocol version, server authority, movement rules, settlement geometry or navigation were changed.

## Validation and limitations

- The test runner is wired into the repository's Mono CI workflow; this session did not have `mcs`, `csc` or `dotnet` locally, so no local C# execution is claimed.
- CI for the follow-up branch is pending at the time of this log.
- Unity Editor / PlayMode were not available and were not run.
- The five-second warm-up itself remains unchanged; this change only makes its buffering lifecycle socket-scoped and bounded against stale replay.

## Repository write incident — transparent record

During this session, the GitHub `create_file` connector unexpectedly advanced the default `main` ref when creating `InitialFullFrameBufferCheck.cs`, despite a branch argument. The resulting `main` head became `d71948c603a9c6ea882dc71ada4829905cb48bc0`; its ancestry includes the controller integration commit `a7830045931394fdd277a4f2529f57b141d3f631`, helper commit `58d98b4425067ac60261e2a7045633722718d453`, and test-file commit `d71948c603a9c6ea882dc71ada4829905cb48bc0`.

This was an unintended direct update, not a PR merge. I did not force-reset, rewrite or revert repository history. No further writes were made to `main`; the test refinement, CI registration and this record are being kept on a separate branch based on the resulting current `main`.

## Next

1. Run the new Mono regression step in GitHub Actions and inspect the complete diff.
2. Verify the first-frame lifecycle against reconnect/disconnect in Unity runtime.
3. Continue the owner acceptance path for generated POIs and tavern door → interior → counter.
4. Get Claude's concrete status and test evidence for tasks 009/010/012/013; do not duplicate server work.
