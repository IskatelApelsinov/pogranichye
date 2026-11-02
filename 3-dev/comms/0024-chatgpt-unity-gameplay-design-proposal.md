# Pogranichye — Unity gameplay direction proposal

Date: 2026-11-02  
Owner: ChatGPT / Unity thick-client track  
Status: proposal for owner + Claude review; **not a protocol change**

This note captures a direction for the Unity client and the first playable test build. It does not authorize changes to the v1 snapshot or intent envelope. Server-side outcomes remain authoritative.

## 1. Camera and player embodiment

The agreed direction is hybrid:

- First-person is the primary exploration and combat camera.
- Third-person is available for social scenes, equipment/appearance inspection, and selected domestic or crafting actions.
- The full 3D player entity remains networked and represented in the world in both modes.
- Camera switching must not create a second player representation or alter authoritative position.
- Local camera look is presentation/input only. Movement, collision, stamina, attacks, inventory, and interaction outcomes must be server-authorized once those intents exist.

Current Unity work adds a prototype first-person camera mode: hold RMB and move the mouse to look; the player root rotates in yaw and only the arms remain rendered. Third-person remains the default. This is a camera/presentation prototype, not yet networked locomotion.

## 2. Scale and spatial conventions

Proposed Unity convention: **1 Unity unit = 1 metre**.

- Humanoid target height: approximately 1.8 m.
- Door height: 2.0–2.2 m; ordinary interior ceiling: 2.5–3.0 m.
- Tavern counter: about 1.1 m; table: 0.75–0.85 m.
- Interact range should be short and contextual (roughly 2–3 m), not a long-distance click on a map marker.
- The current village is a compact tutorial diorama, not a final open-world scale reference. Use a clear playable village core and later stream/compose larger regions from server metadata.
- Server coordinates and client presentation geometry must be kept separate until canonical world metadata is agreed.

## 3. Interaction model

Move from clicking distant floating POIs toward a first-person contextual interaction loop:

1. Camera ray checks a small centre reticle.
2. A nearby valid target shows a short prompt (NPC name/role, door, station, item).
3. Press interact to send a typed intent.
4. UI shows pending state; only a server snapshot/event confirms success or failure.
5. Interaction can be cancelled; no client-side inventory or world mutation.

NPC selection currently sends only the existing generic **socialize** goal. It is not targeted dialogue: v1 does not transmit a target entity ID with the goal. A later contract should include a target entity ID and a typed interaction payload.

## 4. Social play and NPCs

The village should feel inhabited rather than like a set of static quest markers:

- NPC daily routines: work, eat, rest, socialize, move between canonical server POIs.
- Contextual conversations: greet, ask about work, trade, request help, learn rumours. Dialogue choices are client presentation; server validates consequences and relationship changes.
- Relationships should be legible but not reduced to a single arbitrary “friendship” bar: trust, familiarity, reputation, obligations/debt and faction attitude can be separate server-owned values.
- Social camera can shift to third person or a close conversational framing, but should never take control away without a clear input.
- In the first tutorial, use a small cast with distinct roles and repeatable names/IDs from the fixed starter seed.

Current client has selectable NPC prototypes. Clicking one sends **socialize**, with UI text explicitly noting that addressable dialogue is not yet part of v1.

## 5. Tavern and beer — mandatory test-build feature

**Acceptance requirement:** the player must be able to order/buy and actually drink a beer in the tavern. A decorative mug or a generic socialize action alone does not satisfy this requirement.

Minimum playable loop:

1. Approach the bar / interact with the innkeeper.
2. See beer availability and price.
3. Confirm purchase/order.
4. Server checks stock and coins, then records the serving/consumption transaction.
5. Unity plays serving and drinking animation, mug hand attachment, sound and concise feedback only after the authoritative result.
6. Server exposes a small intoxication state (or equivalent status) in the snapshot/event. Unity may apply mild camera sway, altered post-process/blur, or reduced steadiness based on that server value; it must not invent the intoxication value locally.
7. State decays according to server time/rules. Drinking again must respect stock, money, and any gameplay limits.

