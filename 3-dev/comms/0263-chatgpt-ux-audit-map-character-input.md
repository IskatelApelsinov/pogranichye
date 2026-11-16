# 0263 — ChatGPT UX audit and next implementation slices

Date: 2026-11-16  
From: ChatGPT  
To: project owner / Claude (coordination only)

## Owner request

Prioritize user experience today:
- improve the character menu and how character/map information is presented;
- make maps movable/pannable, not just zoomable;
- rethink mouse interactions;
- replace the current F quick-attack binding with a more useful action after review;
- compare interaction/control patterns with other immersive RPGs.

## Current findings

1. Local settlement map in `VerticalSliceController.DrawLocalMap` is a 320 px map. It supports 1×–4× zoom, but zoomed mode automatically follows/centers the player and cannot be independently panned. It needs drag-pan and a visible recenter control.
2. Regional map in `CoreGameplay.DrawRegionMap` supports zoom and clipping but no pan. It should receive the same pan/recenter interaction model.
3. Character journal is still a 500 px IMGUI left panel with summary/inventory/journal/map tabs. It needs a proper responsive journal layout and a clearer separation from system settings.
4. `ClientInput` is the shared semantic input layer; keep it as the only input polling owner.
5. World interaction is already E-based via `WorldInteractionProbe`. Mouse clicks should not activate NPCs/POIs or issue world actions. Keep contextual E prompts.
6. Current combat mapping is LMB primary, RMB guard, Q dodge, F quick attack. F duplicates the attack conceptually and should be redesigned, but not blindly rebound before checking equipment/server semantics.

## Reference games / patterns

- Valheim: E interact, M map, drag map with left mouse, wheel zoom, LMB attack, RMB block, R sheathe weapon.
- The Witcher 3: E interact, LMB fast attack, RMB parry/senses, dedicated inventory/map/journal keys.
- Kingdom Come: Deliverance II: E use/talk/pick up; F has context-sensitive utility in some UI screens (e.g. center map) and is also used for special combat in some contexts.

Take the design principles, not proprietary visual assets or exact UI.

## Proposed design

- Character journal (Tab): distinct from system settings (Esc/F1); persistent left navigation and responsive main content area; Character / Inventory / Journal / Map / Social where supported by server state.
- Local and regional maps: drag-to-pan, wheel or +/- zoom, visible «К моему персонажу» / «К текущему месту» recenter controls, reset overview, legend, north and scale. Map gestures must be UI-only.
- World interaction: E only for focused server-supported targets; no mouse click-to-interact. Mouse remains available for UI controls, map panning and the separate combat layer.
- F: evaluate weapon draw/sheath, contextual secondary action, or map recenter; choose only after confirming desired role and existing support.

## Important boundary

This is client UX work. Do not change the server protocol or authority model. Map content and knowledge remain server-supplied; client only projects it. Do not infer that public render geometry is character knowledge. No click or map gesture may send movement or gameplay action.

## Next work

ChatGPT owns the Unity implementation. Claude is asked only to review any contract/equipment implications if a proposed F action requires server support. No server changes requested at this stage.

Task: `task_ai/016-chatgpt-ux-map-character-input.md`.
