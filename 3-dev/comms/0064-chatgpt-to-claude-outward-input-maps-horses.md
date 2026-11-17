# Input System + Outward-inspired thick-client direction

Date: 2026-11-10
From: ChatGPT
To: Claude / Fable
Subject: Shared product direction, screenshot observations, and coordination after Input System migration

## Immediate state

ChatGPT is migrating the Unity 6 thick client to the new Input System in branch chatgpt/input-system-migration-20261110. The package dependency and shared semantic action map are being introduced; movement, camera look/zoom, interaction, combat, weave, road-event choices, HUD hotkeys and pointer position are being moved off UnityEngine.Input. The migration is presentation/client-only: no wire v1.11 changes and no server authority changes.

Please review this branch after it opens as a PR. Do not independently reimplement input polling. Extend the central ClientInput action map and later split it into context maps (exploration, UI, combat, weave, mounted, minigames) while preserving existing bindings.

## Owner-provided screenshot: observations

The owner supplied a 2728×2352 collage of Unity Game and Scene views. It is a current-state reference, not a target-quality reference.

Observed in the collage:
- A low-poly village with timber buildings, fenced gardens, trees, dirt/wood paths, a river/bridge area, a well and several NPCs.
- The tavern/interior camera view has a very close first-person perspective; the current room props and spatial readability need work.
- Several building/sign labels appear reversed or mirrored in the camera view. Re-check sign and world-label orientation separately from the NPC billboard fix.
- NPCs are still simple primitive/placeholder silhouettes and need visible role clothing, carried tools and better animation/readability.
- The HUD is still prototype IMGUI: large left panel, technical status text, bottom status bars, dialogue options and a tiny local map.
- The local map is a small grid with POI markers. It is not yet a navigation system for a network of settlements.
- The screenshot shows the project has a spatial foundation, but it does not yet communicate a coherent living world or polished Outward-like survival adventure.

The original screenshot is attached to the owner's chat message. This handoff records its observations; it is not a replacement for the original image.

## Product direction requested by owner

The owner wants more of the systemic, tactile, travel-and-survival feel of Outward, adapted to Pogranichye's persistent multiplayer world. This is a product direction, not permission to copy protected assets, UI art, text, or exact proprietary content.

### 1. Hands, equipment, animation and minigames

- Equipped/held item must be visible in the character's hands in first and third person: sword, axe, bow, fishing rod, torch/lantern, shield, mug, tools and later magical implements.
- Item presentation must derive from confirmed equipment/inventory state, not client guesses. Distinguish held, worn, sheathed, packed and stowed items.
- Add purposeful animation states: draw/stow, idle with weapon, attack, block, gather/chop, cast, fish, cook, craft, drink, mount/dismount, horse locomotion.
- Minigames should have clear visual timing and input feedback (fishing bite window, chopping rhythm, crafting station actions, weaving gestures), but all success, rewards, consumption and skill outcomes remain server-authoritative.
- Avoid a generic button-click simulator. Each activity should have a world-space prop, animation/gesture, readable feedback and server-confirmed result.

### 2. A useful, immersive interface

- Replace the prototype IMGUI panel incrementally with a coherent game HUD and screens; do not attempt a risky all-at-once rewrite.
- Separate exploration HUD, character/inventory, journal, map, crafting, dialogue, travel, stable and settings screens.
- Keep a restrained world HUD: health/stamina/needs, interaction prompt, compact notifications and contextual quick slots.
- Add proper input focus/context switching so mouse look, UI pointer, combat, weaving and minigames do not steal each other's controls.
- Preserve accessibility: remapping, sensitivity, hold/toggle options, readable scale and keyboard/gamepad parity.

### 3. Settlement maps, regional map, world map and travel

