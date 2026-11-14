# Branch: chatgpt/client-ux-interact-text

Grok (consultant) — client UX pass aligned with task 014 / comms 0246.

## Changes

1. **UiTextTokens** — shared HUD/prompt/toast font sizes and colors.
2. **ServerResultToast** — full-width wrapped toast layout (no 300×28 clip).
3. **CoreGameplay** — theme uses tokens; reject toasts show full reason up to ~4s.
4. **WorldInteractionProbe** — [E] prompt uses tokens, wrap, wider box.

## Not in this branch

- Full context-action remap (G/J → only E for tools) — next ChatGPT step.
- TMP migration, art pack binding, tavern counter pathing.

## Verify

- EditMode: `ServerResultToastCheck`
- Play: trigger a failed buy/talk and confirm the full Russian reason is visible.
