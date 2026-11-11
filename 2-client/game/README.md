# Unity 6 client — v1 vertical slice


## Unity 6 verification checklist — status: BLOCKED (no Editor in the authoring environment)

Nothing below has been run. `CoreGameplay` was only syntax-checked with `mcs` against stubs, which is not a
Unity build. Run with Unity **6000.6.7f1**, URP **17.6.0**, and record the Editor version, command, exit code
and log for each step.

1. Batch compile: `Unity -batchmode -nographics -quit -projectPath 2-client/game -logFile build.log`; fix every
   `error CS` (start with `Runtime/CoreGameplay.cs` and the `_core` hooks in `VerticalSliceController`).
2. Open the scene, enter Play Mode against `go run ./cmd/pogserver -starter` and check the Console for exceptions.
3. Camera and input: mouse look in first/third person; cursor locks only in first person with the HUD folded
   (F1) and no window open; no movement without keys; RMB (guard) does not break the camera.
4. Movement reconciliation at a wall, on the bridge, at the world edge and while running.
5. Loop: talk to a specific villager → chop in the woods → buy and drink beer in the tavern. Each success must
   appear only after the server confirms it (`me.result` / state); a refusal must show its reason.
6. Foes: visible in the wild zones, primitives lit correctly under URP, aim ray picks the right body part.
7. Feed and journal: no duplicates while full and fast frames arrive together.
8. HUD: Tab window, F1, H (hints), text readable at the real resolution; nothing permanently covers the view.
9. Disconnect and reconnect: no stuck cursor, guard, pending action or stale foes.

## Core gameplay v1.10 — added 2026-11-10 (by Claude, with the owner's permission)

- `Scripts/Runtime/CoreGameplay.cs` + `Scripts/Presentation/CoreFoeView.cs` add the v1.10 layer: foes in the
  scene (`place.foes`), first-person aimed attacks (`op:"attack"` with target/body part/accuracy; the server
  resolves), guard/dodge/stances/tricks, the mouse-traced weave wheel (hold R), the "Дела" window on Tab
  (crafting, requests, market, bag, roads, party), the road screen, the confirmed event feed and status bars.
- The socket asks for `fast=8` frames; `VerticalSliceController` routes them to `CoreGameplay` and keeps the
  strict snapshot parser for full frames only.
- Second pass (same day): foes got snouts, ears, tails, eyes, boar tusks, a bear hump, a hooded bandit; floating
  damage numbers and a hit squash; chopping with the axe and fishing with the rod on LMB; H hides the hints.
- Merged to main on the owner's instruction without an Editor run. If the project does not compile, start with
  `CoreGameplay.cs` and the three `_core` hooks in `VerticalSliceController`.
- Not compiled in the Unity Editor and not run in PlayMode. The two new files compile with `mcs` against
  hand-written API stubs, which checks syntax and typing only. Treat it as a first draft to verify.

## Current implementation status — 2026-11-11 (v1.16)

> This section supersedes any conflicting historical v1-only scope statements below. The detailed feature inventory later in this file has not yet been fully reconciled with the current runtime; treat it as historical implementation notes, not as a complete current contract.

