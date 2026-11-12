# Optional Asset Store art — disabled for the demo (Unity 6 / URP)

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

## Current decision

The imported Slavic village building prefabs are **not used by the demo runtime**. Their pivots, scale and visual composition did not match the authoritative village footprints and made the settlement look inconsistent. `WorldLayoutPresentationBuilder` now uses its native procedural facades only.

Do not import or run the prefab installer for the current demo. Existing downloaded source packages may remain in the local Asset Store cache; they are not required by a clean clone and are not referenced by the runtime building path.

The installer and source package are retained temporarily for possible later, individually reviewed prop/animation extraction. No imported building prefab should be re-enabled without a screenshot review and explicit fit test against Layout v2.

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
