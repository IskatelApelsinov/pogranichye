# Task 016 — ChatGPT: first-play UI/UX pass

**Owner:** ChatGPT (Unity client + UI/UX)  
**Priority:** P0  
**Branch:** chatgpt/tavern-counter-ui-20261114  
**Scope:** client presentation only unless a server contract gap is found. Do not invent server actions.

## Goal

Make the first 10 minutes legible and comfortable for a new player. The current client contains many systems, but the experience still reads as accumulated IMGUI panels and keyboard shortcuts. Prioritize clarity, hierarchy, feedback and discoverability over adding more features.

## Source already changed

- Action-result toast now wraps server reasons and sizes to content instead of fixed 300×28 px.
- E/Interact prompt now wraps and calculates height based on its text instead of fixed 46 px with clipping.
- See 3-dev/docs/CLIENT-UI-UX-STANDARD.md for governing principles and acceptance.

## Completed in this iteration (2026-11-15)

- Exposed read-only shared `RequestGate` state from `CoreGameplay` (`HasPendingRequests`, in-flight label and total count). No new request path or local success prediction.
- Added a compact top-right pending banner: shows the active server-bound action and queued count; it clears naturally when the matching result resolves, times out or the gate is cleared on disconnect.
- Added a persistent first-play objective card while the character journal is collapsed. It uses the existing server-backed task/acquaintance fields and gives one concise next step; the full explanation remains in the journal.
- Positioned pending feedback below the nearby-player invite panel when that panel is visible to avoid overlap.
- Source changes are committed to this branch. Unity Editor/PlayMode visual checks and the latest PR CI remain outstanding.

## Completed — first-play hint simplification (2026-11-15)

- Simplified the optional H-toggle hint strip to teach only immediate movement, looking, interaction and basic navigation.
- Removed combat/weave key overload from the first-play strip; those controls remain in the detailed Settings/Controls help where players intentionally look for advanced bindings.
- Corrected the strip to match actual Input System bindings: WASD, mouse look, E, Shift sprint, Alt jog, Space jump, Tab journal and H hide.
- This is a source-level clarity pass. The strip is still opt-in (H) and needs Unity visual acceptance for wrapping/placement.

## Next implementation steps

1. Audit pending-state placement for every visible action and add local button-level disabling only where a control can currently enqueue duplicates. The shared banner now exposes the single in-flight action and queue count; it clears on matching reqid result, timeout or disconnect. Never show optimistic success.
2. Consolidate tutorial hints into one contextual strip. Remove duplicated/conflicting keybind instructions and expose advanced combat/weave controls only when relevant or when the player opens help.
3. Rework the first-play objective as a small persistent journal card: one current step, progress from authoritative fields, one next action; no duplicated paragraph blocks.
4. Audit panel navigation (Tab/Esc, selected tab, close affordance, cursor lock, scroll state). Ensure opening a panel never accidentally moves/attacks the character.
5. Normalize IMGUI styles and Cyrillic font fallback incrementally using ClientUiTypography. Do not globally change every font size without checking layouts.
6. Make market and tavern actions show clear price, stock, range/shift requirement and pending state. The server remains the final authority.
7. Add a resolution test matrix at 1280×720, 1920×1080 and 2560×1440; include long Russian refusal reasons and narrow UI widths.

## Acceptance

- A first-time player can identify movement, interaction and the current objective without reading a large help panel.
- Every action visibly communicates ready/pending/accepted/refused/timed-out.
- Long server reasons are fully readable; no fixed-height clipping.
- No UI overlaps reticle, quest tracker, status bars or subtitles at target resolutions.
- No success, purchase, item or social effect is shown before server confirmation.
- Anonymous NPCs remain anonymous until server acquaintance state permits a name.
- Pure C# checks pass; Unity Editor/PlayMode acceptance is recorded separately and never implied by CI.

## Handoff

If an implementation requires new server-owned state or an intent, stop and send Claude an exact contract request in 3-dev/comms. Do not implement local simulation as a workaround.


## Verification update

- GitHub Actions `pure-csharp` run #43735210413 passed all 23 steps on source commit `dc44db79cd65f78260c51256b8f9e9dda80a99ef` (2026-11-15). This validates the repository's independent C# regression suites; it does not compile the full Unity scene or exercise IMGUI layout.
- PR #642 remains open and stacked on PR #641. Unity Editor compile, PlayMode, and manual visual checks at 1280×720 / 1920×1080 / 2560×1440 remain required.


## Duplicate-submit guard — 2026-11-15

- Extended the shared `RequestGate` to reject a second pending request when both the player-facing action label and the full payload (excluding `reqid`) match an action already in flight or queued.
- Payload comparison handles nested dictionaries and lists, so repeated card selections / weave payloads do not bypass the guard merely because their arrays were allocated separately.
- Different payloads under the same label remain distinct (for example, choosing a different item); the guard does not block legitimate alternate actions.
- Added pure C# regression cases for duplicate in-flight requests, duplicate queued requests and same-label/different-payload requests.
- This is a safety net at the shared request boundary, not a visual disabled-button treatment. The remaining UI pass should add visible per-control pending states where they materially help.
- Validation is pending a PR workflow run. No Unity Editor, PlayMode or live-server test is claimed.
