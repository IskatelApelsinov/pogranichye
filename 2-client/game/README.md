# Unity 6 client — v1 vertical slice

This folder contains the Unity client scaffold for the stable v1 protocol.

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
- Low-poly humanoid player/NPC prototypes scaled to approximately 1.8 m, with simple facial features, profession accessories (caps, hats and headbands), role-based tunic colors, camera-facing labels, and procedural idle/work/social/eat/rest motion loops, with a reserved drinking gesture that activates only when a future server activity reports it.
- Deterministic first-village scene: meadow with generated grass texture, packed-earth texture, weathered timber grain, thatched-roof courses and rough-stone texture, cottages with stone plinths, exposed timber framing, shuttered windows and roof details; an open-front tavern placed beside the server spawn, with framed walls, detailed counter mouldings/panels, beer taps, back-bar shelf, bottles, tables, stools, casks and decorative beer mugs, detailed blacksmith props, market produce, garden, well, barrels, stacked firewood, handcart, narrow creek with plank footbridge, distant tree line, archery/combat practice yard, crafting bench, fishing dock, barrels, stacked firewood, handcart, fence, trees, grass details, warm directional light, ambient light and distance fog.
- Tutorial POI markers/signs for tavern, work, food and garden. A centre-screen raycast finds nearby POIs/NPCs within 3.2 m and shows an `[E]` contextual prompt; pressing E requests interaction. Hover highlighting remains presentation-only. POIs send only their mapped existing v1 goal; NPCs send generic `socialize`, not targeted dialogue. No local activity or world state is simulated. The v1 server currently accepts `socialize`, `work`, `eat`, `rest`.
- Hybrid camera: third-person smooth follow/orbit, mouse-wheel zoom and SphereCast obstacle avoidance; optional first-person mouse-look with camera-attached forearms and simple hand geometry. Mouse always controls view; RMB is a contextual alternate item/tool mode and does not itself freeze the camera. Only a future gesture mode may explicitly capture mouse motion.
- v1.1 movement: WASD sends normalized `op:move` intents at a throttled ~20 Hz; Shift requests server-controlled running. The client predicts step/run locally for responsiveness, sends real elapsed `dt` and monotonic `seq`. Intents are queued in order while the socket send is busy, preserving quick direction changes instead of merging them into the last input. Unacknowledged intents are replayed over each canonical snapshot and the remaining error is smoothed. Server position and stamina remain authoritative; movement is disabled when snapshots lack `me.pos`. The ground plane is 60×60 m; prediction retains the documented radial 29.5 m margin. The merged server currently clamps X/Y independently (square), so the client/server boundary discrepancy is tracked in issue #17; integration `dt` is capped at the server's 250 ms maximum. No local obstacle collision is claimed yet.
- Explicit connection states (`Closed`, `Connecting`, `AwaitingSnapshot`, `Active`), duplicate-connect prevention, and stale-socket callback guards.
- First-steps HUD explaining the four supported server goals and showing the latest server-side stats/activity.
- A generated starter scene with camera, directional light and runtime controller; the controller builds the prototype environment when Play starts. Any camera/light created at runtime is parented to the controller lifecycle, and the controller removes only a camera rig component it added itself.

## Run

1. Start the Go server on `http://127.0.0.1:8090`.
2. Open `Assets/_Project/Scenes/VerticalSlice.unity`.
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
