# Pogranichye — diegetic NPC identity and familiarity

Date: 2026-11-02  
Owner: ChatGPT / Unity presentation track  
Status: design proposal for owner + Claude review; **no runtime or protocol change authorized**

## Core rule

Pogranichye should not explain its world through floating labels. The player learns people by meeting them, hearing about them, remembering them and recognizing them.

**An NPC's personal name is shown to the player only after the player character has become acquainted with that NPC.** Until then, the person remains visually present and interactable, but their personal name is not displayed in a nameplate, subtitle, prompt, map label or other UI surface.

When a name is known, show only the name (or first name + surname where culturally appropriate) as a restrained, unobtrusive subtitle/name cue. Do not append role, current activity, goal, mood, faction, health or debug data.

## Presentation rules

1. **Unknown NPC**
   - No floating nameplate and no personal name in contextual prompts.
   - No generic replacement label such as 'Unknown NPC' hovering over the head.
   - The NPC is still visible, has a real appearance, moves and behaves normally, and can be approached.
   - Contextual interaction affordance can be generic (for example, an unobtrusive interaction glyph / 'Поговорить' when in range), but must not reveal their name.
   - The player may infer occupation from clothing, tools, location, actions, speech and environmental storytelling—not from a permanent role label.

2. **Known / introduced NPC**
   - Show only the known personal name as a subtle subtitle/name cue while the NPC is in a sensible presentation range and visible/targeted.
   - Do not keep names permanently floating across the whole village. Prefer a short fade-in/fade-out on entering readable range or when the player focuses/looks at the person.
   - No role or activity text is appended. The world communicates activity through animation, props, sound, posture and consequences.

3. **Player character**
   - Never render the local player's own name above their own body in first-person or third-person.
   - Other players' identity rules are a separate multiplayer/social design decision; do not infer them from NPC familiarity.

4. **Dialogue**
   - Before introduction, dialogue UI must not silently reveal the NPC's personal name in a header or choice text.
   - A natural introduction ('Меня зовут…') or a credible third-party introduction can establish familiarity.
   - Once introduced, later dialogue can address the person by name naturally.
   - The dialogue system may show spoken subtitle text when dialogue is implemented; that is distinct from a permanent nameplate and must not become an activity/debug feed.

## Familiarity is authoritative knowledge, not client guesswork

- Familiarity/known-name state must ultimately be server-owned and associated with the player-character ↔ NPC stable IDs.
- Do not set familiarity merely because the NPC entered view, was nearby, was selected, or appeared in a snapshot.
- Do not infer familiarity from the fact that the client has received the NPC's internal name field.
- Suitable future ways to learn a name include direct introduction, a credible introduction by another character, or a server-confirmed story/dialogue event. Rumours may reveal a person exists or is associated with an event without necessarily revealing their exact identity.
- A name known to the player should persist across sessions and reconnects.
- If the current v1 snapshot has no familiarity/known-name field, keep the presentation conservative: do not invent persistent knowledge locally. Propose an optional backward-compatible field or agreed v2 contract before implementing server-backed gating.

## Data / protocol questions for Claude

Please review the current server character/neighbor snapshot and relationship model, then propose:

1. The smallest server-owned representation of whether this player knows this NPC's personal name (keyed by stable player and NPC IDs).
2. Whether this belongs in a per-player known-person/relationship record, a familiarity dimension, or a dedicated name_known bit. Avoid conflating name knowledge with friendship, trust, reputation or faction attitude.
3. A backward-compatible snapshot representation that does not expose unnecessary private identity data to clients who do not know the NPC. If the server must send an internal entity identifier, that is not permission to display its personal name.
4. Authoritative events/actions for introductions and credible third-party introductions, with persistence and idempotency.
5. How the server should represent NPCs whose names are genuinely unknown, aliases, titles, renamed characters, or disputed identities.
6. What existing v1 behavior can safely remain until the contract is agreed.

**Do not change runtime, persistence schema or wire protocol in response to this note until the owner approves the concrete proposal.**

## Unity implementation guidance after approval

- EntityViewRegistry should not blindly call SetLabel(view, neighbor.Name) for every neighbor.
- Prefer no label object/rendering when nameKnown != true; enable it only for known NPCs and only under the presentation visibility/focus rule.
- Do not use role/activity as fallback label.
- The client may locally decide only whether/when to fade a server-authorized known name cue based on camera visibility and distance. It may not decide that the name has become known.
- Unknown or missing familiarity data must fail closed (hide personal name), not fail open.
- Keep a debug-only inspector/log path separate from player-facing UI.

## Acceptance criteria

- A newly encountered NPC has no personal name visible anywhere in normal gameplay UI.
- Merely looking at, approaching, selecting or receiving snapshots for that NPC does not reveal their name.
- After a server-confirmed introduction, only that NPC's name appears in the subtle name cue.
- Reconnect preserves the known-name state.
- No role/activity/goal/debug text appears over NPCs.
- Missing/unknown familiarity data hides the name safely.
- No change to v1 wire contract is made without explicit approval.

## Relationship to current work

- Complements PR #23 (technical activity codes): activity codes drive presentation only; no activity prose is shown to the player.
- Refines PR #24 (remove role/activity nameplate text): the final intended behavior is stricter than showing every NPC's name—only names the player character actually knows may appear.
- This is a design proposal, not a reason to merge a client-only guess or expose names prematurely.