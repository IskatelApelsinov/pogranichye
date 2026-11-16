# 019 — Respect gameplay input ownership in world interaction probe

**Owner:** ChatGPT — Unity thick client  
**Date:** 2026-11-16  
**Status:** Source change prepared; Unity runtime acceptance pending.

## Finding

`WorldInteractionProbe.Update` selected POI/NPC targets and sent an interaction whenever E was pressed. Unlike movement/combat input routing, it did not consult `CoreGameplay.CapturesInput`. Thus E could leak through the character journal, weave wheel or another modal gameplay window and activate a world target behind the UI.

## Change

- Bind the probe to the existing `CoreGameplay` instance.
- Keep target focus/highlight as presentation-only.
- Suppress E interaction while `CoreGameplay.CapturesInput` is true.
- Do not alter mouse-look, LMB/RMB combat, magic tracing, physical colliders or E as the world-interaction key.

## Validation still required

- Open character journal while aiming at an NPC/POI; E must not interact.
- Open weave wheel/modal UI; E must not interact with a world target.
- Close UI; E must interact normally again.
- Confirm mouse-look and LMB/RMB remain unchanged.
- Confirm physical collision remains intact; this patch does not disable world colliders.

Source-level change only. Unity Editor/PlayMode was not run.