# 0099 — ChatGPT → Claude: Unity voice client integration review

Current branch feature/unity-voice-client-20261112:
- Added UPM deps: FDGS WebRTC 3.2.1 (Apache-2.0) and SimpleWebRTC 2.0.0 pinned commit 09f99e90a18064c705837c157abd6fb4c9d8649f (MIT), plus UGUI 2.0.0.
- Added PogranichyeVoiceTransport adapter. It maps server peer_joined/signaling_type=join|welcome, offer, answer, ice, peer_left to SimpleWebRTC v2 SignalingMessage. Outbound SimpleWebRTC signaling maps to our op:"voice".
- VoiceChatController creates WebRTCPeerManager, AudioSender, AudioReceiver at runtime. Joining the voice channel does not open the mic; explicit "Включить микрофон" starts capture, muted by default.
- WebRTC native initialization is invoked through reflection because FDGS exposes InitializeInternal as internal. Please review this lifecycle carefully.
- Server join/welcome event routing now matches SimpleWebRTC's PeerManager expectations; the manager elects its offerer via stable peer IDs.

Review:
1. Confirm UPM Git URLs resolve in Unity 6000.6.7f1, including native binaries from v3.2.1 tag.
2. Confirm reflection-based InitializeInternal call is safe, idempotent, and proper native disposal is handled.
3. Check audio-only PeerSession transceiver lifecycle when both users join muted, then enable/disable microphones.
4. Review disconnect/reconnect and socket-close ordering; ensure voice session always sends leave or relies on server unregister.
5. Test if SimpleWebRTC's Google STUN-only default is enough for our two-device target; plan TURN credentials/config without committing secrets.
6. Consider proximity audio attenuation and automatic teardown after players leave 35 m.
7. Check Android runtime mic permission before we promise mobile support.

Owner must run Unity Editor and two real clients; no claim of working audio until manual checklist passes. See 3-dev/docs/VOICE-DEMO-TEST.md.
