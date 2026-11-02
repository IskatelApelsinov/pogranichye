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
- Camera-facing world-space labels for the player and nearby NPCs; per-renderer colors use `MaterialPropertyBlock` rather than allocating one material per entity.
- Explicit connection states (`Closed`, `Connecting`, `AwaitingSnapshot`, `Active`), duplicate-connect prevention, and stale-socket callback guards.
- Debug HUD and primitive capsule views to validate the end-to-end connection.
- A generated starter scene with camera, directional light, ground plane and the runtime client controller.

## Run

1. Start the Go server on `http://127.0.0.1:8090`.
2. Open `Assets/_Project/Scenes/VerticalSlice.unity`.
3. Enter login and password. Register first if needed, then log in.
4. Connect to the world. The client renders the player plus up to eight neighbors.
5. Use the goal buttons to send v1 intent messages.

The client sends intentions only. It never changes authoritative world state locally.

## Protocol rules

- Protocol version and `alive` are validated as JSON integer/boolean tokens; unsupported versions are rejected visibly.
- `me.id` and `neighbors[].id` must be JSON strings containing valid uint64 values; numeric JSON IDs are rejected rather than coerced.
- The Editor fixture validator also checks malformed IDs, missing `alive`, string-valued versions, unsupported versions, and the valid `alive:false` terminal frame.
- `alive:false` is a valid terminal frame and contains no `me` object.
- `place` and `neighbors` may be absent.
- `last` is opaque display text. The client does not parse it into activity codes.
- Incoming WebSocket messages are assembled as bytes before UTF-8 decoding; the 1 MiB limit is measured in bytes and malformed UTF-8 is rejected.
- Snapshots are full state, approximately once per second.
- Neighbor positions are a temporary deterministic ring around the player, not server coordinates.

## Verification status

The source has **not yet been compiled or run inside Unity Editor 6000.6.7f1**. Parser hardening and Editor-side positive/negative fixture checks are source changes only until the menu action is actually run. URP package pin and setup instructions are committed as project configuration, but actual package resolution, asset assignment, scene generation, runtime compatibility and server integration still require an Editor run. Do not treat this branch as a tested Unity build.
