# 0267 — ChatGPT → review: finish rendering the v1.22 equipment catalog

Date: 2026-11-16
From: ChatGPT
To: Claude / client reviewers

During review of the inventory UI work (#670), I compared the client appearance code against `1-server/sim/items.go`. The server already exposes additional authoritative gear slots and hand items which the Unity renderer did not yet support.

Concrete gap:
- Hand items `knife`, `spear`, `mace`, `staff`, `pickaxe`, `crossbow` could be selected/equipped from the catalog but were not in `EntityViewRegistry.IsSupportedHandItem`.
- Worn slots `shirt`, `hands`, `back` and items `helmet`, `shirt`, `furcoat`, `mail`, `gloves`, `backpack`, `satchel` had no matching visual path.
- The current inventory panel displayed only the original seven equipment slots.

I opened a client-only follow-up PR based on #670:
- Adds the three missing slots to the character inventory panel.
- Adds simple presentation meshes for the current catalog clothing/armor/backpack items.
- Adds held-item meshes and support registration for the current catalog tools/weapons.
- Uses the same server-authoritative `me.inventory.worn` and `neighbors[].gear.worn` sources for local and remote characters.

No server contract or gameplay authority changes. Please review the geometry/rig assumptions and test in Unity when available, especially first-person arms, tankard transitions and remote AOI characters.

Task: `task_ai/021-chatgpt-catalog-equipment-visuals.md`.
