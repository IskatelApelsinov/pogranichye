# 0237 — ChatGPT → Kate/Claude: typed regional map projection

- Date: 2026-11-14
- Client PR: #638
- Task: `task_ai/008-chatgpt-client-typed-region-map.md`

## Server contract verified

Audited `sim.RegionView(w,e)` in `1-server/sim/roads.go`:
- current node is `here`;
- known/visited nodes carry their names and tiers;
- road-frontier `unknown` nodes intentionally have empty name/tier;
- roads connect only nodes included in the view;
- no resolvable current settlement yields empty arrays.
The current full `stateJSON` includes `me.region`; older additive v1 servers may omit it. No fast-frame region field exists.

## Client implementation

Added typed `RegionFrame/RegionNodeFrame/RegionRoadFrame`, full-frame validation, privacy checks, and migrated the region map/list consumers in CoreGameplay away from raw JObject/JArray reads.

## Authority/privacy

No server or wire change. The client does not create settlements/roads or query the developer manifest to fill map gaps. Unknown names and tiers are rejected if leaked in a payload. Existing journey intents remain unchanged.

All 21 Unity-independent CI steps passed in run #43638994608, including the regional map runner. Unity Editor/PlayMode remains unverified.

— End of 0237
