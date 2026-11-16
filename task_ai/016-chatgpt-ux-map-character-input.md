# 016 — UX pass: character journal, maps and contextual input

**Owner:** ChatGPT — Unity thick client / UX  
**Date:** 2026-11-16  
**Status:** In progress — design/audit first, then small implementation slices  
**Authority:** presentation and local input only. Server remains authoritative for position, known geography, actions, inventory, equipment and outcomes.

## Goal

Move the thick client away from prototype-like stacked IMGUI panels toward a coherent, readable adventure-game interface. Prioritize the character menu and map navigation, then simplify the control scheme. Do not replace the entire UI in one risky rewrite.

## Source audit findings

- `VerticalSliceController.DrawMapTab/DrawLocalMap` renders a 320 px square local map inside the character panel. Zoom is available (1×–4×), but at zoom >1 the map is automatically centered on the player; there is no independent pan offset or explicit recenter button.
- The regional map in `CoreGameplay.DrawRegionMap` has zoom controls and clips the projected graph, but no drag-pan. It is a projection of `me.region` only; keep unknown settlements hidden/unnamed and preserve server knowledge boundaries.
- Character UI is a 500 px left-side IMGUI panel with four tabs (summary, inventory, journal, map). It has basic scrolling but does not yet read like a complete character journal/inventory/map experience.
- `ClientInput` is already the single semantic input owner. Do not add a second input poller.
- World interaction is already E-based through `WorldInteractionProbe` and server-backed targets. Do not add click-to-interact or mouse-hover action execution.
- Mouse LMB/RMB are currently combat primary/guard; weaving also uses mouse tracing. These are distinct from clicking world objects to interact.
- F currently triggers `QuickAttack`, while primary attack is already LMB. This duplicates the attack layer and deserves a deliberate redesign; do not silently change it until the owner confirms the intended role and the server/client action semantics are checked.

## Target UX

### 1. Character journal/menu

Keep two distinct layers:
- **Character journal** (Tab): Character, Inventory, Journal, Map, Social/People (only where server data exists).
- **System menu** (Esc/F1): settings, controls, audio/video, account/server actions.

The character journal should feel like a practical medieval field book, not a debug dashboard:
- Large, readable overlay (roughly 80–90% of the viewport, responsive to 720p and ultrawide), with the world still faintly visible behind it.
- Persistent left navigation with clear text labels and selected state; content area uses the remaining space.
- Character page: identity/known name, visible equipment state, core attributes, needs/condition, money and current activity — only fields supplied by the server.
- Inventory page: clear separation between worn/equipped, held, backpack/carrying, storage and horse saddlebag; never imply an item moved/equipped until server confirms.
- Journal page: one clear current objective at top, full task details and history below; no mandatory quest framing.
- Social page: acquaintances/relationships only from server-backed knowledge; unknown NPCs remain anonymous.
- Every page has loading/empty/error/pending states, consistent close affordance, keyboard navigation and scroll preservation.
- Do not show unsupported stats or placeholder buttons just to fill space.

### 2. Maps

Make the map a usable navigation tool, not a static thumbnail:
- Local settlement map and regional map must each support pan and zoom.
- Local map: drag to pan while the map is open; wheel or +/- controls zoom; explicit **«К моему персонажу»** button recenters; reset-to-settlement overview is separate from recenter.
- Regional map: drag to pan; zoom; **«К текущему месту»** recenter; keep current position marker clear.
- Map gestures are UI-only and must never issue movement, travel or interaction requests.
- Roads, rivers, POIs, exits and player position come from validated server frames/layout. Client may project/render but must not invent routes, traversability, POIs or discoveries.
- Fog/unknown state is based on character knowledge. Never leak unknown settlement names or data from public render geometry.
- Show legend with both symbols and labels; scale and north indicator; labels must remain legible at 1280×720 and 2560×1440.
- Clicking a map marker may open a read-only information card only for known targets. It must not move/interact/teleport the character. Actual travel remains an explicit server-backed action where supported.

### 3. World interaction and mouse

- Keep one contextual primary world interaction on **E** (e.g. Talk, Gather, Fish, Buy, Open), selected by the current focused server-supported target.
- Do not make world interaction depend on clicking an NPC/building/ground with the mouse. Mouse pointer clicks belong to UI controls, not world POI activation.
- Keep camera mouse-look independent of RMB. Opening any UI releases cursor; closing UI restores gameplay capture only when safe.
- Combat mouse buttons are a separate question: current LMB attack/RMB guard are not world interaction. Do not remove or remap them as part of map/menu work without explicit approval.
- Keep mouse gestures inside an open map/menu isolated from gameplay, combat and camera.

### 4. F key / combat control cleanup

Current source binding: LMB = Primary, RMB = Guard, Q = Dodge, F = QuickAttack. The F quick-attack overlaps conceptually with primary attack and is a poor standalone discoverability choice.

Candidate roles to evaluate:
1. Draw / sheath the currently equipped weapon (useful in an immersive world; only if presentation and authoritative equipment state can be represented honestly).
2. Contextual secondary action only when the server exposes a distinct action (never generic duplicate interact).
3. Map recenter while the map is open (a useful context-sensitive shortcut, as seen in some RPG control layouts).

Do not commit to a new F binding until the owner confirms the desired role and existing server/equipment actions are checked. Keep the action map centralized in `ClientInput`.

## Reference patterns reviewed

- Valheim: E for contextual use, M for map, drag-to-pan and wheel zoom, LMB/RMB for attack/block, R for sheathe; demonstrates clear separation between world interaction and combat.
- The Witcher 3: E interaction, LMB attack, RMB parry/senses, I inventory, M map, J glossary; supports a consistent dedicated interaction key.
- Kingdom Come: Deliverance II: E use/talk/pickup, mouse attack/block, F special attack and F recenter-map shortcut in some UI contexts. Its context-specific map centering is a useful reference, but we should not copy its exact combat layout.
- These are control-pattern references only; do not copy proprietary UI art or exact layouts.

## Implementation order

1. Add map pan + recenter to local map; verify wheel zoom does not scroll the parent panel and dragging does not leak to gameplay.
2. Add pan + recenter to regional map with the same interaction vocabulary.
3. Restructure character journal navigation/layout without changing server contracts.
4. Audit every mouse-click surface and remove accidental world click-to-interact behavior; keep mouse controls that have a clear role (combat, UI, map).
5. Propose a final F binding after checking equipment/action support.
6. Add/extend pure C# regression checks for pan bounds/projection/input capture where feasible; then Unity Editor/PlayMode acceptance at 1280×720, 1920×1080 and 2560×1440.

## Acceptance criteria

- A player can zoom and pan both maps, then recenter reliably.
- Map drag/wheel never moves the character, attacks, guards, or activates a world POI.
- No mouse click on a world NPC/POI sends an action; E remains the contextual action key.
- Menus release the cursor and block camera/movement; closing returns to gameplay safely.
- Character menu is visually coherent, readable and clearly distinct from system settings.
- No client-invented map data, actions, items, discoveries or outcomes.
- No unsupported F action is introduced.
- Unity Editor/PlayMode validation is reported separately from source-level checks.
