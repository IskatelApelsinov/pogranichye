# 0098 — ChatGPT → Claude: WebRTC voice client compatibility review

Server signaling slice is in feature/voice-signalling-20261112:
- op voice actions: join, offer, answer, ice, leave;
- sender derives from active WS soul lease;
- same settlement + 35 m proximity + both peers voice-enabled;
- SDP max 64 KiB, ICE max 4 KiB, signaling throttle, bounded queue;
- media never goes through game server; P2P WebRTC, STUN/TURN configured by clients.

## Critical Unity dependency finding

Do not automatically add com.unity.webrtc from Unity: upstream marks it deprecated, while this project uses Unity 6000.6.7f1. Please check maintained candidate FireDragonGameStudio/com.unity.webrtc.fdgs (SimpleWebRTC 2.0 lists FDGS WebRTC 3.2.0+ and Unity 6000.0+) before accepting the dependency:
1. Check repository source, license, release/tag and exact UPM package URL.
2. Verify Windows desktop first, then Android ARM64 / iOS if relevant.
3. Verify audio-only capture/playback, microphone permission, mute, device selection, disposal/reconnect.
4. Ensure signaling transport can use our authenticated Pogranichye WS rather than a third-party signaling server.
5. Check whether browser thin-client and Unity client can interoperate using the same SDP/ICE signaling envelope.
6. Use TURN fallback for internet NAT; do not claim STUN-only will always connect.

## Please review

- Check server signaling event shapes and propose missing WebRTC fields (ufrag, mid, mline, candidate end marker, polite/impolite offer role).
- Suggest a minimal two-peer negotiation state machine avoiding glare when both users click Join at once.
- Recommend PTT default for the demo, explicit mic permission and mute indicator.
- Avoid adding deprecated Unity package or changing Unity manifest until compatibility/license is confirmed.

The owner wants a playable two-person demo first; keep this path simple and measurable. This PR is only server signaling, not an audio implementation.
