# 0066 — ChatGPT → Claude: merged client progress and next Outward-style implementation

Date: 2026-11-10
From: ChatGPT
To: Claude / Fable 5.2 and project owner
Subject: Current main state, screenshot-grounded client priorities, and implementation handoff

## Main now includes

- PR #233: Unity Input System migration. The thick client now uses com.unity.inputsystem 1.20.1 through the shared ClientInput semantic action component. Movement, camera look/zoom, interaction, combat, weave, pointer position and hotkeys no longer poll UnityEngine.Input.
- PR #229: thin-client crafting shows actual ingredient/output stock from v1.11 snapshot data and only confirms results after matching server reqid.
- PR #231: thin-client live village map now uses the existing atlas sprites and server positions for player/NPC/foe markers. Decorative trails are explicitly not a navigation graph.
- PR #234: movement send queue is capped at eight; when the user releases movement, stale unsent movement and corresponding prediction entries are removed while the newest zero-vector stop intent is preserved behind the one in-flight packet. F3 shows queue and discarded count. Focus loss releases held attack/guard/fishing/weave state. Pure C# regression suites for RequestGate and movement policy pass in GitHub Actions.
- PR #235: thin-client village map supports 1×–4× zoom, wheel/buttons, drag/touch pan, pointer-anchored zoom, reset-to-full-region and correct inverse coordinate mapping for POI/foe clicks.

No server runtime, persistence, DTO, wire v1.11, Layout v2, geometry or navigation contract was changed by these PRs.

## Owner's visual reference

The owner supplied a 2728×2352 collage of Unity Game and Scene views in chat. It shows:
- The current starter village as a low-poly 3D scene with timber buildings, paths, fenced gardens, trees, river/bridge, well and NPCs.
- A tavern/interior view whose first-person framing is too close and whose props/spatial readability need improvement.
- Some building/sign labels look mirrored or face the wrong way in the current camera view; distinguish this from the already-fixed NPC billboard orientation.
- NPCs and carried tools still need stronger visual identity, silhouettes, and role/activity animation.
- The Unity HUD remains prototype IMGUI with a large left panel, technical text, status bars, dialogue options and a tiny local map.
- The new thin-client atlas map improves the 2D village view, but it is still one 300×300 m settlement overview, not a multi-settlement regional map or the world map.

The original image is attached to the owner's conversation, not committed as a binary Git asset in this handoff. Do not claim that the image file itself is in Git.

## Next implementation priorities

### A. Unity physical item handling — start with a small complete vertical slice

1. Inspect the current v1.11 player/NPC inventory and worn fields; do not invent new wire fields.
2. Make the equipped hand item visibly held by the player in first-person and third-person views. Begin with one sword/axe/rod/torch and make attachment transforms/orientation consistent.
3. Ensure held vs worn vs sheathed vs packed is unambiguous. Appearance must be driven by the confirmed snapshot, never by a speculative local equip action.
4. Add draw/stow and idle/working/attack/block presentation states using existing act_code and confirmed equipment.
5. Add one world-space item interaction and animation loop that ends only after server confirmation. Avoid placeholder button spam.

### B. Maps and travel UX — use truthful geography

1. The local 300×300 m map now zooms/pans; next add readable scale, compass, player-facing cone, selected POI detail and known/unknown marker states where current data supports them.
2. Build a separate regional map showing the cluster/"cauldron" of starter villages only from real server world/known_places data and actual connected routes.
3. Keep the distant world fogged/unknown until revealed by Rumoured → Located → Visited → Personally known. Never show hidden settlement names or routes as discovered.
4. Explain travel as a chain: current village → road/junction → destination, with route choice and server-backed travel state. Adjacent villages are continuous physical walking along connected roads, not teleportation and not op:travel.
5. Layout v2 currently describes only the current 300×300 m settlement and has no authoritative navgraph. The thin-client decorative trails are visual only. Do not present them as actual routes or build a regional route graph from guesses.
6. If current v1.11 world/known_places data is insufficient for the desired regional map, draft an additive contract proposal first; do not silently change snapshots, fast frames, /api/layout, geometry version or persistence.

### C. Horse/stable and backpack — design against current server truth

- A horse must be a persistent server-owned entity/state, not a client-only mount. Stable POI, ownership, mount/dismount, stamina, gait, collision and saddlebag need an authoritative server loop.
- Backpack should be a real container/capacity/burden concept, separate from worn equipment, belt/quick slots, storage and saddlebag. First inspect existing inventory schema and item semantics; do not invent capacity/weight from client-only values.
- Deliver one small reliable loop (stable interaction or backpack management) only after server data/actions exist. Otherwise submit a narrowly scoped server contract proposal.

### D. Interface and minigames

- Replace the most obtrusive IMGUI surfaces incrementally; do not rewrite the whole HUD at once.
- Add proper input context/focus transitions for exploration, UI, combat, weaving, mounted state and minigames on top of ClientInput. Do not introduce a second input poller.
- Minigames need visible world-space props and animation timing; server owns success, rewards, consumption and skill progression.

## Coordination and boundaries

- ChatGPT owns Unity/presentation/assets and thin-client presentation where useful; Claude owns Go/server. Coordinate cross-layer work through numbered comms.
- Keep at least two-thirds of effort on implementation, tests and integration, not repeated audits.
- Do not wait for Unity Editor availability to continue independent work. Do not claim Unity Editor/PlayMode validation unless actually run.
- Keep wire v1.11 and server authority intact unless an explicit contract proposal is reviewed.
- Owner wants safe changes merged promptly, and will test Unity locally.
- Please read the full 0063 master context and current relevant source before changing any gameplay or world semantics.
