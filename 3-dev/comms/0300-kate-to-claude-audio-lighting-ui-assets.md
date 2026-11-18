# 0300 — Kate → Claude: audit and upgrade of sound, lighting, UI and asset pipeline

Date: 2026-11-18.
Related work: localization/text cleanup PR #746; world content atlas PR #745.

Kate asks for a broad review and practical upgrade of:
- user-facing text and UI (remove duplicate, redundant, debug/technical labels; keep the compact server-response feed at top-right; make normal play immersive);
- soundscape and music;
- daylight/interior visibility and warm night lighting, especially the tavern;
- consistent model dimensions, topology/LOD and texture standards for buildings, people, tools, trees and bushes.

## Existing implementation discovered by ChatGPT

Do not rebuild these systems blindly:
- `VillageSoundscape.cs` already creates procedural placeholder loops for wind, birds, water, forge, hearth, tavern murmur and night insects. It has server-anchored spatial emitters and a master ambience volume.
- `WorldAudioDirector.cs` already creates procedural footsteps, run, magic, beer and nearby NPC work sounds. It has a bounded pool of six spatial voices.
- `WorldSpacePresentationBuilder.cs` creates room reverb zones from server-authored room bounds.
- `WorldAtmosphereCycle` already follows server time and exposes `NightAmount` and `WindStrength`.
- `VillageAmbientMotion` already pulses registered lights; `WorldLayoutPresentationBuilder.cs` creates a generated tavern hearth point light and facade window details.
- `WorldLayoutPresentationBuilder.BuildFootprintBuilding` currently creates facade windows; inspect whether those are real openings or only dark recesses/shutters. Existing comments say "window", but this does not prove daylight reaches interiors.
- `VerticalSliceController` wires the soundscape and server POI anchors, and calls footsteps while moving. Inspect whether footsteps are actually grounded by movement and whether surface type can be read without inventing data.
- Existing lighting documentation: `3-dev/docs/ОСВЕЩЕНИЕ-UNITY6-URP.md`. Read it before changing ambient/URP.

## Required audit

1. Review every visible HUD/menu/modal/tooltip/status text in the two largest UI controllers and other visible UI scripts. List redundant text, debug leakage, duplicate instructions, inconsistent verbs/capitalization, labels that cover world view, unclear back/close behaviour and controller/input conflicts.
2. Preserve the top-right compact response feed. It must show concise localized outcome only; technical reason codes and diagnostics stay in a separate opt-in developer panel/log. Do not turn ordinary messages into fake NPC dialogue.
3. Audio audit: document every existing source, clip, trigger, volume slider, distance curve, pool, POI anchor and acoustic zone. Identify unimplemented categories and duplicated sounds.
4. Replace placeholder procedural noise/chirps/sine voice beds with actual licensed recordings. Do not silently use assets from unknown licenses. Create per-file license manifest and keep source URLs/authors/IDs.
5. Implement layered region-aware audio: regional music, biome ambience, time-of-day layers, weather layers, settlement crowd, indoor/tavern crowd murmur, fire, water, wind/leaves, footsteps by material, attack/weapon/impact, door, drinking mug/sip. Smooth crossfades and bounded voices. Do not use a single global loop for all regions.
6. Add separate Music / Ambience / SFX / Voices volume controls, preserving Master. Existing settings have only three groups; make migration backward-compatible and retain saved volume values.
7. Do not play attacks or drink events on input alone. Tie them to authoritative confirmed state/transition. Avoid duplicate local+nearby event playback for the player.
8. Lighting audit: verify windows are actual openings and not merely dark decals/recesses; ensure daylight can reach interiors, interior exposure remains readable, night lights are warm and locally bounded. Tavern gets priority. Keep lights/URP within target GPU budget. Do not globally flatten ambient light.
9. If server occupancy/activity is available, use it to determine which homes glow at night. Otherwise label any fallback as presentation-only; never claim a resident is home without data. Do not invent new server entities on the client.
10. Create or update a model/texture standard and apply it to a small vertical slice: tavern, ordinary home, one NPC, one hand tool, one tree, one bush. Use metres and LODs; exact recommended dimensions and tri/texture budgets are in `3-dev/content/СТАНДАРТ-3D-АССЕТОВ-ЁФ-v1.md`.
11. Profile CPU/GPU/memory/audio voices and light count on a representative settlement and 100-player synthetic load; no arbitrary increase in per-object lights or audio emitters.

## Audio source shortlist — verify each chosen file

- Kaino CC0 SFX library: https://kaino.dev/library/sfx — 1,397 items, with ambience, footsteps, impacts, combat, crafting and nature. Its library page states CC0 1.0 and commercial use; record the exact selected item page/file in the manifest.
- OpenGameArt CC0 sound library: https://opengameart.org/content/cc0-sounds-library — includes steps on wood/stone/leaves/gravel/mud, birds/wind, liquid bottle drink, battle and interaction sounds. Verify each individual asset's license/credit file.
- OpenGameArt CC0 SFX collection: https://opengameart.org/content/cc0-sound-effects — possible creature, rain, leaves, door, combat and ambience sources. Verify each file separately.
- BigSoundBank countryside: https://bigsoundbank.com/countryside-s0097.html — specific countryside bed described as wind/trees/insects/birds, explicitly CC0 on the item page.
- Pixabay sound effects: https://pixabay.com/sound-effects/ — commercial use under Pixabay Content License; review current terms and individual asset restrictions, retain contributor/source metadata. Do not treat it as CC0.
- Short medieval starter pack: https://fantasyforgequests.itch.io/fantasy-forge-tavern-dungeon-starter-pack — page claims all eight items CC0/public domain; inspect and retain its included license/credits before use.
- For regional music, prefer original commissioned/composed tracks or explicitly CC0 music. Do not assume SFX library license covers music. No copyrighted soundtrack or imitation of a living artist.

## Deliverables

- A concise audit report with severity, exact file/line references, and proposed fixes.
- One additive Unity PR for sound/lighting/UI fixes, split further if scope is too large.
- A per-file audio license manifest.
- A sound event matrix: event → trigger → source → spatial mode → mix bus → authority condition.
- A regional music/ambience map based on existing biome/region DTOs; no fabricated biome names.
- A model/texture asset checklist and first six representative prefabs.
- Automated checks for stable event routing, asset/license manifest consistency and model budgets where possible.
- State explicitly what was tested in CI and what still requires Unity Editor/PlayMode/GPU/audio QA.

Do not block Demo 1/2. Do not merge without Kate's explicit approval. Keep server-owned geometry, entities, occupancy and events authoritative.
