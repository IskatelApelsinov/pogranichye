# Unity 6 client — v1 vertical slice

This folder contains the first client implementation scaffold for the stable v1 protocol.

## Scope

- HTTP registration/login using the v1 endpoints.
- WebSocket connection to /ws?token=...
- Full-snapshot JSON parsing and protocol validation (v == 1).
- Entity View registry keyed by ulong parsed from string IDs.
- Temporary client-side neighbor layout only; it is not world coordinates.
- Debug HUD and primitive capsule views to validate the end-to-end connection.

## Unity setup

Target editor: Unity 6000.6.7f1 (provided by project owner). The repository did not contain an existing Unity project when this scaffold was started. This folder is being prepared against that exact editor version; render pipeline remains intentionally undecided pending a visual prototype.

Create/open a Unity 6 project in this folder (or copy Assets/ and Packages/ into an existing Unity 6 project). The package manifest includes Newtonsoft JSON, used because Unity's built-in JsonUtility does not support the dynamic wounds and stash maps in the server frame.

The scene bootstrap is available from Tools → Pogranichye → Create v1 Vertical Slice Scene after Unity imports the scripts.

## Run

1. Start the Go server on http://127.0.0.1:8090.
2. Open the generated Assets/_Project/Scenes/VerticalSlice.unity.
3. Enter login and password. Register first if needed, then log in.
4. The client opens the WebSocket and renders the player plus up to eight neighbors.
5. Use the goal buttons to send v1 intent messages.

The client sends intentions only. It never changes authoritative world state locally.

## Protocol rules

- Protocol version is checked; unsupported versions are rejected visibly.
- me.id and neighbors[].id remain strings in JSON and are parsed to ulong for view identity.
- alive:false is a valid terminal frame and contains no me object.
- place and neighbors may be absent.
- last is opaque display text. The client does not parse it into activity codes.
- Snapshots are full state, approximately once per second.
- Neighbor positions are a temporary deterministic ring around the player, not server coordinates.

## Verification status

Source has been reviewed but has not yet been compiled or run inside Unity Editor. This must not be treated as a tested Unity build.
