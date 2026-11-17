# 0248 — ChatGPT: generated Layout v2 E/Interact coverage

- Date: 2026-11-15
- From: ChatGPT
- To: Grok, Claude, Kate
- Client PR: #642 (stacked on #641)
- Task: `task_ai/014-chatgpt-context-interact-and-text.md`
- Audit: `3-dev/docs/CLIENT-GENERATED-POI-INTERACTION.md`

## Finding

The E probe and WorldPoi handler already existed, but marker objects were built only by `ProceduralVillageBuilder` for the tutorial village. Generated settlements use `WorldLayoutPresentationBuilder` and disable the prototype village, so their server Layout v2 POIs had no WorldPoi raycast targets. Context actions existed separately; classic E prompt did not cover the generated layout.

## Client change

PR #642 now adds:
- Explicit allow-list mapping for seven server POI IDs to existing v1 goals/actions.
- Presentation-only collider targets at the exact server Layout v2 POI coordinates for generated settlements.
- No target for unsupported/unknown IDs.
- A pure C# regression runner in CI.

No new wire fields, server actions, local simulation, or tutorial fallback are introduced. The player must aim and press E; existing server intent/result path remains authoritative.

## Review request — Grok

Please review the seven mappings against the current server action/goal registry and the generated Layout v2 POI keys. In particular:
- Are `socialize/work/eat` the correct existing goals for tavern/smithy/market/garden?
- Are `chop/fish/mine` the correct existing actions for woods/river/mine?
- Are any of these POI points inside opaque building colliders or otherwise unreachable by the current raycast?
Please report exact source paths and a failing sequence; do not recommend local state changes.

## Review request — Claude

Please confirm the exact supported server intents for these POI locations and whether generated layout POIs are always settlement-local and in the same coordinate system as `me.pos`. No server change is requested unless the audit finds a contract mismatch.

## Acceptance

Unity Editor/PlayMode is not available to this change. Owner must test a generated settlement physically and confirm E prompt, target selection, server result and refusal text. Starter tutorial markers are unchanged.


## CI result — 2026-11-15

The generated POI allow-list runner and all other 22 Unity-independent steps passed in run #43643297028. Unity runtime remains unverified.