- The Unity client now requests the server-owned **Layout v2** document before enabling movement prediction. The returned 300×300 m world geometry, building footprints, river and bridge are rendered by `WorldLayoutPresentationBuilder`; local movement checks the same received geometry through `WorldLayoutFrame.ResolveMove`.
- The snapshot DTO includes additive v1.6/v1.7 fields: inventory (`me.inv`), hand/worn equipment, skills, desire, settlement list (`me.world`), known-place state, neighbor traits/wants/player marker, and monotonic social-event sequence. Missing additive fields remain nullable for older v:1 snapshots.
- The Inventory tab is now actionable for the existing server model: it shows `me.inv.items`, hand and worn slots, `me.stash` bulk goods, food/medicine, `me.bag` goods load and horse saddlebag contents. Equip, wear, use/eat, horse stow and unstow are sent through the shared `CoreGameplay.RequestGate`; the UI waits for matching `me.result` and never mutates inventory optimistically. The "Дела" tab already exposes server-backed take, craft, market, fieldwork, forge, travel and other supported actions. Split stacks, drop, arbitrary container transfers and a purchasable/craftable physical bag are not implemented yet.
- Player appearance is driven by authoritative `me.inv`: head/body/feet render hat/cloak/boots; armor/offhand render the crafted jerkin/shield; hand renders axe/sword/knife/bow/rod/torch/lantern. Carried tools, quiver, belt pouches and folded clothing are presentation-only representations of actual inventory counts. Nearby entities receive only the public `gear` subset, never private item quantities. Equipment actions use the shared result gate.
- The current tutorial/HUD is still a prototype, not yet the complete owner-approved #80 loop. Targeted NPC talk, task progression/reward and tavern continuation still need end-to-end verification against server acceptance/reconnect rules.
- The first-person/third-person camera and WASD movement exist. Space triggers a **presentation-only jump**: `PlayerVisualRoot` and the camera move vertically while the authoritative player root and server snapshot remain grounded. It is visual feedback only; it does not clear obstacles, change server physics or authorize vertical movement. Reset/reconnect handling is implemented. Do not treat it as networked jump gameplay.
- The thin client is a separate click-to-location farm/management experience with minigames. It must not inherit continuous character walking from the Unity thick client.
- Unity Editor compilation, PlayMode and visual acceptance have not been run in this environment. A clean source diff is not a Unity build claim.

This folder contains the Unity client scaffold for the stable v1 protocol.


## World model — owner clarification (2026-11-06)

**Two-tier model: bounded starter/demo territory + huge open world.** The starter territory contains several villages; travel between those villages is physical and road-based. Mountains/natural barriers form the outer perimeter separating the demo territory from the open world. A special gateway/teleport may be used only to cross from the starter/demo territory into the open world; this is not ordinary settlement travel.

Inside the open world, regions are parts of one large connected geography. Travel between its settlements/regions is physical along roads, bridges, passes and other traversable routes; no ordinary teleport. `op:travel` must not silently represent village-to-village travel in the thick client.

The current 300×300 m Layout v2 is the local playable area of one settlement, not the dimensions of the full multi-village demo territory or the open world. The global coordinate, region streaming and demo-gateway contract remain architecture work; do not claim the current client traverses these spaces already.

## Project target

- Unity Editor: **6000.6.7f1**
- Render pipeline: **URP** (decision confirmed by project owner on 2026-11-02)
- URP package: **17.6.0** (Unity 6.6 / 6000.6 line)
- JSON: Newtonsoft JSON

The URP package is pinned in `Packages/manifest.json`. Unity's graphics packages are core packages; if Package Manager reports a package resolution issue, use the version offered for this exact Editor installation rather than upgrading Unity or guessing a newer package.

## First-time setup

1. Open this folder as a Unity project with Unity **6000.6.7f1**.
2. Wait for Package Manager to resolve dependencies and for scripts to finish importing.
3. In the Project window, create a URP asset: **Assets → Create → Rendering → URP Asset (with Universal Renderer)**. Save it under `Assets/_Project/Settings/`.
4. Open **Edit → Project Settings → Graphics** and assign that asset to **Default Render Pipeline**.
5. Open **Edit → Project Settings → Quality** and make sure no active quality level overrides the Graphics URP asset with a different pipeline.
6. Create the scene from **Tools → Pogranichye → Create v1 Vertical Slice Scene**.
7. Save the generated scene and add it to Build Settings if you need a player build.

The repository initially contained a source scaffold rather than a fully initialized Unity project. The first-time setup above completes the editor-owned URP asset configuration.

## Scope

