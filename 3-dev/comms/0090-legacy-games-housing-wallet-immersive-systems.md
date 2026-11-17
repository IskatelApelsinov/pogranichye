# 0090 — Legacy gameplay audit: tavern cards, minigames, housing, wallet and immersive world presentation

**Date:** 2026-11-11  
**From:** ChatGPT  
**To:** Project design / Claude  
**Status:** design and implementation map only; no wire/runtime changes  
**Base:** current main after PR #298

## Purpose

The owner asked to recover useful gameplay from `3-dev/archive/legacy-prototype` and extend the current direction with a rented-room chest, a physical wallet/purse, more character/world details, richer Unity location templates, varied minigames and tavern card games. This note records what was actually found in legacy and how to reintroduce it without copying obsolete implementation.

## 1. What exists in legacy (verified source)

### Tavern games

Legacy `netserver.py` exposed all of these at the tavern:
- Dice, stake 5 coins.
- Ochko (21) against the innkeeper, stake 10.
- Podkidnoy Durak against the innkeeper, stake 10.
- PvP Ochko and PvP Durak at shared tables.

Legacy `mp.py` contains the game orchestration and coin settlement:
- Ochko: player gets two cards, can hit or stand; bust loses, tie returns stake, higher total wins; innkeeper draws to at least 17; natural 21 has a special payout.
- PvP Ochko: two seats, both contribute to a pot, each can hit/stand; opponent totals remain hidden until allowed; result settles the pot.
- Durak: separate `world/durak.py` rules engine; 36 cards (6..Ace), trump suit, six-card hands, lowest trump opens, attack/defend, throw-ins by matching rank, take or beat, draw back to six, final loser is the last player holding cards.
- PvP Durak: the same engine can run with `vs_bot=False`; each seat receives only its own hand and the opponent's card count/name. The bot is disabled.
- Legacy game code also used a fair-random stream with commitment/reveal fields. Preserve the principle of auditable server-side shuffle; do not expose hidden deck/hand state to clients.

**Important limits of the old implementation:** Durak is one-on-one in its engine, not a full 3–6 player table; transfer-Durak is not implemented in the shown rules. The old stakes are hard-coded demo values, not a suitable economy balance. The old game sessions are not a drop-in replacement for current reqid/result handling.

### Activity minigames

Legacy `netserver.py` selected a minigame from a pool per activity, rather than always using one interaction:

| Activity | Legacy variants |
|---|---|
| Firewood/chopping | mash, timing |
| Garden | sequence, catch |
| Berries / herbs | ripe, catch |
| Fishing | hold, balance |
| Hunting | targets, aim |
| Building/help | sequence, timing |
| Mining | timing, balance |

The eight minigame patterns were:
- `timing`: stop a moving marker in a target window;
- `sequence`: repeat a short sequence;
- `ripe`: select ripe items and avoid unripe ones;
- `mash`: rhythmic repeated input;
- `hold`: hold/release at the right moment;
- `catch`: catch useful falling objects and avoid junk;
- `aim` / `targets`: aim and hit moving/appearing targets;
- `balance`: keep a moving point in a stable zone.

Difficulty was described in four bands (easy / medium / hard / master) and selected per activity. This is a useful UX reference, not a reason to bring back the old global random `DIFF` variable or client-authoritative quality.

### Housing, items and money

- Legacy `world/player.py` has room rental with daily rent, and a separate house purchase/furnishing path.
- Legacy `world/items.py` has a catalogue, recipes, equipment/clothing slots, trinkets, consumables, buying, crafting and inventory ownership.
- Legacy character money is a scalar coin balance (`coins`). I did **not** find a dedicated physical wallet item/container with its own capacity or a persistent personal chest assigned to a rented room in the inspected legacy files.
- Therefore: rented-room chest and purse are new requirements, not features to claim as restored from legacy.

## 2. Product direction: rented room and personal storage

Add the following as separate persistent concepts, not a single stash flag:

1. **Rental room:** room ID, settlement/building/floor/door, tenant, rent period, price, paid-through time, access/keys, occupied status and eviction/renewal rules.
2. **Room chest:** a persistent container physically placed in that room. Contents survive reconnect/restart and remain after leaving the room; access requires a valid rental/key/permission. Define what happens on expired rent (grace period, retrieval/claim process, never silently delete contents).
3. **Purse/wallet:** an equipment/container item worn on belt or carried. It displays carried coins and has capacity/weight/visual representation. Do not silently equate this with the character's total wealth, bank balance, merchant credit or room chest. Decide whether coins physically occupy slots/weight; keep transaction atomic.
4. **Inventory destinations:** carried inventory, backpack, purse, room chest, horse saddlebag and settlement bulk storage are distinct containers with explicit transfer operations. No implicit item duplication or destructive migration.

Suggested first vertical slice: rent one room → unlock/open its chest → move one stack into/out of it → reconnect and verify persistence. In parallel, show the equipped purse and server-confirmed coin count in the inventory UI. Only then expand to locks, shared keys, room furnishing and multiple containers.

## 3. Inventory and character detail

Build on ADR 0005 and comms 0088; do not replace `InventoryC` or `StashC` destructively.

- Item definitions: stable ID, category, icon/presentation key, unit mass/volume, stack limit, allowed slots, durability/quality and world prefab key.
- Distinguish stackable goods from unique instances (weapon condition, crafted quality, ownership).
- Equipment: head, body, legs/feet, cloak, hands, main/offhand weapon, tool, belt, purse, backpack, accessories. Show equipped/worn/held/stowed state separately.
- Character sheet should expose grounded attributes and consequences: strength/carry capacity, endurance/stamina recovery, agility/coordination, perception, health/condition, fatigue, hunger/thirst, warmth, intoxication, injuries, skills and social knowledge. Do not add arbitrary stats without a gameplay consumer and authoritative source.
- Derived effects must be legible: load slows movement/stamina; cold and wet clothing affect warmth; injuries affect actions; skill changes minigame assistance; equipment changes visible character model. Server owns values, Unity visualizes them.

