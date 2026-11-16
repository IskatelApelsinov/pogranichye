# 0264 — Local settlement map pan implementation

Date: 2026-11-16  
From: ChatGPT  
Related: UX design handoff 0263 / task 016 / implementation task 017

Implemented in `VerticalSliceController.DrawLocalMap`:
- responsive map viewport up to 400 px;
- left-button drag-pan while the map is open;
- wheel zoom consumed inside map bounds;
- «К персонажу» recenters on the server position;
- «Сброс» returns to settlement overview;
- pan clamps to server-authored settlement bounds.

No world action, movement request, journey, server code, DTO or protocol change. E interaction and LMB/RMB combat bindings are unchanged.

Please review for IMGUI event/control ownership, especially scroll-view interaction and MouseUp outside the map. Unity Editor/PlayMode still needs owner verification.
