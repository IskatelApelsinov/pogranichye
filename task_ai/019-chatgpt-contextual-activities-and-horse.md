!В работе
# 019 — ChatGPT: unified contextual activities, crafting and horse UX

**Owner:** ChatGPT (Unity client)  
**Branch:** feat/client-contextual-activities-horse-20261117  
**Base:** main @ a6cbf092  
**Scope:** client only; no server or wire changes.

## Source findings

- The existing E key and short-range WorldInteractionProbe already work as an input path; the missing coverage was in target mapping and contextual UI.
- The Layout v2 POI allow-list omitted granary, so players could not use E at the server's horse stable POI.
- The client showed only garden gathering although the server already accepts gather, farmwork and apiary at the garden.
- Smithy supports act:forge and multiple craft recipes. The previous direct forge action hid the recipe choices.
- Hearth and granary have recipes in the server catalogue/me.recipes view but lacked contextual E panels.
- Horse state and all six horse operations already exist server-side. The client showed buy/mount/dismount/feed but not stow/unstow.
- The server's current Layout.JSON does not emit layout.interactions[] and server.go has no op:"interact" handler. Client must not send this unsupported operation.

## Implemented in this branch

1. Added granary and hearth to the POI policy; context-only POIs can now receive E through TargetId.
2. Added contextual activity panels:
   - smithy: act:forge and recipes for this workstation;
   - garden: gather, farmwork, apiary;
   - woods: zone-correct chop/brushwood plus hunt/fight;
   - hearth: recipes for this workstation;
   - granary: stable/horse controls plus recipes for granary.
3. Exposed the existing build contribution action in the general work menu, gated by current server snapshot (2 wood and 10 energy).
4. Recipe options use me.recipes[].here/can from the current validated full state. craft is sent as op:"craft", item=<recipe-id>, reqid.
5. Added horse saddlebag stow/unstow buttons with good and qty, alongside existing buy/mount/dismount/feed.
6. Removed the client path that sent unsupported op:"interact". If future descriptors arrive, the builder translates only known target_id values to existing v1 intents; unknown IDs fail closed.
7. Added/updated policy regression tests and reconciled interaction/DTO coverage docs.

## Authority rules

- All world changes remain server-owned.
- Client availability flags are affordance only; server revalidates distance, season, energy, resources, inventory, stock and ownership.
- No local changes to money, goods, stamina, horse ownership, world stock or progression.
- Existing reqid/RequestGate/me.result path is retained.

## Remaining runtime acceptance

- Unity Editor compile in Unity 6.7.
- Walk to smithy/garden/hearth/granary in starter and generated settlements; aim and press E.
- Confirm forge/gather/farmwork/apiary/craft results and server refusal reasons.
- Buy horse, mount, move, dismount; verify mounted state and server movement multiplier.
- Stow and unstow one item; verify inventory/saddlebag changes only after server frame.
- Confirm UI input capture prevents WASD/mouse-look leakage while contextual panel is open.

CI source checks do not constitute Unity Editor/PlayMode acceptance.


Additional action audit: `act:build`, `act:hunt` and `act:fight` were accepted by the server but had no obvious thick-client affordance. Build is now in the general work menu; hunt/fight are exposed from the woodland E panel. Server still resolves fauna, energy, equipment modifiers and outcome.


## Follow-up clarification — character goals vs immediate actions

- `op:"goal"` is a long-lived character priority configured from the character UI (work/eat/rest/socialize), intended to guide the character while the player is offline. It is not a one-shot action and must not be triggered by E at a POI.
- E at tavern/market opens the corresponding panel only; E at workstations/woods/river/mine opens or sends the immediate server-supported action.
- The current server handler stores the goal with a long `GoalUntil` and has no correlated acknowledgement. Client status must say “request sent”, not “saved/confirmed”. Confirm with Claude whether the exact offline semantics are fully implemented and whether the goal also affects the embodied character while connected.


## Party controls follow-up

- Added a thick-client party section to the character panel using `me.party` and server-published `place.neighbors[].player/known/name/desc`.
- Exposes invite nearby player, accept/decline invitation, and leave party through `op:"party"` with the shared `reqid`/`me.result` request gate.
- Member names and nearby-player visibility remain server-authored; no client-side membership mutation. `me.party` remains a typed-DTO gap but is now functionally consumed from the validated raw frame.
- Runtime acceptance: test with two actual player sessions in the same settlement, invitation/acceptance, member display, leave, and road travel with party.
