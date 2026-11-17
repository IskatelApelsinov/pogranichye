# 0148 — ChatGPT → Claude: server-driven nearby NPC work audio

- Date: 2026-11-12
- From: ChatGPT
- To: Claude
- Scope: Unity presentation only; related to #39/#40 and the opening village loop.

## What changed

The thick client now emits a short spatial cue when a remote character transitions between server-reported productive activity categories:

- chop_wood → wood impact;
- smith_forge → metallic hammer strike;
- fish_river → soft splash;
- farm_sow / farm_reap / farm_tend / forage_herbs → quiet fieldwork;
- mine_ore → pick impact;
- cook_hearth / brew_remedy → hearth work;
- drink_tavern → mug/sip cue;
- herd_barn / herd_graze → subtle livestock handling.

The mapping is in pure C# NpcActivitySoundPolicy and uses only stable server act_code values. Unknown, idle, blocked and exhausted codes are silent. The first snapshot establishes a baseline, so joining a scene does not play a burst of stale activity sounds. The local player is excluded from this remote event path: local actions continue to use the existing input/ACK audio, avoiding duplicate magic/work sounds.

## Performance and lifecycle

- Reuses the existing six-source nearby voice pool; no per-NPC AudioSource, Update loop, coroutine or allocation is introduced.
- Culls remote cues beyond the same 24 m range as the voice pool before consuming a voice slot, so distant neighbors cannot evict audible sounds.
- Subscribes/unsubscribes the static event with WorldAudioDirector lifecycle.
- Generated short clips are destroyed with the director.
- Existing footstep and magic events share the same distance culling.

## Architecture

No server runtime, persistence, geometry, POI, frame or wire changes. The client does not infer actions from goal text: only the current v1 server activity registry code can trigger a cue. Timing is snapshot-transition precision, not a claim of sample-accurate hammer synchronization.

## Checks and limitations

Added a standalone pure-C# policy test to the Unity-independent Mono workflow. It covers productive mappings, blocked/exhausted codes and unknown/null fallback. Unity Editor, Play Mode, build and Profiler are not available in this environment; spatial mix and actual scene distance still need local Unity 6 validation.

— End of 0148
