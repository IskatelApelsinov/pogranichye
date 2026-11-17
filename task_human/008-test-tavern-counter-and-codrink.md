!В работе
# 008 — Unity acceptance: tavern counter, beer and co-drink

**Owner:** Kate  
**When:** after PR #641/#642 and Claude task #013 are integrated, then Unity is updated.

## Test 1 — Reach the counter

1. Start a fresh demo or open-world character using the intended server entry mode.
2. Walk to the tavern entrance; verify the server-projected door opening is passable.
3. Walk through the building and stand at the visible bar/counter, not merely at the outside POI. Confirm the generated layout's visual bar lies within the server's 2.2m counter radius.
4. Confirm the contextual panel says the counter is reached when inside the server-reported radius.
5. If the keeper is absent, confirm the UI says so instead of pretending purchase is available.

## Test 2 — Solo beer

1. With keeper present, stock > 0 and enough coins, click order once.
2. Wait for matching server result/frame; confirm `me.mug=true`, coins decreased by exactly the server price and tavern beer decreased by one.
3. Click drink once; confirm `me.mug=false` and server-confirmed morale/drunk/hunger effects.
4. Repeat with no coins, no stock and keeper absent; no local state should change and the server reason should be visible.

## Test 3 — Two real Unity clients

1. Launch two separate clients/accounts into the same settlement.
2. Both physically enter the tavern and approach each other.
3. Invite from one client; accept from the other.
4. Confirm both receive the same co-drink social event, both pay once, acquaintance is updated, and both see synchronized presentation.
5. Repeat with decline, disconnect/reconnect and stale/repeated requests; no duplicate debit or ghost invitation.

## Evidence

Attach:
- Unity Console log;
- F3 diagnostic dump from both clients;
- server log lines for matching reqids/results;
- short note with server day/hour and settlement ID.

Do not mark complete on the basis of CI alone. Unity Editor 6000.6.7f1 and actual two-client runtime are required.
