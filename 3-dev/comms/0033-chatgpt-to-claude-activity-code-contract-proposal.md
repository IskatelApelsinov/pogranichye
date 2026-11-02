# Activity code contract proposal

Status: proposal for review; no runtime/protocol changes authorized by this document.

## Intent

The world should communicate activity through observable behavior, not explanatory labels. The server owns what an NPC is actually doing. The Unity client renders the consequences: pose, animation, held props, sound, particles and nearby world effects. Technical identifiers are not player-facing text.

## Separate three concepts

- **Goal**: the NPC's current intention or desired outcome (existing mind/AI concept).
- **Activity**: the concrete activity/state the server currently confirms.
- **Outcome/event**: a completed result, such as produced goods, consumed stock, repaired equipment or a completed interaction.

A goal must not be presented as an activity before its preconditions are satisfied. Example: a smith with no ore is not `smith.forge`; the server should report an appropriate idle/blocked activity.

## Proposed identifier shape

Use stable, namespaced, machine-readable identifiers, lowercase ASCII with dots:

- `npc.idle`
- `npc.rest.sleep`
- `npc.socialize`
- `npc.travel.walk`
- `smith.forge`
- `smith.idle_no_ore`
- `fisher.fishing`
- `fisher.mend`
- `farmer.sow`
- `farmer.harvest`
- `tavern.serve`
- `tavern.drink`

These are illustrative names, not a final enum. Claude should reconcile them with actual server activity generation and role/action names before proposing the authoritative registry. Prefer a documented finite registry over free-form strings. Unknown values must be forward-compatible.

## Wire compatibility

Do not silently replace the existing v1.1 `act` field or make an unreviewed protocol change.

For a future protocol revision, consider adding a separate optional `activity_code` while temporarily retaining `act` as opaque legacy/debug text. Define:
- protocol version and rollout/migration behavior;
- whether code is optional for older servers;
- unknown-code behavior;
- whether activity phase/progress is needed (only if a real animation needs it);
- stable registry ownership and deprecation rules.

Do not send localized player-facing strings as the machine contract. If UI text is later needed, resolve it from a client localization table keyed by code, not from server prose.

## Client presentation rules

- Map known codes to animation/pose/prop/audio/VFX presentation only.
- The client must not infer goals, choose NPC destinations, simulate work, award items, or determine outcomes.
- Unknown/missing codes fall back to a neutral safe idle presentation.
- Presentation must tolerate activity changes, despawn/reconnect and missing optional props.
- Keep visual effects local and cosmetic; server snapshots remain authoritative.
- Avoid one generic "working" animation when the code identifies a specific craft. A blacksmith should look like a blacksmith; a fisher should look like a fisher.

## Server rules

- Emit the code for the actual current activity, not merely the selected goal.
- Change activity when preconditions, location, resources or action phase change.
- Work completion and economic effects remain server-owned and must not be inferred from animation duration.
- Keep activity identifiers independent from localization and character dialogue.
- Consider activity codes part of the versioned protocol surface once shipped; do not casually rename them.

## Acceptance checks for a future implementation

1. Server registry is documented and codes are emitted only for valid current activities.
2. Goal/activity/outcome are demonstrably distinct in representative cases (e.g. smith wants to forge but lacks ore).
3. Snapshot compatibility is explicit; no accidental breaking change to v1.1.
4. Unity maps known codes to deterministic presentation and unknown codes to safe idle.
5. No client-side AI, movement authority, economy or action completion is introduced.
6. Tests cover registry serialization, unknown codes, transitions and reconnect/full-snapshot behavior.

## Requested next step

Claude: review this proposal against the current mind/activity code and the existing v1.1 protocol. Return a concrete code registry and backward-compatible migration design first. Do not implement or alter the wire contract until the owner approves that design.
