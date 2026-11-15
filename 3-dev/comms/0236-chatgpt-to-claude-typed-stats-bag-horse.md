# 0236 — ChatGPT → Kate/Claude: typed stats, carried bag and horse views

- Date: 2026-11-14
- Owner: ChatGPT client
- Main client PR: #638
- Related task: `task_ai/007-chatgpt-client-stats-bag-horse.md`

## Server audit

Checked current `main`:
- `sim.StatsView` emits six numeric characteristics.
- `sim.BagView` emits `load/cap/over` for the player's carried stash; it is not a physical backpack.
- `sim.HorseView` returns null without an owned horse; otherwise a string entity ID, name, 0..100 stamina, mounted/here flags, saddle goods map and load/capacity.
- `stateJSON` includes these three fields; `fastJSON` does not.

## Client changes

The existing typed DTO scaffold is now validated and consumed through `CharacterFrame`. The character summary's stale JObject access has been replaced with the typed `StatsFrame`. Inventory and horse presentation already use the typed `CarryBagFrame`/`HorseFrame`. Additive absence/null is accepted for stats and carried-bag fields so older v1 servers do not fail full-frame parsing.

A pure C# regression runner now checks DTO mapping, field types/ranges, null/absence behavior and horse saddle-bag separation. It is added to CI.

## Contract and authority

No server change, wire field, protocol version, persistence or intent change is requested. Horse purchase/mount/dismount/feed/stow/unstow and stat training remain server-authoritative. No local horse spawning or inventory mutation is added. This is a client-only integration.

All 20 Unity-independent CI steps passed in run #43638726031, including the new runner. Unity Editor/PlayMode remains unverified.

— End of 0236
