!В работе
# 007 — Complete typed stats, carried burden and horse views

**Owner:** ChatGPT  
**Source scaffold:** `2-client/game/Assets/_Project/Scripts/Networking/Protocol/HorseStatsFrames.cs`  
**Related audit:** `3-dev/docs/CLIENT-HORSE-STATS-DTO-COVERAGE.md`

## Goal

Finish the existing DTO scaffold for `me.stats`, `me.bag`, and `me.horse`, then migrate read-only CoreGameplay consumers without changing the server contract.

## Required work

1. Add typed properties to `CharacterFrame`.
2. Wire strict full-frame validation in `WorldFrameParser`.
3. Decide absent/null compatibility for older v1 snapshots; do not materialize defaults as server truth.
4. Add pure C# parser tests and full-frame Editor fixture cases.
5. Migrate stats display to typed values.
6. Migrate burden/saddlebag/horse menu to typed values.
7. Migrate `UpdateMount` and rider pose reads to typed `HorseFrame`.
8. Do not add these fields to fast frames; current `fastJSON` does not carry them.

## Authority and semantic constraints

- Server owns stats progression, burden, horse identity/ownership, stamina, mounting and saddlebag contents.
- `me.bag` is the `StashC.Goods` burden summary, not a physical backpack.
- `me.horse.bag` is a distinct saddlebag map.
- Do not conflate `InventoryC.Items`, `StashC.Goods`, `HorseC.Bag`, room chest `ContainerC`, purse and a future physical backpack.
- Do not locally mutate inventory, stamina, horse ownership, speed or action outcomes.
- Physical backpack/container functionality remains a separate server-contract task.

## Validation

- Source audit completed; typed properties/consumer migration not yet complete.
- Unity compile/Editor/PlayMode not run for this future slice.
