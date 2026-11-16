# Client UI/UX standard — Pogranichye

**Status:** active client direction, 2026-11-15  
**Owner:** ChatGPT — Unity client + UI/UX  
**Scope:** Unity 6 thick client, URP, keyboard/mouse first; gamepad/accessibility later.  
**Authority:** this document governs presentation and interaction design. Server remains authoritative for world state, eligibility, outcomes, prices, inventory and social actions.

## Product experience

Pogranichye is a lived-in world, not a stack of debug panels. The interface should feel like a restrained medieval field journal and practical adventurer's kit: readable, quiet, tactile, atmospheric. It must not imitate a generic sci-fi MMO HUD or cover the settlement.

The first 10 minutes should teach the player by doing:
1. arrive and understand where they are;
2. see one clear next step, not a wall of instructions;
3. approach a person or place and understand the available interaction;
4. receive immediate, readable feedback for pending / accepted / refused / timed-out actions;
5. understand what changed and what to do next;
6. find deeper systems in the journal, inventory or activity window when they ask.

## Non-negotiable UX rules

- **One primary action in context.** Show the most relevant action at the focused POI/NPC. Secondary actions belong in the activity window, not a pile of competing buttons.
- **Never hide server truth.** Do not show success, spend currency, add items, change morale or trigger social presentation until the matching server result/state arrives.
- **Explain refusals.** Show the complete server reason, wrapped to multiple lines. Never reduce it to a generic "failed" or silently clip it.
- **Make latency visible.** A submitted action should enter a clear pending state; disable duplicate submission for that request; show confirmed / refused / timed out distinctly.
- **Context before memorization.** Prefer [E] Talk, [E] Fish, [E] Buy beer over requiring the player to remember G/J or search a menu. Keep combat and weaving shortcuts as a secondary layer.
- **Progressive disclosure.** HUD = essential now; context prompt = what can I do here; journal = what am I working toward; inventory = what do I carry; map = where can I go; debug = opt-in only.
- **Respect the view.** No permanent full-screen panels. Keep critical prompts clear of reticle, status bars, quest tracker and subtitle area.
- **Readable at real resolutions.** Support 1280×720 through 2560×1440; wrap long Russian text; use consistent type tokens; avoid tiny 8–10 px gameplay text. Debug may be smaller but must remain opt-in.
- **Anonymous until known.** Do not reveal unknown NPC names in nameplates, dialogue labels or interaction prompts.
- **No fake affordances.** Disabled actions explain why; unavailable actions are not rendered as clickable buttons.
- **No invented systems.** UI can expose only server-supported actions and data. Presentation-only effects never become simulation outcomes.

## Visual language

- Palette: warm parchment / weathered wood / charcoal / muted brass. Use color semantically: warm gold for focus/primary, green for confirmed, muted red for refusal/danger, neutral for information.
- Typography: one Cyrillic-capable font family with fallback. Shared tokens: title 18–22, body 15–16, secondary 13–14, debug 10–11. World labels use a consistent distance-aware scale.
- Surfaces: translucent dark wood/charcoal panels with restrained borders and padding. Avoid opaque boxes around every line.
- Motion: short fades/slides for opening, closing and feedback; no constant pulsing or excessive motion. Respect reduced-motion setting when introduced.
- Icons supplement text; never rely on icon-only meaning for important actions.

## Interaction and feedback contract

Each server-backed request must have four visually distinct states:
1. **Ready:** action and key shown; requirements visible where useful.
2. **Pending:** action visibly in progress; duplicate click/keypress prevented by RequestGate.
3. **Accepted:** concise confirmation, then UI reflects the next authoritative snapshot/result.
4. **Rejected / timed out:** complete reason shown with a useful next step where the server reason allows it.

A toast must size to its content and wrap. Long reasons may occupy multiple lines; the UI must not clip them at a fixed one-line height. Keep a persistent copy in the journal/event history where relevant.

## Current near-term UX backlog

### P0 — first-play clarity
- [x] Context prompt supports dynamic multi-line height and screen width.
- [x] Action-result toast wraps server reasons and sizes to content.
- [x] Expose the shared RequestGate in-flight action and queue count in a compact persistent pending banner. Button-level duplicate prevention remains a per-control follow-up.
- [x] Show one concise server-backed first-play objective while the character journal is collapsed; keep the detailed explanation in the journal.
- [x] Simplify the optional H-toggle strip to basic first-play controls; advanced combat/weave bindings stay in the detailed controls help. A complete context-aware per-mode strip remains follow-up.
- [ ] Check prompt/toast/quest tracker/status bars for overlap at 720p and ultrawide.

### P1 — coherent navigation
- [ ] Normalize tab names and key hints: Character, Inventory, Journal, Map, Settings.
- [ ] Ensure Tab opens/closes the expected player panel and Esc opens settings without trapping the cursor.
- [ ] Make all panels scrollable, preserve scroll position and expose empty/loading/error states.
- [ ] Add consistent close affordance and visible selected-tab state.
- [ ] Surface map legend and current-location marker without relying on color alone.

### P1 — world interaction
- [ ] Context prompt uses action verbs and the correct target label; do not always say generic "Interact".
- [ ] Focus highlight is subtle and readable in daylight/night, never a glowing outline through walls.
- [ ] Keep NPC identity rules: unknown = "житель", known = server-provided name.
- [ ] Make market, fishing, woodcutting, fieldwork and tavern flows understandable from context, with deeper controls in panels.
- [ ] Show requirements and server refusal reasons without implying the client can guarantee eligibility.

