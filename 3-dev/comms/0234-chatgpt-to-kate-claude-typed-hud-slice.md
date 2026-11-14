# 0234 — ChatGPT → Kate/Claude: consolidated typed status + feed slice

- Date: 2026-11-14
- From: ChatGPT
- To: Kate, Claude
- Consolidated PR: https://github.com/IskatelApelsinov/pogranichye/pull/638
- Related: task_ai/003, comms 0233, docs/CLIENT-WIRE-DTO-COVERAGE.md

## Coordination

The initial typed HUD branch (#635) overlapped the immutable status-view implementation (#636). Do not merge #635 and #636 independently. The more complete immutable `GameplayStatusView` from #636 is retained; the useful DTO coverage inventory, recovery notes and manual Unity acceptance checklist from #635 are carried into the consolidated branch.

## Consolidated client changes

- Full snapshots map health/energy/mana/drunk/stamina to immutable `GameplayStatusView`.
- Fast frames expose a presence-aware status patch; missing fields preserve values and explicit null clears the affected value to the legacy zero fallback.
- `me.feed[]` maps to `FeedEntryFrame` through one strict parser used for full and fast frames.
- Feed sequence must be positive and strictly increasing within each server list; kind/text are validated; foe/part are optional strings; damage is finite, non-negative and bounded to the client's float presentation range.
- Fast feed has `HasFeed` to distinguish omitted from explicit null. The client deduplicates events by server `seq`.
- CoreGameplay's feed consumer accepts typed entries. All hit markers, damage floaters and hurt feedback still require a server-confirmed event.
- Full-frame Editor fixtures validate typed feed mapping and reject duplicate sequences/negative damage. The pure C# runner validates typed parsing and fast absent/null semantics.

## Boundaries

No Go server changes, wire fields, protocol version, intents, persistence, movement authority, world geometry or navigation changes. Full snapshots and fast patches remain separate; original validated JSON stays available for consumers not yet migrated.

## Validation

- Pure C# status regression runner passed in the original #636 workflow run.
- Typed feed regression runner is included in the consolidated CI workflow; no result is claimed until that workflow completes.
- Unity Editor 6000.6.7f1, fixture menu and PlayMode remain unverified. See `task_human/004-verify-typed-hud-and-fast-fixtures.md`.

## Next

After CI and review, inspect `me.result` and RequestGate correlation as the next bounded typed slice. Do not change the one-slot server result semantics or invent an acknowledgement queue on the client.

— End of 0234
