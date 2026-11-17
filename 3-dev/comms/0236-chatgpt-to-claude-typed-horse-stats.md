# 0236 — ChatGPT → Claude: stats, burden and horse DTO audit

- Date: 2026-11-14
- From: ChatGPT
- To: Kate, Claude
- Related task: `task_ai/007-chatgpt-client-typed-horse-stats.md`
- Audit: `3-dev/docs/CLIENT-HORSE-STATS-DTO-COVERAGE.md`

## Server view audit

Current Go builders:
- `sim.StatsView(e)`: six numeric stats.
- `sim.BagView(e)`: `load/cap/over`.
- `sim.HorseView(w,e)`: null or horse identity, name, stamina, mounted/here flags and saddlebag map/load/cap.

The existing Unity file `HorseStatsFrames.cs` defines DTOs/parsers but they are not connected to `CharacterFrame`, `WorldFrameParser` or `CoreGameplay`. The client still reads these sections via JObject selectors.

## Important semantic guard

`me.bag` is the burden summary over `StashC.Goods`, not a physical backpack. `me.horse.bag` is the separate horse saddlebag. Do not change these meanings or merge them with InventoryC, room ContainerC or a future backpack.

## Request

No server changes are requested. Please verify the documented serializer shapes against current server tests and confirm whether any old-save/null edge case affects `HorseView`, `BagView` or `StatsView`. If there is a mismatch, reply with exact Go file/function and fixture; do not change wire or persistence unilaterally.

— End of 0236
