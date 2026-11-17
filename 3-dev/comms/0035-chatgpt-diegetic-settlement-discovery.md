# Pogranichye — diegetic settlement discovery, rumours and quest knowledge

Date: 2026-11-02  
Owner: ChatGPT / Unity thick-client design  
Status: design proposal for owner + Claude review; **no runtime or protocol change authorized**

## Core principle

The player does not begin with a complete world map or an omniscient list of settlements. The character learns about the world through people, tavern conversations, travellers, trade, letters, quests, signs and personal travel.

A settlement becomes part of the character's known world only when the character receives credible information about it. Actually visiting it is a separate state from merely hearing about it.

## Knowledge states — keep them distinct

Each settlement should have player-character-specific knowledge with separate facts, rather than one simplistic discovered/not-discovered boolean:

- **Rumoured** — the character has heard that a settlement or place exists. The name may be uncertain or absent; location is approximate or unknown.
- **Located** — the character has received enough credible directions, a map, a route description or coordinates to place it on their map. The marker may remain approximate.
- **Visited** — the character has physically reached the settlement. This is a server-confirmed exploration fact.
- **Personally known** — the character has meaningful local knowledge: people, services, dangers, politics or routes. This should grow through visits, quests and repeated contact, not be granted automatically on first discovery.

These are not necessarily a strict linear ladder. A traveller might give precise directions (Located) without a reliable name (Rumoured), while a famous city may be widely known but never visited. Store the actual knowledge facts and derive UI presentation from them.

## How the player learns about settlements

1. **Tavern conversations**
   - Locals discuss nearby villages, distant towns, roads, markets, rulers, shortages, festivals, dangers and recent events.
   - Ambient conversations can be overheard, but only relevant/credible information should become a journal/map knowledge entry. Do not turn every background bark into a quest marker.
   - A conversation can reveal only part of the truth: a settlement's existence, a direction, a local nickname, a road hazard, or a rumour about an event.
   - Repeated conversations can corroborate, correct or contradict rumours. Preserve uncertainty rather than silently converting every rumour into fact.

2. **Quests and letters**
   - A quest can reveal a settlement, identify a person there, provide a route, or establish a concrete reason to travel.
   - A letter or delivery request can provide a destination name and recipient without necessarily revealing the entire map or all local services.
   - Completing a quest can upgrade knowledge: e.g. from Rumoured to Located, or from Visited to Personally known.
   - Quest knowledge should be granted by authoritative quest state/events, not because the client has loaded a scene or received hidden world data.

3. **Travellers, caravans and guides**
   - Travellers can reveal routes, distances, seasonal closures, tolls, bandit activity and local names.
   - A guide may offer a reliable route only after trust/payment/quest conditions are met.
   - Caravans can make an unknown route physically accessible, but the player should still learn where they are going through dialogue or travel context.

4. **Physical exploration**
   - Reaching a settlement confirms Visited, even if the player has not spoken to anyone there.
   - Discovering a road, bridge, ford, ruin or landmark can add a separate map fact without revealing every settlement connected to it.
   - Avoid auto-revealing the whole region map just because the player crossed one border or entered a scene.

## Map and journal presentation

- The world map should be incomplete at the beginning. No global list of all settlements, exact coordinates, quest destinations or services.
- Rumoured places can appear as vague handwritten notes, question marks or a broad directional region only if the character has enough information to represent them. Do not show exact pins from vague gossip.
- Located settlements can appear as approximate or confirmed map markers depending on source confidence.
- Visited settlements can use a distinct visual treatment and may expose only the local map the character has actually explored.
- Personally known places may reveal useful local details (inn, smith, market, guard post, local contacts) through visits, conversations or quests.
- The journal should preserve provenance in natural language: 'A merchant at the tavern said…', 'The miller warned me…', 'I delivered the letter to…'. It should not expose database IDs, raw activity codes or debug labels.
- Do not force a modern quest tracker with omniscient objectives. If a player wants help, the journal can summarize what their character has actually been told.

## Credibility, provenance and uncertainty

Information should carry at least:

- source (NPC, quest, letter, sign, map, personal observation);
- when learned (world time);
- confidence / certainty;
- the specific facts learned (existence, name, direction, route, danger, service, person);
- whether later information corrected or contradicted it.

NPCs should not be universal databases. Their knowledge depends on home, occupation, travel, faction, relationships, recent events and believable information flow. A fisherman may know river crossings and nearby villages; a merchant knows trade routes and prices; a guard knows patrols and threats; a traveller knows distant roads. They can be wrong or out of date.

## Multiplayer and persistence

- Knowledge belongs to a character/player profile, not globally to the whole server or party by default.
- If one player learns a destination, another player should not automatically receive it. Sharing requires a conversation, map handoff, party share mechanic or another explicit server-confirmed event.
- Party members may share a journal/map entry only if the game explicitly supports shared knowledge and the server records that transfer.
- Knowledge persists across reconnects and sessions.
- Server owns the authoritative facts and grants; Unity renders only the knowledge provided for that character.

## Relationship to NPC familiarity

Settlement knowledge and NPC name familiarity are related but distinct:

- Knowing that 'a village beyond the eastern ford' exists does not mean knowing any resident's name.
- Knowing an NPC's name does not automatically reveal their home settlement or the route there.
- A rumour about a bandit attack may identify a settlement without identifying the victim or suspect.
- A quest may reveal a recipient's name and destination together, but each fact should be explicit.

Coordinate this proposal with PR #25 (NPC identity/familiarity) and PR #18 (thick-client opening mission). The opening letter to the neighbouring town is a good first example: the player learns the town and recipient through an NPC/letter, receives only the route information that the NPC can credibly provide, and confirms the town as Visited upon arrival.

## Questions for Claude's design review

1. What persistent per-character knowledge/discovery structures already exist, if any?
2. How are settlements, world regions, POIs and routes currently identified? Which IDs are stable across restarts?
3. Propose the smallest server-owned data model for settlement knowledge facts and provenance without exposing undiscovered world data in snapshots.
4. How should quest completion, dialogue, overheard rumours, letters and physical arrival grant or update knowledge idempotently?
5. How can NPC knowledge be role-, origin-, travel- and relationship-aware without building a huge expensive simulation?
6. What is the minimum v1-compatible path for a demo, and what requires a later protocol version?

**Do not change runtime, persistence schema or wire protocol until the owner approves the concrete proposal.**

## Acceptance examples

- A new character opens the map and does not see every settlement in the world.
- A tavern conversation reveals a rumour about a settlement, but only vague information is recorded if the speaker cannot give reliable directions.
- A quest giver provides a named destination and a letter; the journal records the source and the known destination, not the entire destination's local map.
- Reaching the destination marks it Visited, even if the player avoids all conversations.
- A second player does not inherit the first player's discoveries without an explicit sharing mechanic.
- Contradictory rumours remain visibly uncertain until corroborated or corrected.
- No client-side scene loading, hidden snapshot fields or debug data silently grants player knowledge.