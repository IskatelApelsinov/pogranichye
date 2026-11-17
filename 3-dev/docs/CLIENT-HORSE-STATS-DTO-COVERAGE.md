# Client DTO coverage — stats, carry bag and multi-horse roster

Date: 2026-11-18  
Status: typed snapshot integration is implemented; Unity Editor/runtime acceptance remains required.

## Server-owned public wire views

| Wire path | Server view | Shape | Client handling |
|---|---|---|---|
| `me.stats` | `sim.StatsView(e)` | six numeric attributes | typed `StatsFrame` |
| `me.bag` | `sim.BagView(e)` | `load/cap/over` | typed carry-bag summary |
| `me.horse` | active horse compatibility view | nullable horse object | typed `HorseFrame`, drives current mount presentation |
| `me.horses[]` | v1.32 owned-horse roster | array of horse objects, including active/location metadata | typed `List<HorseFrame>`, stable UI and target-specific actions |

Each horse has a stable string `id`, name, stamina, mounted/here flags, separate saddlebag contents/load/capacity, and additive `active`, `settlement_id`, `settlement_name`, `realm_name` fields. Older v:1 servers may omit `me.horses`; the client keeps the prior `me.horse` view as a one-horse fallback.

## Client integration

- `CharacterFrame` exposes `Stats`, `Bag`, `Horse` and `Horses`.
- `WorldFrameParser` validates the active horse and each optional roster entry. Malformed roster containers/entries are rejected; absent/null roster remains backward-compatible.
- The stable UI lists all owned horses, location/realm and saddlebag state; it can select a horse and sends its stable ID as `target` for mount/dismount/feed/stow/unstow. The server remains authoritative.
- `me.horse` remains the active horse used by the existing mount/rider presentation and inventory compatibility view.
- `HorseStatsFrameParserCheck` covers multi-horse arrays, stable IDs, location metadata, missing/null compatibility and malformed roster values.

## Semantic constraints

`me.bag` is not a physical backpack. It is the player's bulk-goods burden summary. `me.horse.bag` is a separate saddlebag map on the server horse entity. Do not conflate either with `InventoryC.Items`, worn equipment, room chest `ContainerC`, or a future physical backpack.

Unity must never locally change horse ownership, stamina, location, saddlebag quantities or movement speed. Every horse operation uses the shared RequestGate and server `reqid/me.result`.

## Validation boundary

The pure-C# horse parser regression suite is part of Unity-independent CI. Unity Editor compilation, actual stable interaction, horse switching, mounting, reconnect and multi-client runtime behavior still require testing in Unity 6000.6.7f1 against the server.