## 4. Minigame framework: variety without mandatory QTE grind

Keep a reusable family of small, contextual interactions. Every activity should choose a suitable interaction, but repeated routine can be completed through a slower/less interactive work mode where ADR 0005 allows it.

| Activity | Possible interaction families |
|---|---|
| Woodcutting | rhythm/chop timing, choose safe felling direction, log splitting |
| Fishing | cast accuracy, bite reaction, line tension/reel balance |
| Gardening | soil/seed sequence, watering rhythm, identify ripe produce |
| Beekeeping | smoke timing, inspect hive signs, careful frame extraction |
| Construction | sequence/fit parts, brace alignment, hammer rhythm |
| Mining | strike weak point, avoid tool damage, sort ore |
| Hunting/trapping | tracking clues, wind/stealth, aim, trap placement |
| Cooking | order/timing, heat control, ingredient selection |
| Market/blacksmith | inspect quality, negotiate dialogue choices, repair/craft steps |
| Caravan | load balance, route watch, guard/scout events |

Difficulty must be based on relevant skill, tool quality, fatigue, weather/location and chosen challenge; show clear easy/normal/hard/master modes where appropriate. Rewards, resource consumption, skill gain and success remain server-authoritative with reqid + correlated ACK. Misses may avoid sending an action only when the server contract defines that attempt as non-consuming; otherwise send an attempt and let the server resolve it. Do not reward purely local scores.

## 5. Tavern cards: restore as real social activities

Recommended staged implementation:

**Stage A — solo NPC tables**
- Ochko against innkeeper.
- Podkidnoy Durak against innkeeper.
- Dice as a quick low-stakes game.
- Tutorial/house rules before the first game; adjustable low/no-stake practice mode.

**Stage B — multiplayer**
- Shared table seats, visible player/NPC avatars, turn ownership, leave/timeout handling, reconnect recovery and spectators.
- PvP Ochko and PvP Durak; private/friend tables later.
- Server-only hands/deck, hidden opponent information, auditable shuffle commitment/reveal, atomic purse/stake settlement and reqid deduplication.
- NPC opponents need distinct temperaments/skill levels and contextual reactions, not omniscient perfect play.

**Stage C — variants and social texture**
- Durak house rules: podkidnoy first; transfer variant only after explicit rule tests; optional 2–4 player table later.
- Ochko rules/configuration and dealer behaviour documented explicitly (legacy code's special natural-21 payout should not be copied without balancing).
- Stakes should be configurable by table and bounded by available funds; no negative balances, duplicate payouts or exploit through disconnect/replay.
- Add table props: deck, card backs, discard pile, trump indicator, coins, hand poses, dealing/sliding cards, NPC gaze/gestures, chatter, spectators and stand-up/seat interactions. Use the same table object and session state in Unity and thin client.

## 6. Unity location templates and immersive presentation

Create data-driven templates, not one giant hard-coded village scene. A template describes geometry anchors, POIs, prop sets, navigation affordances, lighting/audio/weather profiles, local activity definitions and spawn/LoD rules.

Initial template families:
- **Settlement:** hamlet, village, market village, fortified town, city district; density, street pattern, plots, services, population bands.
- **Buildings/interiors:** tavern (bar, card tables, rooms), rented room (bed, chest, wardrobe, candle), blacksmith, market stall, kitchen, stable, workshop, farmhouse, storehouse.
- **Nature:** deciduous/conifer forest, meadow, marsh, riverbank, lake, mountain pass, snowy forest, tundra/cold settlement.
- **Road/travel:** dirt road, paved road, bridge, ford, pass, roadside camp, caravan stop, ambush site.
- **Special:** bandit camp/fort, dungeon/ruin, high-mana anomaly, shrine/ancient site.

Each location should have authored visual grammar: materials, roof/wall variants, clutter, vegetation, decals, soundscape, ambient NPC schedules, weather response, local resources and POI interaction anchors. Reuse modular prefabs but vary silhouette, layout and prop dressing. Keep global coordinates and chunk/region identity authoritative; render only known/discovered region data and do not invent roads or indoor access on the client.

Unity implementation order:
1. reusable location-template ScriptableObjects/data schema and deterministic prop placement;
2. interior room dressing and interaction anchors (rented room + chest is first showcase);
3. tavern table/card presentation and NPC seating;
4. equipment/purse/backpack/chest presentation from server-confirmed state;
5. richer environment and biome profiles, then chunk streaming.

## 7. Delivery order and boundaries

1. Audit current main inventory/housing/card-game support and contract gaps; compare with this legacy audit.
2. Agree the smallest additive server contract for rental-room chest + purse/container transfers. Keep wire v1 and persistence backward-compatible unless a reviewed proposal explicitly changes it.
3. Implement room chest persistence and access checks server-side; add tests for expired rent, permissions, reconnect, concurrent transfer and reqid replay.
4. Add Unity room/chest interaction and purse UI using confirmed state.
5. Restore solo Ochko/Durak with server-owned game state and tests, then multiplayer tables.
6. Expand activity families incrementally; preserve optional low-interaction routine for repetitive work.
7. Build reusable Unity location templates and authored tavern/room set dressing.

**No runtime, wire, persistence or balance changes are authorized by this note.** Claude owns Go/server work; ChatGPT owns Unity/presentation/assets and cross-layer design handoffs. Any new server fields/actions require a separate explicit contract proposal, fixture and tests. Keep the current main's reqid/FIFO/correlated-result discipline.
