!В работе
# 015 — Server-driven tavern counter interaction UI

**Owner:** ChatGPT  
**Subsystem:** Unity thick client, protocol DTO, tavern contextual UI  
**Depends on:** existing server implementation from PR #630 and comms 0224; Grok analysis 0240–0244 (counter, shift schedule, AOI and keeper target).

## Goal

Close the client-side gap between the already server-authoritative tavern system and the actual player's path through the tavern: approach the server counter, understand whether the keeper is serving, buy a mug, consume it, and invite a nearby real player to co-drink.

## Implemented in current branch / stacked PR #642

1. Add typed `TavernCounterFrame` and strict parser for existing `place.tavern_counter`.
2. Keep missing/null additive v1 compatibility.
3. Validate in `WorldFrameParser`.
4. Gate both active contextual beer UI and legacy CoreGameplay beer panel on the server counter position and keeper on-shift state (not a claim of physical presence).
5. Display explicit player-facing reasons for missing counter data, not at counter, keeper absent, no stock and insufficient coins.
6. Show co-drink invitation affordance only within the server's broad `atTavern` zone (exterior POI reach 2.5m or inside the server-projected tavern building) and for `neighbors[].player == true`.
9. Align generated visual serving bar with the server's geometric counter centre; starter bar already falls within server radius.
10. Use accurate “keeper on shift” wording until Claude aligns NPC target and physical presence.
7. Keep every action as a RequestGate intent; do not locally mutate mug, coins, stock, morale, drunk or social state.
8. Add a pure C# parser regression test and CI step.
11. Extend the opt-in F3 dump with a compact tavern snapshot (`tavern`, beer stock/price, mug, counter coordinates/radius/on-shift state) and neighbor summaries (`id`, player flag, role, act_code, local position), without names/chat/voice/credentials.

## Authority constraints

- Server owns counter geometry, keeper schedule/presence, purchase eligibility, stock, price, mug, drink effects, co-drink session, proximity and result.
- Client-side radius check is presentation only. Server always revalidates.
- Co-drink uses broader server `atTavern` semantics; it is not the beer-purchase counter check.
- Do not create or seed beer/NPCs locally.
- Do not weaken the server counter rule.

## Server dependency found by Grok 0244

The keeper AI currently targets the exterior `POI("tavern")` door, while the server purchase point is the building centre. `bartender_present` is derived from `ActCode == "keep_tavern"`, not physical distance. Claude owns task #013 to align this; do not label the current hint as proof that an NPC stands behind the bar.

## F3 diagnostic scope

The F3 dump now includes a bounded compact summary for the current tavern and AOI neighbors. Raw frame capture remains bounded by the existing diagnostic collector. The summary intentionally omits neighbor names, chat, voice and credentials.

## Remaining verification

- Resolve canonical protocol §3.2 mismatch with Claude (current prose still mentions exterior POI ≤2.5m, later server code uses counter radius + keeper).
- Confirm generated and starter tavern layout door path physically reaches counter in Unity.
- Test buy → mug confirmed → consume → mug cleared and stats/feed updated.
- Test two real Unity clients in same settlement: invite → accept/decline, both see social event, each charged once.
- Capture F3 diagnostic dump and server logs for a failed or successful transaction.


## CI result — 2026-11-14

PR #642 Unity-independent CI passed all 24 workflow steps in runs #43641435028, #43641448658 and #43641455630, including the dedicated `TavernCounterFrameParserCheck`. This is parser/pure C# validation only; Unity compile and runtime acceptance remain pending. Generated bar alignment and “keeper on shift” wording were source-reviewed, not visually tested in Unity.


## Latest CI — 2026-11-14

All 24 workflow steps passed in run #43641807521, including the dedicated tavern counter parser regression runner. This run followed the F3 diagnostic context addition. The CI does not compile the full Unity project or verify runtime movement, keeper arrival or co-drink.


## Generated Layout v2 E/Interact follow-up

The same PR now also adds `WorldLayoutPoiInteractionPolicy`, `WorldLayoutPoiTargetBuilder` and comms 0248: generated settlements receive short-range presentation-only E targets at server-provided POIs, mapped only to seven existing v1 goals/actions. The pure C# allow-list runner is wired into CI; latest result is pending. Unity runtime and POI occlusion remain unverified.

## Readability follow-up in this PR

- Server result/status labels now word-wrap instead of truncating long refusal reasons.
- The interaction prompt wraps; dialogue line/options use shared HUD body sizing.
- Shared ClientUiTypography tokens cover HUD title/body/secondary/debug and world label font resolution.
- This is an incremental slice, not a complete audit of every IMGUI panel or generated POI mapping; see task 014 for the remaining work.


## Latest CI — 2026-11-14

All 24 workflow steps passed in run #43642750564, including tavern parser regression checks and the existing client pure-C# regressions. This does not compile Unity MonoBehaviours or validate PlayMode.