- HTTP registration/login using the v1 endpoints.
- WebSocket connection to `/ws?token=...`.
- Full-snapshot JSON parsing and protocol validation (`v == 1`).
- Entity View registry keyed by `ulong` parsed from string IDs.
- v1.1 local settlement coordinates: canonical `me.pos` and `neighbors[].pos` are mapped to Unity metres (server X→Unity X, server Y/north→Unity Z); v1.0 snapshots without positions remain parseable.
- Low-poly humanoid player/NPC prototypes scaled to approximately 1.8 m, with simple facial features, profession accessories (caps, hats and headbands), role-based tunic colors with stable-ID color variation, varied skin tones, occasional hair/beards and subtle stature differences, camera-facing labels, and procedural idle/work/social/eat/rest motion loops, with a reserved drinking gesture that activates only when a future server activity reports it.
- Deterministic first-village scene: meadow with generated grass texture, packed-earth texture, weathered timber grain, thatched-roof courses and rough-stone texture, cottages with stone plinths, exposed timber framing, shuttered windows and roof details; an open-front tavern placed beside the server spawn, with framed walls, detailed counter mouldings/panels, beer taps, back-bar shelf, bottles, tables, stools, casks and decorative beer mugs, detailed blacksmith props, market produce with delivery crates and sacks, fenced garden beds, a well with winch/rope/bucket, roadside lanterns with subtle flicker, a crossroads signpost, resting bench, laundry line with gently swaying linen, tavern hitching rail and water trough, barrels, stacked firewood, handcart, narrow creek with plank footbridge, distant tree line, an outer belt of mixed broadleaf and conifer woodland with low shrubs, wildflower clusters, scattered field stones, subtle drifting pollen and dawn/dusk lowland mist and a slowly flowing creek surface with reed clusters along its banks, an outlying crop field with five planted ridges, scarecrow and harvest storage, a small fruit orchard with five fruit trees, a low fence and harvest basket, archery/combat practice yard, crafting bench, fishing dock, fences, trees, wind-swaying grass, warm directional light, ambient light and distance fog. A presentation-only ambient motion component animates lantern intensity and hanging cloth; it does not alter simulation state.
- Tutorial POI markers/signs for tavern, work, food and garden. A centre-screen raycast finds POIs/NPCs within 3.2 m and shows an `[E]` contextual prompt; the active reticle target receives a presentation-only focus highlight. Pressing E requests interaction. POIs send only their mapped existing v1 goal; NPCs send generic `socialize`, not targeted dialogue. No local activity or world state is simulated. The v1 server currently accepts `socialize`, `work`, `eat`, `rest`.
- Hybrid camera: third-person smooth follow/orbit, mouse-wheel zoom and SphereCast obstacle avoidance; optional first-person mouse-look with camera-attached forearms and simple hand geometry. Mouse always controls view; RMB is a contextual alternate item/tool mode and does not itself freeze the camera. Only a future gesture mode may explicitly capture mouse motion.
- v1.1 movement: WASD sends normalized `op:move` intents at a throttled ~20 Hz; Shift requests server-controlled running. The client predicts step/run locally for responsiveness, sends real elapsed `dt` and monotonic `seq`. Intents are queued in order while the socket send is busy, preserving quick direction changes instead of merging them into the last input. Unacknowledged intents are replayed over each canonical snapshot and the remaining error is smoothed. Server position and stamina remain authoritative; movement is disabled when snapshots lack `me.pos`. The ground plane is 60×60 m; prediction retains the documented radial 29.5 m margin. The merged server currently clamps X/Y independently (square), so the client/server boundary discrepancy is tracked in issue #17; integration `dt` is capped at the server's 250 ms maximum. No local obstacle collision is claimed yet.
- Explicit connection states (`Closed`, `Connecting`, `AwaitingSnapshot`, `Active`), duplicate-connect prevention, and stale-socket callback guards.
- First-steps HUD explaining the four supported server goals and showing the latest server-side stats/activity.
- A generated starter scene with camera, directional light and runtime controller; the controller builds the prototype environment when Play starts.
- Expanded northern residential lane: three additional cottages, branching dirt paths, domestic yards (laundry, firewood, handcart, kitchen garden) and a shared hearth aligned to the current server hearth POI (4, 16). This remains presentation-only and inside the existing 30 m scene boundary; it does not claim new server navigation or collision support.
- Any camera/light created at runtime is parented to the controller lifecycle, and the controller removes only a camera rig component it added itself.

