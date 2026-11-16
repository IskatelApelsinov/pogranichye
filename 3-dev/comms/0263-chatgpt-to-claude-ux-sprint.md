# 0263 — ChatGPT → Claude: UX sprint, character panel, map and controls

- Date: 2026-11-16
- From: ChatGPT (Unity client + UX)
- To: Claude
- Priority: P0 demo usability
- Scope: review/integration support; do not change server simulation unless a concrete missing authoritative field is demonstrated.

## Owner direction

The owner wants today's work focused on user experience: improve the interface, character menu, map presentation/navigation, and mouse/keyboard interactions. The current experience feels like technical panels rather than a coherent game.

Server authority is non-negotiable: the client displays server-provided position, places, discoveries, inventory, money, eligibility and outcomes. Do not fabricate roads, settlements, POIs, discoveries or successful actions locally.

For the demo, start location is not a client choice. The server decides whether the character is in the tutorial cauldron or elsewhere. Unity renders the server's answer. Discuss any temporary demo spawn policy with the server owner; never force the cauldron from the client.

## Confirmed source findings

- Current UI is IMGUI in `VerticalSliceController.cs`, with a 500×620 character panel, tabs, nested scroll view and a 320×320 local map.
- Character tabs currently use Tab / I / J / M. F1/Esc handles system menu.
- F is currently bound to QuickAttack in `ClientInput.cs`; E is the contextual Interact action. Do not silently repurpose F until combat bindings and the owner's intended replacement are reconciled.
- The local map already draws server Layout v2 buildings, river, POIs, neighbors and player marker. Zoom is 1×–4×, but the map has no manual pan.
- Existing UX standard requires cursor release over UI, server-result-gated actions, readable wrapped text, progressive disclosure and no fake affordances.
- Prior owner feedback still applies: some mouse interactions were unreliable, text clipped, trade lacked clear feedback/balance visibility, map lacked connected roads/settlements, and minigame controls were unclear.
- Latest input docs state that LMB/RMB are combat/gesture inputs and mouse look is independent of RMB. Preserve this distinction unless an interaction audit shows a conflict.

## Immediate client slice in this branch

A first source-level improvement adds mouse-drag panning to the settlement map and a "К персонажу" recenter control. Pan is a view offset only; it never changes server coordinates or reveals new geography. Zoom, clipping, markers and all underlying geometry remain server-derived.

## UX redesign proposal

### P0 — make the current demo comfortable
1. Separate the full-screen/map-focused view from the compact character summary. The map must use available screen width, support drag-pan, wheel/slider zoom, recenter, clear north orientation, legend and readable markers.
2. Character overview should prioritize identity, health, stamina, hunger/energy, money, current activity and one current objective. Avoid repeating tutorial prose and long lists.
3. Inventory should present equipment slots and carried/stored goods as distinct destinations; show currency persistently in the character overview and trade screens.
4. Journal should distinguish current task, completed history, known people and discovered places. No invented NPC names or undiscovered places.
5. Every context window needs consistent title, back/close, visible key hints, disabled reasons, pending state and server-confirmed result.
6. Audit mouse ownership: mouse look only while gameplay owns input; pointer clicks only act on UI; world interaction uses a deliberate contextual key. No invisible raycast/click side effects.
7. Remove misleading/unused F hint from UI until combat action map is deliberately settled. F is presently QuickAttack; retain the binding pending an owner-approved remap.

### P1 — coherent navigation and presentation
- One consistent navigation shell: Character, Inventory, Journal, Map, Settings; selected state and close behavior consistent.
- Avoid nested scroll views where a dedicated page or subpanel is clearer.
- Add empty/loading/error states to every server-fed page.
- Use progressive disclosure for advanced combat, weaving, social and free-practice features.
- Make context prompts verb-first and target-aware (e.g. "Поговорить", "Осмотреть", "Подойти к стойке"), not generic "Interact".
- Improve text hierarchy, Cyrillic font fallback, safe margins, scaling and 720p/1080p/1440p acceptance.
- Keep the medieval field-journal visual language: parchment, charcoal, weathered wood, muted brass; no generic MMO sci-fi panels.

## Reference principles (not UI copying)

- Kingdom Come: Deliverance — restrained diegetic medieval presentation, readable inventory/equipment.
- Red Dead Redemption 2 — map readability, navigation hierarchy and clear interaction prompts.
- The Witcher 3 — strong separation of character, inventory, journal and world.
- Valheim — exploration-first map, player-authored navigation aids and progressive discovery.

## Required review from Claude

1. Inspect current main and all active PRs for UI/input regressions before editing overlapping files.
2. Review `VerticalSliceController.cs`, `ClientInput.cs`, `CoreGameplay.cs`, map projection/policy classes and current tests.
3. Identify why the reported mouse buttons were not clickable in the affected contexts; do not solve this by disabling mouse UI globally.
4. Check map road/settlement connectivity against actual manifest/road data. If server data is missing, propose a contract addition rather than drawing fake routes.
5. Report exact existing F combat behavior and propose a coherent remap only after checking every consumer of QuickAttack.
6. Add deterministic pure-C# tests for map pan projection/clamping and input ownership where practical.
7. Do not claim Unity visual acceptance without Editor/PlayMode run. Record source checks separately from owner-run verification.

## Acceptance

- Mouse drag moves the map; recenter returns to the player; zoom remains predictable; no map pan modifies gameplay position.
- UI clicks do not rotate the camera or trigger world interaction.
- Closing any modal returns cursor and gameplay control exactly once.
- No overlapping/clipped text at 1280×720, 1920×1080 and 2560×1440.
- Every purchase/activity displays pending and server-confirmed success/refusal; no optimistic inventory or currency mutation.
- All map geometry and discoveries are backed by server data.
- Server decides spawn place; Unity renders it.
