# Pogranichye — Unity gameplay direction proposal

Date: 2026-11-02  
Owner: ChatGPT / Unity thick-client track  
Status: proposal for owner + Claude review; **not a protocol change**

This note captures a direction for the Unity client and the first playable test build. It does not authorize changes to the v1 snapshot or intent envelope. Server-side outcomes remain authoritative.

## 1. Camera and player embodiment

The agreed direction is hybrid:

- First-person is the primary exploration and combat camera.
- Third-person is available for social scenes, equipment/appearance inspection, and selected domestic or crafting actions.
- The full 3D player entity remains networked and represented in the world in both modes.
- Camera switching must not create a second player representation or alter authoritative position.
- Local camera look is presentation/input only. Movement, collision, stamina, attacks, inventory, and interaction outcomes must be server-authorized once those intents exist.

Current Unity work adds a prototype first-person camera mode: hold RMB and move the mouse to look; the player root rotates in yaw and only the arms remain rendered. Third-person remains the default. This is a camera/presentation prototype, not yet networked locomotion.

## 2. Scale and spatial conventions

Proposed Unity convention: **1 Unity unit = 1 metre**.

- Humanoid target height: approximately 1.8 m.
- Door height: 2.0–2.2 m; ordinary interior ceiling: 2.5–3.0 m.
- Tavern counter: about 1.1 m; table: 0.75–0.85 m.
- Interact range should be short and contextual (roughly 2–3 m), not a long-distance click on a map marker.
- The current village is a compact tutorial diorama, not a final open-world scale reference. Use a clear playable village core and later stream/compose larger regions from server metadata.
- Server coordinates and client presentation geometry must be kept separate until canonical world metadata is agreed.

## 3. Interaction model

Move from clicking distant floating POIs toward a first-person contextual interaction loop:

1. Camera ray checks a small centre reticle.
2. A nearby valid target shows a short prompt (NPC name/role, door, station, item).
3. Press interact to send a typed intent.
4. UI shows pending state; only a server snapshot/event confirms success or failure.
5. Interaction can be cancelled; no client-side inventory or world mutation.

NPC selection currently sends only the existing generic **socialize** goal. It is not targeted dialogue: v1 does not transmit a target entity ID with the goal. A later contract should include a target entity ID and a typed interaction payload.

## 4. Social play and NPCs

The village should feel inhabited rather than like a set of static quest markers:

- NPC daily routines: work, eat, rest, socialize, move between canonical server POIs.
- Contextual conversations: greet, ask about work, trade, request help, learn rumours. Dialogue choices are client presentation; server validates consequences and relationship changes.
- Relationships should be legible but not reduced to a single arbitrary “friendship” bar: trust, familiarity, reputation, obligations/debt and faction attitude can be separate server-owned values.
- Social camera can shift to third person or a close conversational framing, but should never take control away without a clear input.
- In the first tutorial, use a small cast with distinct roles and repeatable names/IDs from the fixed starter seed.

Current client has selectable NPC prototypes. Clicking one sends **socialize**, with UI text explicitly noting that addressable dialogue is not yet part of v1.

## 5. Tavern and beer — mandatory test-build feature

**Acceptance requirement:** the player must be able to order/buy and actually drink a beer in the tavern. A decorative mug or a generic socialize action alone does not satisfy this requirement.

Minimum playable loop:

1. Approach the bar / interact with the innkeeper.
2. See beer availability and price.
3. Confirm purchase/order.
4. Server checks stock and coins, then records the serving/consumption transaction.
5. Unity plays serving and drinking animation, mug hand attachment, sound and concise feedback only after the authoritative result.
6. Server exposes a small intoxication state (or equivalent status) in the snapshot/event. Unity may apply mild camera sway, altered post-process/blur, or reduced steadiness based on that server value; it must not invent the intoxication value locally.
7. State decays according to server time/rules. Drinking again must respect stock, money, and any gameplay limits.

The current v1 goals are **socialize**, **work**, **eat**, and **rest**; there is no confirmed beer/drink intent. Claude has been asked in PR #11 to propose the smallest server-authoritative contract and backward-compatible optional snapshot field or a separately agreed v2. Do not silently map beer consumption to **socialize** or **eat**.

## 6. Combat direction

Target feel: deliberate, readable, dangerous medieval melee — more tactical survival sim than arcade click-spam.

- First-person hands/weapon presentation with optional third-person body feedback.
- Explicit combat intents: equip, attack (attack type/direction), guard, feint, dodge/step, release/stop guard. Exact envelope requires server design.
- Server validates weapon, stamina, timing, range, facing, target, armour, wounds and hit result.
- Client may predict animation and camera response, but not damage, hit confirmation, stamina cost, death or loot.
- Use clear attack wind-up / active / recovery phases and readable audio/animation telegraphs.
- Stamina and wounds should matter; avoid infinite blocking and weightless rapid attacks.
- Start with one training weapon and a training dummy; then a controlled NPC sparring test. Do not put unrestricted PvP in the first village build.

## 7. Crafting and work

- Stations define context: forge, workbench, cooking hearth, herbalist table, building site.
- UI previews recipe, required ingredients, output, time and tool/station requirements.
- Server owns recipe validity, item counts, quality, durability, output and ownership.
- Client owns interaction presentation: station camera, progress display, hand/tool animation, particles, sound.
- Work goal in v1 remains a generic activity and must not be presented as a fully implemented crafting transaction.
- First slice: one simple recipe at the smithy after inventory/item contract is available; one cooking/beer-related tavern flow.

## 8. Magic direction

Magic should feel like constructing and controlling a pattern, not selecting a spell from a static hotbar.

- Compose a spell from nodes (source, element/force, shape, target, modifier) and links.
- First-person: trace/aim the pattern in the world; third-person or a dedicated focus view: inspect/edit the node graph.
- Server validates learned nodes, links, costs, range, target legality, cooldown, interruption and final effect.
- Client previews the pattern and renders effects; server owns effect, damage, duration, resource consumption and failure.
- Early test: one harmless utility spell and one simple offensive spell in a controlled test arena. Keep magic separate from the tavern/social slice.

## 9. Milestone order

1. Tutorial village readability, hybrid camera, local interaction prompts and NPC selection.
2. Tavern interior and the server-backed beer purchase/drink loop (mandatory test-build acceptance).
3. Social dialogue target contract and a few repeatable NPC routines.
4. Server-backed movement/interaction and canonical POI coordinates.
5. One complete crafting recipe.
6. One controlled melee combat loop.
7. First composable magic prototype.

## 10. Contract and ownership boundaries

- Do not change v1 envelope/snapshot silently.
- Claude owns server simulation, world generation, inventories, transactions, combat, crafting, magic and authoritative NPC state.
- ChatGPT owns Unity rendering, input presentation, animation, camera, HUD, interaction affordances and protocol adapter.
- New intents/fields must be proposed in a separate PR or design thread and agreed before Unity integration.
- All PRs remain draft until the project owner’s review.
