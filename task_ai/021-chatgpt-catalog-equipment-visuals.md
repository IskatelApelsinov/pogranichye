# Task 021 — complete server-catalog equipment presentation

Status: implementation PR opened; Unity runtime validation remains required.

## Problem found during review of PR #670

The v1.22 server catalog exposes more equipment than the current Unity presentation renders. In particular, several hand items (knife, spear, mace, staff, pickaxe, crossbow) were accepted by the inventory UI but were not in `EntityViewRegistry.IsSupportedHandItem`. Worn slots `shirt`, `hands`, and `back`, plus catalog items such as helmet, furcoat and mail, were also absent from the character appearance path. This meant server-confirmed equipment could be equipped but visually disappear.

## Scope implemented

- Extend the inventory equipment panel with the server-backed shirt, gloves and back slots.
- Render helmet, shirt, fur coat, mail, gloves, backpack and satchel from the existing server `me.inventory.worn` / `neighbors[].gear.worn` fields.
- Render all catalog hand tools/weapons currently defined in v1.22: knife, spear, mace, staff, pickaxe and crossbow, alongside existing items.
- Keep all visuals presentation-only. No local equipment state, server schema changes, movement/collision changes or new inventory slots.
- Apply the same supported gear set to the local character and remote AOI characters.

## Acceptance checks

1. Equip/unequip each new slot and verify appearance follows the next server frame only.
2. Confirm remote players/NPCs show only public `neighbors[].gear` fields.
3. Check first-person camera arms, third-person body and tankard gesture do not duplicate held items.
4. Check new held items do not alter combat/action input or server resolution.
5. Unity 6000.6.7f1 / URP Editor compile and PlayMode checks remain mandatory; independent C# CI is not a substitute.
