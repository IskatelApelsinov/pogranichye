# 0298 — Kate → Claude: language foundation status and server text audit

Date: 2026-11-18. Related to 0297 and PR #745.

ChatGPT has started the first Unity localization slice in branch `feat/client-localization-foundation-v1`:
- seven locale identifiers: ru, en, zh-Hans, ar, fr, de, hi;
- deterministic lookup with Russian fallback and caller-authored fallback (never show localization keys);
- device-local locale preference;
- language selector in Unity settings;
- first localized settings and login strings; status/reason keys are catalogued but not yet wired to every server response;
- pure C# regression test and CI step.

This is intentionally a foundation, not a claim that the whole client is translated. Current UI still contains a large amount of hard-coded Russian text. The selector explicitly tells players that some older text remains untranslated. Arabic strings are present, but complete RTL layout, bidi and shaping are not yet accepted; don't mark Arabic as production-complete until tested with proper fonts and layout.

## Claude tasks

1. Audit all Go/server user-facing prose, result/reason text, NPC dialogue, names/descriptions, feed/chat and thin-client text. Separate machine-stable reason codes from localized presentation text.
2. Report all current source locations and propose stable localization keys, including placeholders and grammatical metadata. Never make the client parse prose to infer action success/failure.
3. Keep wire contract and existing persisted IDs stable. If adding locale selection to account/profile or server-generated localized messages, propose an additive ADR/fixture first. Initial language choice is device-local and should not alter server state.
4. Ensure server responses carry stable semantic codes and structured values. Prefer the client to localize them; do not duplicate locale-specific presentation strings in server logic unless a server-rendered channel genuinely requires it.
5. Audit name/person DTOs for sex/gender, culture, canonical name, display name, grammatical forms and relationship-aware address. Do not infer gender from a given name.
6. Provide a translation source export for all server-owned strings and a completeness check across the seven locales. Mark uncertain lore/terms for review instead of inventing equivalents.
7. Identify thin-client/web localization framework and its locale persistence; align locale codes and key naming with Unity.

## Acceptance and coordination

- Keep Russian as the editorial source of truth.
- Translation quality matters more than raw coverage. No literal machine-sounding phrases.
- Keep diagnostics distinct from diegetic NPC dialogue.
- The compact server response feed remains at the top right in Unity; ordinary gameplay UI should remain quiet and immersive.
- Return an audit and a small additive PR first. Don't bundle all translations or change the protocol without review.
- No claims of complete Arabic RTL, CJK/Devanagari font coverage, or Unity runtime verification until actually tested.
- Do not block Demo 1/2 or the resident home routine.

— Kate
