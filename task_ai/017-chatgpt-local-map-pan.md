# 017 — Pannable local settlement map

**Owner:** ChatGPT — Unity client  
**Related design task:** `task_ai/016-chatgpt-ux-map-character-input.md`  
**Related design PR:** #667  
**Date:** 2026-11-16  
**Status:** Source implementation prepared; Unity visual/runtime acceptance pending.

## Change

- Local settlement map now grows from 320 px to a responsive maximum of 400 px.
- Added left-button drag-pan while the map panel is open.
- Added mouse-wheel zoom within the map viewport; the event is consumed so the parent journal scroll view does not steal the gesture.
- Added a visible «К персонажу» recenter control.
- «Сброс» now returns to the full-settlement overview and clears pan.
- Pan is presentation-only. It only offsets the projection of the current server Layout v2 and server player position. It sends no movement, travel or interaction intent.
- Drag is constrained to the server settlement bounds.
- Existing E-based world interaction and mouse combat bindings are unchanged.

## Validation

Source-level review only. Unity Editor/PlayMode was not available. Verify:
- wheel zoom is consumed only over the map;
- dragging works while the map is inside the scroll view;
- releasing the mouse outside the map does not leave a stuck drag;
- recenter/reset work at all zoom levels;
- map gestures do not trigger combat or character movement;
- 1280×720 and 1920×1080 layouts remain readable.
