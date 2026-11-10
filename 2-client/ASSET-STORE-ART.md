# Asset Store art integration (Unity 6 / URP)

## Source packages received

The source archive supplied for the demo contains these Unity packages:

| Package | Assets | Use |
|---|---:|---|
| Slavic Medieval Village Free Modular Environment Kit | 590 | Primary village architecture, props, paths, vegetation and environment |
| Low Poly Modular Armors Free | 267 | Wearable armor parts and props |
| Human Basic Motions FREE | 191 | Humanoid idle, walk, run and turn animation clips |
| Food FREE – Low Poly 3D Models Pack | 183 | Food props |
| Animancer Lite v8 | 567 | Deferred; review package layout/dependencies before adding to project |
| IK Helper Tool | 60 | Deferred; examples include unrelated weapon assets |
| Vegetation Spawner FREE | 49 | Optional editor utility; must not author gameplay/navigation geometry |
| Fullscreen Editor Play Mode FREE | 17 | Optional editor convenience |
| FREE Casual Game SFX Pack | 52 | Candidate sound effects; curate for medieval ambience |

The original .unitypackage files are not committed to this repository. Keep third-party package binaries in the local Unity project / Asset Store download cache and observe the publishers' current licenses.

## Import and install

1. Import **Slavic Medieval Village Free Modular Environment Kit** into 2-client/game through Unity Package Manager / Asset Store's My Assets.
2. Wait for Unity to finish importing and compiling.
3. Run **Pogranichye → Art → Install Slavic Village Prefabs**.
4. The installer copies selected prefab variants into Assets/_Project/Resources/PogranichyeArt/ while leaving the publisher's source assets untouched.
5. Enter Play Mode. WorldLayoutPresentationBuilder will use the imported village house prefabs for server-owned building footprints. If the package or generated Resources prefabs are absent, the current procedural facade remains as fallback.
6. Import the remaining packages individually. Do not import their sample scenes as the game's startup scene.

The installer currently prepares VillageHouse01..03, Tree01..03, Bush01, Grass01, Bench01, Barrel01, Mug01 and Cup01. Trees, foliage and props are staged for subsequent integration; the first runtime pass replaces building facades only.

## Contract and scene safety

- WorldLayout v2, server footprint coordinates, collision volumes, POIs, exits, and server-owned world state remain authoritative and unchanged.
- Asset prefabs are presentation only. Never use their colliders as a replacement for server validation or authoritative navigation.
- Keep the existing camera, ClientInput, player movement, NPC A-Life, and server action flow. Do not import replacement controllers, camera systems, MMO templates, or NPC AI frameworks.
- The first integration replaces only the visible building facade where a matching imported prefab exists. It does not silently rewrite village layout or move server anchors.
- The source asset package is optional: clean clones must continue to work using procedural fallbacks.

## Next visual integration passes

1. Replace procedural trees, shrubs, benches, barrels and tavern crockery with the installed Resources prefabs.
2. Use the Human Basic Motions clips after checking their rig/avatar compatibility; map them to server act_code rather than client-side goal text.
3. Use the modular armor pieces as visual attachments driven by me.worn; do not invent inventory slots.
4. Add curated ambience/footstep/interaction sounds from the SFX pack.
5. Add atmospheric depth, terrain material blending and LOD after the actual imported materials have been inspected in Unity.
