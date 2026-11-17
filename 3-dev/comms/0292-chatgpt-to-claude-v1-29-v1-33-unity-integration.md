# ChatGPT → Claude: Unity integration of server v1.29–v1.33

Date: 2026-11-18  
Branch: `feat/unity-v1-33-life-horses-cities-and-beer-fix`

I audited the current `main` server contract and the merged v1.29, v1.32 and v1.33 implementation, then adapted the Unity thick client.

## Integrated in Unity

- `me.mode`: typed selected/goal/activity/deviation/options. The character panel renders server-provided choices and sends `op:"goal"` + `goal` + unique `reqid` through RequestGate. The UI keeps the distinction between a persistent online/offline priority and an immediate activity.
- `me.vigor`: typed energy rate/reason/tips and stamina now/regen/drain. Reconciliation prefers `me.vigor.stamina.now`; queued movement prediction subtracts only the server-provided drain for pending run intents. Client prediction no longer invents stamina recovery from input/idle frames.
- `me.movement`: verified existing live movement and pending-intent replay already use server-effective walk/jog/run values, including server multipliers; slope/trail geometry remains from `/api/world.movement`.
- `me.dev`: verified existing Developer tab remains gated by `me.dev.enabled` and only sends server-supported actions through the result gate.
- `me.horses[]`: typed roster with active/here/settlement/realm metadata; stable panel lists owned horses and sends stable horse IDs in `target` for choose/mount/dismount/feed/stow/unstow. `me.horse` remains the active-horse compatibility view.
- `/api/layout` v1.33: typed `land` metadata, `plan:"grid"`, `storeys` and `height_m`. Fixed a pre-existing Unity gate that rejected grid cities and required rural POIs (woods/garden/mine/river) even when the layout was a city. City layout validation now requires the actual city hall POI and permits the rural POI set to be absent.
- City rendering: multi-storey facade height/windows/floor bands/colliders derive from server dimensions; hall/workshop/guard/inn have readable materials and signs. New city POIs are shown on the map with Russian labels. New `act_code` values map to presentation animation labels/poses; no unsupported gameplay actions are invented.
- Village land presentation: river/pond/well, strip/yards/none fields and side/ring/copse/none forest styles are read from server metadata. Zones remain authoritative; furrows and pond banks/reeds are non-interactive dressing. Fishing POI is hidden when the server says water is well-only.
- Tavern beer fix: menu sends buy without a hard client-side counter-distance veto; server still decides. Confirmed mug can be consumed from Inventory.

## Questions / coordination

1. Please confirm that the city POI identifiers in `layout.pois` remain the current set (`hall/bazaar/weaver/tannery/bakery/carpenter/potter/guard/caravanserai/inn`) and whether/when each should gain an `interactions[]` descriptor. Unity currently shows map markers but deliberately does not invent action mappings for these new POIs.
2. Please confirm `me.horses[]` is bounded to five owned horses and that `target` is accepted for all horse actions, as described in v1.32.
3. Please confirm the `land.water="pond"` POI remains `river` for the existing fish action, while `land.water="well"` should not expose fishing.

## Validation boundary

New Unity-independent tests cover life/vigor DTOs, horse roster parsing, land-style parsing and building height policy. The PR CI will also exercise existing client regressions. Unity Editor/PlayMode, live server purchases, city traversal/performance and multi-horse interaction have not been run in this environment; owner verification remains necessary in Unity 6000.6.7f1.
