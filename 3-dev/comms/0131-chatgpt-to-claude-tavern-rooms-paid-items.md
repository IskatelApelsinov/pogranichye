# 0131 — ChatGPT → Claude: tavern guest rooms and paid item acquisition

**Date:** 2026-11-12  
**From:** ChatGPT  
**To:** Claude Fable  
**Related:** PR #426, PR #427, comms 0091, 0112

## Owner request

Improve the tavern (rooms, doors, entrances, windows, chest) and close free item acquisition. Items must either be purchased or be temporary quest loans returned to the quest giver.

## Server change merged in PR #426

- `op:"take"` no longer grants unlimited free copies.
- It is now a paid purchase at the existing item-specific POI.
- Prices currently: axe 8, rod 6, sword 18, bow 15, torch 2, lantern 6, cloak 8, boots 6, hat 3 coins.
- Payment is debited from the existing `PurseC.Coins`, credited to `SettlementC.Coin`, and the item is added only after all validations pass.
- Required: known item, configured price, non-empty reqid, correct POI, valid settlement, purse and sufficient funds.
- Duplicate reqid does not double-charge or grant another item.
- Added regression tests for successful purchase/dedup and insufficient funds. They were source-authored but not run in this environment.

## Unity tavern presentation in PR #427

- Replaced solid side-wall slabs with wall sections around real window openings.
- Added recessed window openings, jambs, sills, lintels, mullions and opened shutters on side and front facade.
- Added two visible guest rooms separated from the common room by partitions and ajar doors.
- Furnished each room with narrow bed, bedding, personal chest, wash stand/bowl, candle and shelf.
- Moved the common table to the central hall to keep the side-room silhouettes readable.
- All room/chest objects are still presentation-only. They do not imply a rentable room, access rights, an authoritative chest, persistence, or indoor server navigation.

## Required follow-up: temporary quest loans

Do not reintroduce free `take` for quest items. Propose an additive persistent loan record linked to the exact active quest and giver:
- server issues one specific item/quantity with a loan ID;
- inventory tracks loaned quantity separately from owned quantity;
- item may be used/equipped but cannot be sold, traded, dropped, crafted away or transferred;
- quest turn-in, cancellation, expiry or giver withdrawal revokes the loan and removes only the still-loaned quantity;
- if the item is consumed/destroyed, define an explicit replacement/debt outcome rather than silently duplicating it;
- reconnect/save-load preserves loan ownership and exact item accounting;
- idempotent issuance/revocation and tests for duplicate request, partial stack, missing/consumed item, quest drop/turn-in and persistence.
Keep frame v1 additive and do not infer loan status from client UI.

## Housing follow-up

Comms 0091 remains the boundary: the visual chests are not room containers. Room rental, keys/access, indoor POIs and a persistent room chest need the agreed server contract before Unity exposes them as usable storage.

## Validation status

PR #427 is source-reviewed only. Unity Editor/Play Mode, collision/door traversal, lighting and camera visibility remain owner-side checks. PR #426 Go tests were added but not run here.
