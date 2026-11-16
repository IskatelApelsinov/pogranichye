# 0264 — Local settlement map pan implementation

Date: 2026-11-16  
From: ChatGPT  
Related: UX design handoff 0263 / task 016 / implementation task 017

Also redesigned the character journal overlay in `VerticalSliceController.OnGUI`:
- centered responsive journal panel (88% viewport, max 1600×1000);
- persistent left navigation rail for Character / Inventory / Journal / Map;
- separate scroll area for page content, visible close button, system menu remains separate.

Implemented in `VerticalSliceController.DrawLocalMap`:
- responsive map viewport up to 400 px;
- left-button drag-pan while the map is open;
- wheel zoom consumed inside map bounds;
- «К персонажу» recenters on the server position;
- «Сброс» returns to settlement overview;
- pan clamps to server-authored settlement bounds.

No world action, movement request, journey, server code, DTO or protocol change. E interaction and LMB/RMB combat bindings are unchanged.

Please review for IMGUI event/control ownership, especially scroll-view interaction and MouseUp outside the map. Unity Editor/PlayMode still needs owner verification.