### Shared beer with another player (planned multiplayer milestone)

This should be a real shared network activity, not two clients independently playing the same local animation.

- Both players must be present in the same server-authoritative world instance, with stable player/entity IDs and canonical positions.
- A player approaches the innkeeper/bar and starts a **co-drink invitation** addressed to a specific nearby player. The other player explicitly accepts or declines; no forced animation or purchase.
- The server validates both players' proximity to the tavern/table, availability, funds, stock, session state and each player's consent. It creates one shared session ID and emits an authoritative start/result event to both clients.
- Each participant's beer purchase/consumption is recorded separately (unless a later design explicitly introduces a host-paid round). A failed purchase for one participant must not silently charge the other.
- The server synchronizes the shared activity phase (invited → accepted → serving → toast/drink → finished/cancelled) and a common event timestamp/sequence. Clients align seating/standing positions, mug hand attachment, toast gesture, sound and animation to that phase.
- Intoxication remains per-player server state. The shared animation is synchronized; the gameplay effects are not shared or inferred from the other player's client.
- Disconnect, movement out of range, death, cancellation or insufficient stock/funds must resolve the session server-side and notify the remaining participant. Reconnect should recover the current session state from snapshot/event replay.
- Do not use direct peer-to-peer authority. Server relays the accepted session state to both clients; clients only render it.

**Important dependency:** the current v1 snapshot does not expose a roster of other connected player characters or canonical positions. A true co-drinking loop therefore needs a coordinated protocol extension (or agreed v2) for player presence/identity/position, targeted invitation/acceptance intents, shared activity events/state and per-player intoxication. This is a natural follow-up once the single-player beer loop and movement/position contract are agreed with Claude.

The current v1 goals are **socialize**, **work**, **eat**, and **rest**; there is no confirmed beer/drink intent. Claude has been asked in PR #11 to propose the smallest server-authoritative contract and backward-compatible optional snapshot field or a separately agreed v2. Do not silently map beer consumption to **socialize** or **eat**.

## 6. Combat direction

Target feel: deliberate, readable, dangerous medieval melee — more tactical survival sim than arcade click-spam.

- First-person hands/weapon presentation with optional third-person body feedback.
- Explicit combat intents: equip, attack (attack type/direction), guard, feint, dodge/step, release/stop guard. Exact envelope requires server design.
- Server validates weapon, stamina, timing, range, facing, target, armour, wounds and hit result.
- Client may predict animation and camera response, but not damage, hit confirmation, stamina cost, death or loot.
- Use clear attack wind-up / active / recovery phases and readable audio/animation telegraphs.
- Stamina and wounds should matter; avoid infinite blocking and weightless rapid attacks.
- Start with one training weapon and a training dummy; then a controlled NPC sparring test. Do not put unrestricted PvP in the first village build.

## 7. Crafting and work

- Stations define context: forge, workbench, cooking hearth, herbalist table, building site.
- UI previews recipe, required ingredients, output, time and tool/station requirements.
- Server owns recipe validity, item counts, quality, durability, output and ownership.
- Client owns interaction presentation: station camera, progress display, hand/tool animation, particles, sound.
- Work goal in v1 remains a generic activity and must not be presented as a fully implemented crafting transaction.
- First slice: one simple recipe at the smithy after inventory/item contract is available; one cooking/beer-related tavern flow.

## 8. Magic direction

Magic should feel like constructing and controlling a pattern, not selecting a spell from a static hotbar.

- Compose a spell from nodes (source, element/force, shape, target, modifier) and links.
- First-person: trace/aim the pattern in the world; third-person or a dedicated focus view: inspect/edit the node graph.
- Server validates learned nodes, links, costs, range, target legality, cooldown, interruption and final effect.
- Client previews the pattern and renders effects; server owns effect, damage, duration, resource consumption and failure.
- Early test: one harmless utility spell and one simple offensive spell in a controlled test arena. Keep magic separate from the tavern/social slice.

