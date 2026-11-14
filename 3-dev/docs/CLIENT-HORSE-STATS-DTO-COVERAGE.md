# Client DTO coverage — stats, carry bag and horse

Date: 2026-11-14  
Current status: server view audit complete; Unity DTO scaffolding exists but is not wired into WorldFrame/CoreGameplay.

## Server-owned public wire views

| Wire path | Server builder | Shape | Notes |
|---|---|---|---|
| `me.stats` | `sim.StatsView(e)` | object with `strength/agility/endurance/intellect/perception/will:number` | All six values rounded to one decimal; server derives and persists progression |
| `me.bag` | `sim.BagView(e)` | `{load:number, cap:number, over:boolean}` | This is the player's bulk-goods burden summary, not a physical backpack/container |
| `me.horse` | `sim.HorseView(w,e)` | null or `{id:string,name:string,stam:number,mounted:boolean,here:boolean,bag:map<string,number>,bag_load:number,bag_cap:number}` | Horse is a server entity; saddlebag is distinct from player stash and carried inventory |

## Existing client scaffold — not yet complete

`2-client/game/Assets/_Project/Scripts/Networking/Protocol/HorseStatsFrames.cs` currently defines:
- `StatsFrame` and `StatsFrameParser`;
- `CarryBagFrame` and `CarryBagFrameParser`;
- `HorseFrame` and `HorseFrameParser`.

However, at this audit:
- `CharacterFrame` does not expose these properties;
- `WorldFrameParser` does not call these validators;
- `CoreGameplay` still reads stats, bag and horse through JObject selectors;
- there is no dedicated pure C# regression runner for these parsers. The dedicated parser regression suite passed in run #43638732127; wire integration and parser behavior remain unverified.

Therefore these classes are scaffolding only; do not count these wire sections as migrated or runtime-verified. In particular, `CarryBagFrameParser.Validate` currently expects an object; integration must handle absent/null old-v1 fields deliberately.

## Current CoreGameplay consumers

- Character sheet and character menu read all six `me.stats` values.
- Inventory panel reads `me.bag.load/cap` and `me.horse.bag` quantities.
- Horse menu reads `id/name/stam/mounted/here/bag_load/bag_cap`.
- `UpdateMount` and `ApplyRiderPose` use `me.horse.mounted` to drive presentation.

## Critical semantic distinction

`me.bag` is not a physical backpack. It is `BagView` over `StashC.Goods`: load is the sum of bulk-goods quantities and capacity derives from strength. `me.horse.bag` is a separate saddlebag map on the server horse entity. Do not conflate either with `InventoryC.Items`, worn equipment, room chest `ContainerC`, or a future physical backpack.

The server already owns purchase/mount/dismount/feed/stow/unstow outcomes. Unity must display confirmed values and send existing `op:"horse"` intentions only. Do not locally change horse ownership, stamina, bag quantities or movement speed.

## Next safe implementation slice

1. Add `StatsFrame`, `CarryBagFrame`, `HorseFrame` properties to `CharacterFrame`.
2. Wire validators into full `WorldFrameParser`; these sections are absent from current fastJSON, so do not invent fast patch fields.
3. Preserve missing/null semantics for old v1 frames.
4. Migrate one bounded read-only consumer at a time: stats display, then bag/horse menu, then mount presentation.
5. Add pure C# tests for numeric bounds, null/absent horse, saddlebag maps and overloaded burden.
6. Keep physical backpack/container work separate; it requires a server contract and migration plan, not reinterpretation of `me.bag`.

No server change is requested by this DTO slice.