## Run

1. Start the Go server on `http://127.0.0.1:8090`.
2. If the generated scene is not present yet, run **Tools → Pogranichye → Create v1 Vertical Slice Scene** once; then open `Assets/_Project/Scenes/VerticalSlice.unity`.
3. Enter login and password. Register first if needed, then log in.
4. Connect to the world. The client builds the prototype village and renders the player plus nearby NPCs at server-provided v1.1 scene positions. If the server sends an older v1.0 snapshot without `pos`, movement remains disabled and neighbor fallback placement is only a preview.
5. Use WASD to move and Shift to request running. Move the mouse to orbit the third-person camera or look around in first-person. The client queues ordered `move` intents and predicts motion; server snapshots reconcile position and stamina. In first-person, forearms and hands are attached to the camera as a presentation-only viewmodel and are restored to the full character when switching back to third-person. Use the wheel to zoom in third-person. Aim at a nearby POI/NPC and press E, or use the first-steps panel. Contextual interactions are range-limited to 3.2 m and send only existing v1 goal intents; the server remains authoritative for the result.

The broader keyboard/mouse scheme and intended weighty-but-responsive character feel are documented in `../../3-dev/comms/0025-chatgpt-control-and-puppet-feel-proposal.md`. WASD/Shift movement is now wired to the agreed v1.1 movement contract; combat, bow, fishing, crafting, magic and beer remain future server-authoritative mechanics. The client sends intentions only. It never changes authoritative world state locally. The four-step onboarding advances only after a snapshot reflects the requested server-side goal; it does not locally simulate success.

## Protocol rules

- Protocol version and `alive` are validated as JSON integer/boolean tokens; unsupported versions are rejected visibly.
- `me.id` and `neighbors[].id` must be JSON strings containing valid uint64 values; numeric JSON IDs are rejected rather than coerced.
- The Editor fixture validator also checks malformed IDs, missing `alive`, string-valued versions, unsupported versions, and the valid `alive:false` terminal frame.
- `alive:false` is a valid terminal frame and contains no `me` object.
- `place` and `neighbors` may be absent. `me.pos` and `neighbors[].pos` are optional for backward-compatible v1.0 snapshots; when present they follow the v1.1 coordinate contract.
- `last` / `act` remain opaque server display text for gameplay. The prototype character animation uses only lightweight presentation heuristics for recognizable words; it never derives or changes simulation state from them.
- Incoming WebSocket messages are assembled as bytes before UTF-8 decoding; the 1 MiB limit is measured in bytes and malformed UTF-8 is rejected.
- Snapshots are full state, approximately once per second.
- Neighbor positions are taken from `neighbors[].pos` when available; deterministic ring placement is only a fallback for older snapshots.
- The interaction probe is presentation/input only: a centre-camera raycast and 3.2 m range gate select a POI/NPC, then the controller sends only a supported v1 goal. Third-person camera zoom and obstacle avoidance are visual-only. Direct click-to-select has been removed in favour of contextual E interaction.
- The prototype scene builder accepts a stable string seed (currently `tutorial-preview`) and hashes it deterministically. The tavern mugs are currently visual props only: actual beer purchase/consumption is a required test-build feature, but must wait for an agreed server-authoritative drink intent and snapshot result. The current v1.1 snapshot exposes local settlement position only; it does **not** expose the actual world seed, biome, building layout or POIs. When the server supplies agreed metadata, the client can pass the server seed into the generator and derive reproducible presentation geometry without treating it as simulation authority.

## Verification status

The source has **not yet been compiled or run inside Unity Editor 6000.6.7f1**. Parser hardening and Editor-side positive/negative fixture checks are source changes only until the menu action is actually run. URP package pin and setup instructions are committed as project configuration, but actual package resolution, asset assignment, scene generation, runtime compatibility and server integration still require an Editor run. Do not treat this branch as a tested Unity build.