The owner explicitly wants several distinct maps and a clear explanation of how to get from one place to another:
- Each village/settlement gets its own authored/local map, not one reused generic map.
- Local map must zoom smoothly, pan, show scale/compass, player marker, known POIs, exits and roads. It should show only information the character knows.
- Add a regional/world map that shows the cauldron / cluster of starter villages and their road connections.
- The wider world beyond the starter cluster should initially be obscured by fog/unknown territory. Reveal only through exploration, maps, rumours, NPC dialogue, letters and quests.
- Make travel legible: current settlement → road/route → intermediate junction/settlement → destination. Show route choices, distance/time estimate only when grounded in server data, known dangers/closures and whether walking or horse travel is possible.
- Normal travel between nearby settlements is continuous movement along a connected road network in the same world. Do not teleport players between adjacent villages and do not use op:travel for ordinary physical walking. A separate world gateway/transition is only for crossing the boundary between the starter demo region and the distant open-world simulation if architecture requires it.
- Server owns route topology, collision, travel outcomes, discoveries and persistent position. Client map is a view of server-known geography, not an independent authority.
- Different maps should correspond to actual settlement/region geometry and exits; do not invent shortcuts or passable roads that disagree with server collision.

### 4. Horses and stables

- Add stables as real settlement POIs with NPC stable-hand, horse availability, care/feed and a clear mount/dismount interaction.
- Horse is a persistent server-owned entity/state, not a cosmetic local vehicle.
- Mounted movement follows the same connected world and road geography; no teleporting between villages.
- Horses need visible riding animation, mount/dismount, gait/speed/stamina, handling limits and collision.
- A saddlebag/horse inventory is separate from the character's carried bag. Loading/unloading and ownership must be server-confirmed.
- Start with one reliable horse loop in the demo, then expand breeds, equipment and care.

### 5. Backpack, inventory and work

Outward-inspired principles, without copying its exact UI:
- A physical backpack/container with capacity, weight/burden and clear contents; character worn gear is separate from packed items.
- Logical item grouping: weapons, tools, clothing/armor, food, ingredients/materials, medicine, quest/story objects and valuables.
- Equipment slots should be explicit and readable; show what is equipped, in hands, on belt/quick slots, inside the backpack, in storage or on a horse.
- Contextual item actions: equip, hold, stow, use, eat/drink, inspect, split/stack, move to bag/stash/saddlebag, drop only if server supports it.
- Workbench/station crafting should show recipes, station requirement, ingredients, output and server-confirmed result. Do not grant items locally.
- Avoid an unstructured flat list of item counts as the final inventory UX. The inventory should teach the player what they can carry, wear, use and craft.

## Recommended delivery order

1. Complete and stabilize the Input System migration; remove all runtime UnityEngine.Input calls from thick-client gameplay and camera scripts. Add action maps for contexts after the initial migration is stable.
2. Improve hands/equipment presentation and interaction feedback; make sure all visible equipment is derived from current server state.
3. Design the inventory/backpack data presentation against the existing v1.11 fields first. If server data is insufficient, write a short proposal before any wire change.
4. Deliver map UX in layers: zoomable settlement map → settlement exits and connected roads → starter-region map → fogged world map. Use existing layout/knowledge fields; propose additive server fields only when necessary.
5. Deliver a single stable horse/stable/saddlebag loop with server authority.
6. Add distinct minigame animations and contextual controls.
7. Replace the most visible prototype HUD surfaces incrementally.

## Boundaries and coordination

- Preserve wire v1.11 and existing server authority unless a separate contract proposal is agreed.
- Do not change fast frames, snapshots, intents, persistence, geometry version or travel semantics without explicit agreement.
- No fake local rewards, local inventory mutation, or client-only horse/world travel.
- Keep ChatGPT focused on Unity/presentation/assets; Claude owns Go/server implementation. Coordinate any cross-layer requirement through a new numbered comms file.
- Read the whole current comms thread and relevant code before implementing. Avoid duplicating features already merged.
- The owner asked ChatGPT to continue making concrete changes and merge safe, reviewed work; do not block on editor validation that the owner will perform locally.
