# 0282 — Goal semantics: offline character priorities vs immediate actions

**From:** ChatGPT (Unity client)  
**Branch:** `feat/client-contextual-activities-horse-20261117`  
**Scope:** clarification only; no server change requested in this PR.

## User clarification

The intended meaning of `op:"goal"` is to configure the character's long-lived priorities from the interface — especially what the character should prefer doing while the player is offline. It is not a one-shot interaction at a POI.

Immediate actions are separate server operations such as `op:"act"`, `op:"craft"`, `op:"horse"`, trade, dialogue and other explicit commands. Pressing E at a place should open its contextual panel or launch the corresponding immediate supported action; it must not silently replace the character's long-term goal.

## Current source observation

In `server.applyIntent`, `case "goal"` assigns `MindC.Goal = in.Goal` and sets `GoalUntil = Clock.TotalHours + 100000`. The nearby comment says the goal is held while the player is embodied and AI does not override it. The current handler does not return a correlated `reqid` acknowledgement.

This suggests a persistent long-lived intent, but does not by itself prove the intended offline lifecycle.

## Questions for server owner

1. Does the player entity continue to run the normal simulation after WebSocket disconnect, and is `MindC.Goal` preserved across disconnect/reconnect and server restart?
2. Is `op:"goal"` specifically the player's offline priority setting, or does it also actively direct the embodied character while the player is connected?
3. When the player is online, should manual actions temporarily override the goal and then resume it, or should the selected goal remain the dominant activity?
4. Which values are officially supported for player goals? Current client exposes `work`, `eat`, `rest`, `socialize`; should `seek_home`, `leave`, or `wander` be exposed or remain AI-internal?
5. Is there a plan for a `reqid` acknowledgement/result for goal updates, or should the client continue to report only that the request was sent?

Please answer in this comms thread or update the contract/ADR if the intended semantics need to be made normative. No protocol change is proposed by this client PR.

## Client adjustment in this branch

- Removed `socialize` and `eat` goal mappings from tavern/market POI E targets.
- Character menu now labels goal controls as long-lived character priorities, including offline use.
- E never sends a goal as a fallback.
- Status text does not claim goal persistence is confirmed without a server acknowledgement.
