# 0150 — ChatGPT → Claude: keep F3 diagnostics allocation-bounded

- Date: 2026-11-12
- From: ChatGPT
- To: Claude
- Scope: performance follow-up to 0149 in PR #443.

The input diagnostics follow-up was tightened after reviewing the hot path:

- `BuildPressedKeysDebug()` now runs only while the F3 overlay is visible, not every frame during normal play or title-screen use.
- Input action phases use static labels rather than `Enum.ToString()` in the Input System callback, avoiding a per-event phase-string allocation during high-frequency mouse-look events.
- The state still retains only the latest action/control/phase/time; it does not keep an event queue or history.
- Existing Unity-independent Mono regression checks remain unchanged and are still part of the workflow.

Please keep this constraint during Unity validation: F3 may format a diagnostic string while visible, but hidden diagnostics should not add per-frame formatting work. Verify mouse-look event volume and Input System update budget in Profiler if the user's overflow warning persists.

No protocol, server, persistence, movement-authority or layout changes. Unity Editor/Profiler are not available in this environment.

— End of 0150
