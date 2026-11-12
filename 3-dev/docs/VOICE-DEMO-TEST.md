# Voice demo: manual verification checklist

## Requirements
- Unity 6000.6.7f1.
- Two separate accounts and two clients in the same settlement, within 35 m.
- Microphone and speakers/headphones available. Headphones recommended to avoid acoustic feedback.
- Network must permit WebRTC UDP/STUN. TURN relay is not configured yet, so some NAT/firewall combinations will fail even when signaling succeeds.

## Unity setup
1. Open 2-client/game in Unity. Package Manager should resolve com.fdgs.webrtc 3.2.1, SimpleWebRTC 2.0.0 and UGUI 2.0.0 from Packages/manifest.json.
2. Wait for native plugin import and script compilation. If the package cannot resolve or reports missing native binaries, do not remove the dependency silently; capture the full Package Manager error.
3. Enter Play mode, log into account A and connect to the world. Repeat with account B on another client/device.
4. Keep both souls in the same settlement and within 35 m.

## Voice test
1. On both clients click "Войти в голос". This does not request microphone access.
2. On client A click "Включить микрофон"; grant OS microphone permission if prompted.
3. Speak from A. Client B should hear A; B's microphone remains muted by default.
4. Enable B's microphone and verify two-way audio.
5. Mute A and confirm B stops hearing A while B→A still works.
6. Move one player beyond 35 m or to another settlement and verify new signaling is rejected. Existing P2P audio may persist until the session is closed; range-triggered voice teardown is a follow-up.
7. Click "Выйти из голоса" on either client; the other should receive peer-left and stop receiving audio.
8. Disconnect one client from the world; the server should release the voice peer and the same soul should return to A-Life.

## Known limits
- Two-peer demo only; SimpleWebRTC currently uses a full mesh, not an SFU.
- Default STUN only. TURN configuration/credentials and relay testing are not yet implemented.
- No proximity volume attenuation yet; range is enforced for signaling, not continuous audio attenuation.
- No thin-client/browser voice UI yet.
- Unity Editor, microphone, native plugin and real network tests must be run by the owner; Go CI cannot validate these.
