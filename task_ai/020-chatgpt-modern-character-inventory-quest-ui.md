# 020 — Modern character inventory, equipment and quest journal

**Owner:** ChatGPT — Unity thick client / UI  
**Date:** 2026-11-16  
**Status:** Client presentation pass prepared; Unity visual acceptance pending.

## Goals

Replace the legacy text-heavy inventory and quest journal with a readable, game-facing interface while preserving the server-authoritative model.

## Implemented in this branch

- Inventory header with authoritative coin balance and carry-load summary.
- Equipment slots separated by meaning: hand, head, clothing/body, armor, feet, offhand and belt.
- Inventory categories: all, weapons, clothing, consumables and materials.
- Text search across localized item names and stable item IDs.
- Item cards with stack count, equipment slot and the already-supported server intent (equip, wear, unwear, use).
- Storage section collapsed by default; stash and horse summary remain distinct from carried inventory.
- Quest journal now presents the opening milestones as checkable steps, shows server-confirmed progress bars for the current errand, giver/location/reward, and separates persistent chronicle from this-session observations.
- No optimistic inventory or quest mutation. UI reads frame data and sends existing intents only.

## Existing equipment appearance pipeline

EntityViewRegistry.Apply already drives the controlled character's visible clothing and held/carried props from frame.Me.Inventory, and nearby characters from the public neighbors[].gear subset. This work exposes the exact matching slots more clearly in the UI; it does not invent new equipment states.

## Explicit non-goals / server gaps

- Do not invent durability, rarity, stack splitting, item instances, drag/drop transfers, item icons, equipment slots or stats that are absent from the server contract/catalogue.
- Do not create new quest objectives on the client.
- No server changes in this task.
- Item drag/drop, unique item instances, detailed tooltip stats, 3D paper-doll preview and visual item icons require checking the authoritative catalogue and content assets first.

## Acceptance checklist in Unity

1. Inventory categories and search filter only visible presentation; no server mutation.
2. Equip/wear/use actions submit the existing server request and UI changes only after a confirmed frame.
3. The clothing, armor and belt slots match the actual me.inv.worn keys; held item matches me.inv.hand.
4. Character model changes only from authoritative inventory frames; verify first-person arms and third-person body.
5. Quest milestone/progress bars match server flags and issued.have/quantity; ready/done states remain server-confirmed.
6. Test narrow and wide aspect ratios, long translated names, empty inventory, missing optional catalogue and older v1 frames.
7. Verify mouse-look, combat and world interaction remain unchanged while panels are open.

Unity Editor, PlayMode and visual acceptance were not run during source preparation.