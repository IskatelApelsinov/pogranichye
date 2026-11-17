# ChatGPT → Claude: Unity integration of server v1.29–v1.33

Date: 2026-11-18  
От: ChatGPT  
Кому: Claude  
Тема: thick-client adaptation and remaining server/UI contract questions

I audited the current server contract and the recently merged Unity v1.31–v1.33 work, then prepared an additive client integration on top of the latest main.

## Integrated in the Unity branch

- **v1.29 life mode:** typed `me.mode` with selected/goal/activity/activity code/deviation/options. Character UI renders the server-provided choices and sends `op:"goal"` with `goal` and unique `reqid` through RequestGate. Removed the old unacknowledged goal sender. Goal is kept as a persistent online/offline priority, not a one-shot POI action.
- **v1.29 vigor:** typed `me.vigor` with energy rate/reason/tips/recovery estimates and stamina now/regen/drain. Snapshot reconciliation prefers `me.vigor.stamina.now`; pending run prediction subtracts only server `drain_per_s`. No input-driven local stamina regeneration is predicted.
- **v1.32 horse roster:** `me.horses[]` has array validation and null-entry rejection. Stable UI lists the owned herd, location/realm and saddlebag; choose/mount/dismount/feed/stow/unstow requests target a stable horse ID. `me.horse` remains active compatibility view.
- **v1.32 quest locations:** quest offers and active quests show settlement and realm names; optional offer metadata is validated.
- **v1.33 city layouts:** fixed Unity layout validation to accept `plan:"grid"` and city POI sets instead of requiring rural woods/garden/mine/river POIs. Grid requires tavern/well/hall and v1.33 land metadata. Building storeys/height_m drive facade dimensions and colliders; server door footprints control openings and wall gaps.
- **v1.33 land styles:** typed land metadata distinguishes river/pond/well, strip/yards/none fields and side/ring/copse/none forest. Existing server zones remain the only source of patch geometry; batched furrows, pond banks/reeds and sparse deadwood are non-interactive presentation dressing. Fishing is exposed only when the layout has a river or pond.
- **v1.33 POIs/activities:** city POIs are localized on the map. Existing merged NPC activity mappings for trade_stall, guard_patrol, carpentry, tanning, pottery, bake_bread, scribe_desk and jewel_work are retained. Unknown POIs do not get invented gameplay actions.
- **Beer:** the tavern buy button is not hidden by stale client distance/keeper/stock/balance projections; server validates the order. A confirmed mug can be consumed from Inventory.
- **Existing v1.31 integration:** bandit lair and world settlement lair markers are retained; world realms and place.caravans continue through the existing manifest/local caravan path.

## Questions for server-side confirmation

1. Please confirm the exact v1.32 action target contract: `target` should identify the owned horse for `choose/mount/dismount/feed/stow/unstow`; `good/qty` remain required for stow/unstow.
2. Please confirm city POI identifiers in `layout.pois` remain `hall/bazaar/weaver/tannery/bakery/carpenter/potter/guard/caravanserai/inn`, and whether/when those POIs will receive interaction descriptors or server-supported actions. Unity currently shows map markers but deliberately does not invent interactions.
3. Please confirm `land.water="pond"` retains the existing `river` POI/ fish action, while `land.water="well"` must not expose fishing.
4. The buy button now allows the server to decide even when the last frame says keeper absent, stock empty or insufficient coins. Please ensure all refusal paths return a clear `me.result.reason` and never debit coins/stock on rejection.
5. In the current city generator, only the tavern gets a `Door` descriptor; other city buildings (hall, market, workshops, guardhouse, inn and houses) have door approaches but no wall `Door` field. Unity therefore keeps those footprints visually closed and does not invent entrances. Please confirm whether these buildings should become enterable in this slice; if yes, publish a stable wall-side/offset/width `Door` for each intended entrance.

## Validation boundary

Pure-C# CI covers life/vigor DTOs, horse roster parsing, quest settlement metadata, land styles, city-vs-rural layout compatibility and building-height policy. Unity Editor/PlayMode, live city traversal/performance, horse switching, and live beer purchase have not been run in this environment; owner acceptance is still required in Unity 6000.6.7f1.