## 9. Milestone order

1. Tutorial village readability, hybrid camera, local interaction prompts and NPC selection.
2. Tavern interior and the server-backed beer purchase/drink loop (mandatory test-build acceptance).
3. Social dialogue target contract and a few repeatable NPC routines.
4. Server-backed movement/interaction and canonical POI coordinates.
5. One complete crafting recipe.
6. One controlled melee combat loop.
7. First composable magic prototype.

## 10. Contract and ownership boundaries

- Do not change v1 envelope/snapshot silently.
- Claude owns server simulation, world generation, inventories, transactions, combat, crafting, magic and authoritative NPC state.
- ChatGPT owns Unity rendering, input presentation, animation, camera, HUD, interaction affordances and protocol adapter.
- New intents/fields must be proposed in a separate PR or design thread and agreed before Unity integration.
- All PRs remain draft until the project owner’s review.


## 9. Учебная деревня: последовательность обучения и видимые станции

Цель первого полноценного тестового билда — не пустая техническая площадка, а маленькое обжитое поселение, где игрок естественно знакомится с основными системами. Станции можно расставить заранее как presentation assets, но подсказка «доступно» появляется только после реализации серверного действия.

Рекомендуемый маршрут обучения:
1. **Осмотр и перемещение** — FPS/TPS, мышь, ходьба/бег, остановка, обход препятствий. Сервер подтверждает canonical position.
2. **Взаимодействие** — подойти к объекту, увидеть контекстную подсказку, нажать E, дождаться серверного результата.
3. **Таверна и социальная жизнь** — познакомиться с корчмарём и жителями; адресный диалог/приглашения только после target-ID контракта.
4. **Пиво — обязательный отдельный этап** — увидеть цену и наличие, подтвердить покупку, получить кружку, выпить; монеты/запас/consumption/intoxication подтверждает сервер. Декоративная кружка или socialize не засчитываются.
5. **Рынок и еда** — посмотреть цену/остаток, купить или съесть только поддерживаемый сервером продукт.
6. **Огород и сбор** — посадка/сбор с серверной проверкой сезона, зрелости, инструмента и инвентаря.
7. **Мастерская и крафт** — рецепт, ингредиенты, инструмент, время; сервер рассчитывает результат и качество.
8. **Рыбалка** — у берега выбрать снасть/наживку, забросить, пройти короткую реакционную мини-игру, получить улов только после серверной проверки времени/места/снасти/результата.
9. **Лук и тренировка** — ПКМ прицеливание, ЛКМ выстрел; клиент рисует траекторию/анимацию, сервер проверяет боеприпас, выносливость, направление и попадание. Учебная мишень не должна выдавать локальный урон.
10. **Бой и защита** — подготовка/активная фаза/восстановление, Q-защита, F-финт, выносливость и раны на стороне сервера.
11. **Магия** — отдельное gesture-capture состояние для плетения; сервер проверяет состав узора, стоимость, дальность, цель, cooldown и эффект.

Сцена уже получает визуальные ориентиры для нескольких будущих этапов: таверна, кузница, рынок, огород, ручей с мостом, рыбацкий настил, тренировочная мишень/манекен и верстак. Пока движение и соответствующие intent-контракты отсутствуют, эти объекты остаются декорациями и не должны притворяться работающими мини-играми.

Для тонкого клиента тот же маршрут нужно представить в компактной 2D-карте/панели активностей: тап по месту открывает карточку действия, карточка показывает требования и состояние, мини-игра запускается только после серверного разрешения. Мобильный ввод — крупные touch targets, без обязательного drag precision; для рыбалки/лука можно использовать удержание/отпускание и короткие timing windows. Любая награда, трата ресурсов, покупка, попадание и состояние опьянения остаются authoritative на сервере.
