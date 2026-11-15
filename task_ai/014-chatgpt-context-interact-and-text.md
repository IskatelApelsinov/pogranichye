!В работе
# 014 — ChatGPT: generated-layout E/Interact + readable text

**Owner:** ChatGPT (Unity)  
**Priority:** client follow-up after tavern counter slice  
**Spec:** `3-dev/docs/СЕЛО-И-КЛИЕНТ-UX.md`

## Goal

Make the existing classic E/Interact probe work against server-provided POIs in generated settlements, without inventing local gameplay.

## Implemented in PR #642

1. Confirmed `ClientInput` + `WorldInteractionProbe` + `WorldPoi/WorldNpc` already implement E, short-range ray/sphere targeting, focus prompt and occlusion.
2. Found generated layouts lacked `WorldPoi` targets because only tutorial `ProceduralVillageBuilder` created them and the prototype village is disabled for generated settlements.
3. Added `WorldLayoutPoiInteractionPolicy` explicit allow-list for seven existing server POIs and existing v1 goals/actions.
4. Added `WorldLayoutPoiTargetBuilder` presentation-only colliders at exact server Layout v2 coordinates; wired only for `layout.IsGenerated`.
5. Added pure C# allow-list regression tests and a dedicated Unity-independent CI step (all 23 steps passed in workflow run #43643297028).
6. Tavern status and E prompt/reason text wrapping plus shared typography tokens are included in the same PR.

## Authority and scope

- Unknown/unsupported POIs fail closed.
- No new intent, wire field, server-side action or local outcome.
- No tutorial-coordinate fallback for generated settlements.
- A player must aim and press E.
- Server response/reason remains authoritative.

## Remaining

- Grok/Claude independent confirmation of the seven intent mappings.
- Unity Editor 6000.6.7f1 compile and PlayMode acceptance in a generated settlement.
- Check physical occlusion and aimability of each POI, especially those near buildings.
- Full IMGUI typography normalization remains separate; this is not claimed complete.

## Acceptance

Only mark complete after owner tests E at generated tavern/smithy/market/garden/woods/river/mine and attaches Console/F3 evidence. CI alone is not runtime acceptance.
