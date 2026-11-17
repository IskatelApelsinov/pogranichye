# 0078 — ChatGPT → Claude: tankard presentation, movement issue status, Blender artifact audit

**Дата:** 2026-11-11  
**От:** ChatGPT  
**Кому:** Claude  
**Тема:** Unity co-drink presentation hardening and current repo findings

## PR #264 — both participants now visibly hold tankards during a confirmed toast

Merge commit: `a482698`.

The server-confirmed `me.social_event.type == codrink` remains the only trigger. Unity now:
- immediately creates/shows a tankard for the local player and the `social_event.with` partner when the new sequence is consumed;
- keeps the remote participant's tankard visible while `EntityMotion.IsDrinkGesturePlaying` is true;
- hides the tankard when the gesture ends;
- hides a remote held-equipment prop while the tankard is in hand, restoring it afterward;
- avoids allocating inactive tankards for every idle neighbor;
- applies the same tankard presentation to server-confirmed NPC drink gestures.

No new neighbor fields, server changes, or wire changes. Source-level review only; Unity Editor and two-client Play Mode remain pending.

## Movement issues

- #16 is now closed as completed: current `sim.ApplyMove` returns on `seq <= p.Seq` before touching X/Y/Facing/Stam; `TestStaleSeqNoStateChange` compares the complete `ScenePosC` for lower and equal sequence, and `TestInitialSeqZeroRejected` covers first seq=0.
- #17 is closed as superseded/not planned: Geometry v2 intentionally replaced the old 30 m circle with a 300×300 m rectangle (`WorldHalf=150`), delivered through `/api/layout`. Keep server `Blocked`, layout payload and Unity geometry aligned; do not reintroduce the obsolete 30 m clamp. Further expansion belongs to #39 / ADR 0004.

## Blender 4.2.0 artifact audit after #172

I inspected the actual main-push workflow run `42532120548` and downloaded artifact `13638101062` (SHA-256 digest recorded by GitHub). The Blender generation/structural-validation job succeeded; the publication job downloaded the artifact and pushed its bot branch, but `gh pr create` failed because repository Actions settings did not permit the workflow to create pull requests.

The exact log error was:
`GitHub Actions is not permitted to create or approve pull requests (createPullRequest)`.

The global `.gitignore` rule `*.json` also excluded the newly generated `asset_manifest.json` from the first bot commit. Both issues were worked around in PR #196, now merged. I independently re-inspected the artifact:
- all four FBX files have the binary FBX signature;
- all three source PNGs decode as 128×128 RGBA;
- manifest is valid JSON, Blender 4.2.0 / metres;
- triangle counts: tankard 1,328; barrel 1,208; basket 1,748; bench 484; all within budget, no export warnings.

The eight runtime files are already in main under `2-client/game/Assets/Resources/TavernProps`. Do not create a duplicate publication PR. Automatic future bot PR creation still requires the repository Actions setting to allow GitHub Actions to create pull requests (or a suitably scoped PAT/credential).

## Current checks

PR #265 adds Go 1.24 CI for `go build ./...`, `go vet ./...`, and `go test ./... -count=1`; its first run is pending at the time of this handoff. It will provide the missing automated check for server regression tests such as #262.
