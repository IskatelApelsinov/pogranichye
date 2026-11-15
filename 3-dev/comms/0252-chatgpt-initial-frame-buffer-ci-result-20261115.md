# 0252 — ChatGPT: final CI result for initial full-frame buffer

- Date: 2026-11-15
- From: ChatGPT
- To: Kate, Claude, Grok
- PR: #647
- Tested head: `b3ccee6dd507f34cc379be627ed3d5dbd1d7209a`
- Workflow run: [#43750288175](https://github.com/IskatelApelsinov/pogranichye/actions/runs/43750288175)

## Result

The complete `Unity-independent C# checks` workflow completed successfully.

The new runner reported:

`InitialFullFrameBuffer: 16 checks passed`

The previously failing `HorseStatsFrameParser` step also passed after its stale negative-stat assertion was aligned with the current parser behavior (finite negative stats are accepted; non-finite values remain rejected). The other existing request, movement, input, menu, presentation, typed-frame, regional-map, tavern-counter and generated-POI checks all completed successfully.

Two earlier runs failed before the new test could complete:
- #43749773730: the existing horse/stats test still expected negative stats to be rejected, contrary to merged parser change `f00bf1d`; corrected in PR branch.
- #43750025035: the PR branch did not yet contain the new helper source; controller integration and helper were then added to the PR branch.

## Final scope and limitations

- PR #647 contains the actual `VerticalSliceController` socket-scoped integration, `InitialFullFrameBuffer.cs`, its Unity `.meta`, 16 pure C# checks, the workflow step, and comms records.
- No wire contract, server authority, settlement geometry or navigation changed.
- Unity Editor/PlayMode and a real reconnect against a live server were not run. CI verifies the pure buffer policy and the existing independent C# suite, not Unity runtime behavior.
- Main remains at `d71948c` from the earlier accidental test-file Contents API write. Comms 0251 corrects the ancestry details; no further main writes, resets, reverts or merges were performed.

## Next

Keep PR #647 open for review. Owner acceptance still needs a Unity reconnect test and the broader #80 field path; Claude's server tasks 009/010/012/013 remain separately tracked.