### P2 — polish and accessibility
- [ ] Audit all IMGUI font sizes and world TextMesh labels; remove inconsistent one-off values.
- [ ] Verify Cyrillic glyph coverage and fallback in every text surface.
- [ ] Add scalable UI setting (100/110/125%) and save it locally.
- [ ] Add remappable controls and gamepad navigation after keyboard/mouse flow is stable.
- [ ] Add subtitle settings (size/background/speaker labels) and safe-area handling.
- [ ] Check color contrast, focus states, keyboard navigation and reduced motion.

## Acceptance checklist

For every screen and prompt:
- At 1280×720, no text overlaps or clips; long Russian server reasons wrap.
- At 1920×1080 and 2560×1440, typography remains proportional and not too small.
- Every actionable control has an enabled, disabled-with-reason, pending or unavailable state.
- Every server-backed success/refusal is driven by the matching server response.
- Mouse cursor is available while a panel/chat is open and reliably re-locks only when gameplay resumes.
- No UI panel blocks movement or interaction unexpectedly.
- The same action is named consistently across HUD, prompt, journal and result feedback.

## Work note — 2026-11-15

Source audit found two concrete clipping defects: the interaction prompt used a fixed 46 px height with clipping, and action-result feedback used a fixed 300×28 px box. Both were changed to content-sized, wrapped surfaces in PR #642. This is a source-level fix; resolution/PlayMode visual acceptance remains outstanding.


## Work note — 2026-11-15, iteration 2

The gameplay HUD now reads the existing shared `RequestGate` through read-only `CoreGameplay` properties and displays the in-flight action plus queued count. It does not predict success and clears only when the matching result resolves, the gate times out, or disconnect clears it. A compact first-play objective card appears when the journal is collapsed and derives from the existing server task/acquaintance state. The pending banner is placed below the nearby-player invite panel when that panel is visible. Source-level implementation only; Unity resolution/PlayMode acceptance remains outstanding.


## Work note — 2026-11-15, iteration 3

The optional H-toggle hint strip was reduced to first-play essentials (movement, mouse look, E interaction, sprint/jog/jump and journal navigation). Combat and weaving shortcuts were removed from this small overlay so the first minutes are not overwhelmed by advanced bindings; the full list remains in Settings/Controls. The wording was checked against `ClientInput.cs` bindings. This is a source-level change; actual overlap/readability at 1280×720 and larger resolutions still requires Unity acceptance.

## Cursor ownership follow-up — 2026-11-16

The gameplay controller now locks and hides the cursor only while the world connection is active, gameplay input is not blocked, and the application has focus. Chat, menus, contextual POI windows, mini-games, loading/login and loss of application focus release the cursor. The controller reasserts the lock on subsequent focused gameplay updates because Unity may release it after focus changes. This is a source-level fix; verify mouse-look, menu cursor availability, Alt-Tab recovery and controller/gamepad behavior in Unity PlayMode.


## Contextual POI window navigation — 2026-11-16

- «← Назад» и «× Закрыть» принадлежат только контекстным окнам конкретных мест/активностей (корчма, рынок, конюшня и т.п.). Не добавлять их в постоянную верхнюю панель или основное TAB-меню.
- Каждое открытое контекстное окно обязано иметь видимые действия навигации/закрытия и поддерживать Escape.
- Открытие по E на соответствующем server-authored interaction target; пока окно открыто, блокируется управление персонажем и освобождается курсор. Закрытие любым предусмотренным способом немедленно возвращает управление и захват курсора.
- «Назад» возвращает из вложенного экрана к предыдущему экрану того же места; «Закрыть» закрывает всё окно места и возвращает в игровой мир. Если вложенного экрана нет, «Назад» не показывается.
- TAB остаётся отдельной панелью персонажа/журнала/карты и не получает кнопки закрытия контекстных окон.


## Request de-duplication — 2026-11-15

The shared RequestGate rejects an identical pending request (same visible action label and payload, ignoring generated reqid) before it can be sent twice. The tavern beer and card-game buttons additionally become visibly disabled while that exact action is pending. Different payloads under the same label remain available. This is a client UX safeguard only; server validation and outcomes remain authoritative. Pure C# CI passed in run #43754354166; Unity visual/runtime acceptance remains pending.


## Panel navigation follow-up — 2026-11-15

The character/journal panel has an explicit top-right close affordance in addition to the existing Tab toggle. Closing it releases character-menu input capture. This source-level change still needs Unity PlayMode verification for cursor and movement behavior.


## Progressive disclosure — first-play summary

The character summary prioritizes the current opening step and tavern/social context. Optional free practice, mini-games, mana and weave controls are grouped behind an explicit “Показать дополнительные занятия” disclosure. This is a presentation hierarchy only: all values and eligibility remain server-owned, and the journal keeps the full task explanation.


## UX sprint 2026-11-16 — owner direction and map navigation

- The owner has explicitly prioritized a focused UX sprint: character menu, map navigation, mouse/UI ownership and overall interaction clarity.
- The local settlement map now has source-level drag panning and a recenter-to-player control in the UX branch. The pan offset is view-only; it cannot alter server position or reveal undiscovered geography.
- Next work should prioritize a dedicated map-focused layout rather than keeping the only map inside the narrow character-panel scroll view.
- The F key is currently bound to `QuickAttack`. Do not repurpose it casually: inspect all combat consumers and propose a complete control layout before changing it. E remains the contextual world-interaction key.
- Server remains the sole authority for spawn location, map geometry, road connections, known settlements, discoveries, action eligibility and outcomes.
- Reference principles: Kingdom Come: Deliverance (restrained historical inventory), Red Dead Redemption 2 (map/navigation clarity), The Witcher 3 (information architecture), Valheim (exploration-first map). Do not copy their assets or UI literally.
- Detailed implementation/review checklist: `3-dev/comms/0263-chatgpt-to-claude-ux-sprint.md`.
