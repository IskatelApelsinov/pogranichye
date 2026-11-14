# Branch: chatgpt/client-ux-interact-text

Grok — client UX pass (task 014 / comms 0246).

## On this branch

| File | Role |
|------|------|
| `UiTextTokens.cs` | Shared HUD/prompt/toast font sizes |
| `ServerResultToast.cs` | Full-width wrapped toast layout |
| `ServerResultToastCheck.cs` | EditMode tests |
| `WorldInteractionProbe.cs` | Readable [E] prompt |
| `patches/core-gameplay-toast-ux.patch` | CoreGameplay theme + toast |

## Apply CoreGameplay patch

```bash
cd repo-root
git apply 2-client/patches/core-gameplay-toast-ux.patch
```

Or merge this PR after ChatGPT applies the patch in Unity project (27-line change).

## Verify

1. EditMode: ServerResultToastCheck
2. Play: force a failed action — full Russian `reason` visible, not clipped in 300×28 box
3. Aim at NPC/POI — [E] prompt uses shared tokens

## Follow-ups (not this branch)

- Map G/J tool hotkeys into primary E where possible
- TMP for world labels
- Bind textures/grok materials
