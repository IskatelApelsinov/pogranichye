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
- Temporary client-side neighbor layout only; it is not world coordinates.
- Low-poly humanoid player/NPC prototypes scaled to approximately 1.8 m, with simple facial features, profession accessories (caps, hats and headbands), role-based tunic colors, camera-facing labels, and procedural idle/work/social/eat/rest motion loops, with a reserved drinking gesture that activates only when a future server activity reports it.
- Deterministic first-village scene: meadow with generated tile texture, packed-earth paths, cottages, an open-front tavern interior with bar counter, back-bar shelf, bottles, tables, stools, casks and decorative beer mugs, detailed blacksmith props, market produce, garden, well, barrels, stacked firewood, handcart, narrow creek with plank footbridge, distant tree line, archery/combat practice yard, crafting bench, fishing dock, barrels, stacked firewood, handcart, fence, trees, grass details, warm directional light, ambient light and distance fog.
- Tutorial POI markers/signs for tavern, work, food and garden. A centre-screen raycast finds nearby POIs/NPCs within 3.2 m and shows an `[E]` contextual prompt; pressing E requests interaction. Hover highlighting remains presentation-only. POIs send only their mapped existing v1 goal; NPCs send generic `socialize`, not targeted dialogue. No local activity or world state is simulated. The v1 server currently accepts `socialize`, `work`, `eat`, `rest`.
- Hybrid camera prototype: third-person smooth follow by default, mouse-driven orbit, mouse-wheel zoom and sphere-cast obstacle avoidance; optional first-person view with always-on mouse-look and visible arms. RMB is reserved for future magic-weaving input; camera look pauses while RMB is held so the mouse can trace a pattern. Third-person orbit changes only camera yaw/pitch, not the character transform. The contextual probe uses the centre reticle and E key for short-range POI/NPC interaction. In the fixed tutorial preview, NPC placeholders are placed within interaction distance and the tavern marker is reachable from the initial spawn; other POIs still require future server-authoritative movement. NPCs highlight on hover and interaction sends only the existing generic socialize goal (not targeted dialogue). Environment/entity positions remain presentation-only.
- Explicit connection states (`Closed`, `Connecting`, `AwaitingSnapshot`, `Active`), duplicate-connect prevention, and stale-socket callback guards.
- First-steps HUD explaining the four supported server goals and showing the latest server-side stats/activity.
- A generated starter scene with camera, directional light and runtime controller; the controller builds the prototype environment when Play starts. Any camera/light created at runtime is parented to the controller lifecycle, and the controller removes only a camera rig component it added itself.

## Run

1. Start the Go server on `http://127.0.0.1:8090`.
2. Open `Assets/_Project/Scenes/VerticalSlice.unity`.
3. Enter login and password. Register first if needed, then log in.
4. Connect to the world. The client builds the prototype village and renders the animated player plus nearby NPCs. Their positions are temporary presentation layout, not server coordinates.
5. In third-person, move the mouse to orbit the camera and use the wheel to zoom. In first-person, move the mouse to look around. RMB is reserved as a contextual alternate mode for the equipped tool/item (bow aim, fishing, magic, etc.); it does not automatically freeze camera look. Gesture-based modes may explicitly capture mouse motion when implemented. Aim at a nearby POI/NPC and press E, or use the first-steps panel: tavern/socialize, work, eat, rest. Contextual interactions are range-limited to 3.2 m and send only existing v1 goal intents; the server remains authoritative for the result.

Proposed full keyboard/mouse scheme and the intended weighty-but-responsive character feel are documented in `../../3-dev/comms/0025-chatgpt-control-and-puppet-feel-proposal.md`; it is a design proposal, not a claim that movement/combat inputs are implemented. The client sends intentions only. It never changes authoritative world state locally. The four-step onboarding advances only after a snapshot reflects the requested server-side goal; it does not locally simulate success.

## Protocol rules

- Protocol version and `alive` are validated as JSON integer/boolean tokens; unsupported versions are rejected visibly.
- `me.id` and `neighbors[].id` must be JSON strings containing valid uint64 values; numeric JSON IDs are rejected rather than coerced.
- The Editor fixture validator also checks malformed IDs, missing `alive`, string-valued versions, unsupported versions, and the valid `alive:false` terminal frame.
- `alive:false` is a valid terminal frame and contains no `me` object.
- `place` and `neighbors` may be absent.
- `last` / `act` remain opaque server display text for gameplay. The prototype character animation uses only lightweight presentation heuristics for recognizable words; it never derives or changes simulation state from them.
- Incoming WebSocket messages are assembled as bytes before UTF-8 decoding; the 1 MiB limit is measured in bytes and malformed UTF-8 is rejected.
- Snapshots are full state, approximately once per second.
- Neighbor positions are a temporary deterministic ring around the player, not server coordinates.
- The interaction probe is presentation/input only: a centre-camera raycast and 3.2 m range gate select a POI/NPC, then the controller sends only a supported v1 goal. There is no local movement yet, so the probe does not create the ability to walk to distant targets. Third-person camera zoom and obstacle avoidance are visual-only. Direct click-to-select has been removed in favour of contextual E interaction.\n- The prototype scene builder accepts a stable string seed (currently `tutorial-preview`) and hashes it deterministically. The tavern mugs are currently visual props only: actual beer purchase/consumption is a required test-build feature, but must wait for an agreed server-authoritative drink intent and snapshot result. The current v1 snapshot does **not** expose the actual world seed, biome, settlement coordinates, building layout or POIs. When the server supplies agreed metadata, the client can pass the server seed into the generator and derive reproducible presentation geometry without treating it as simulation authority.

## Verification status

The source has **not yet been compiled or run inside Unity Editor 6000.6.7f1**. Parser hardening and Editor-side positive/negative fixture checks are source changes only until the menu action is actually run. URP package pin and setup instructions are committed as project configuration, but actual package resolution, asset assignment, scene generation, runtime compatibility and server integration still require an Editor run. Do not treat this branch as a tested Unity build.
