!В работе
# 005 — ChatGPT: восстановление Unity-клиента для играбельного демо

**Исполнитель:** ChatGPT  
**Подсистема:** Unity thick client / input / presentation / UI / streaming.  
**Основной backlog:** `3-dev/docs/ПЛАН-ВОССТАНОВЛЕНИЯ-ДЕМО.md`.  
**Серверный handoff:** `3-dev/comms/0232-chatgpt-to-claude-recovery-plan-server-priorities.md`.

## Цель

Последовательно устранить пользовательские жалобы на клиент, не превращая Unity в отдельную игру и не дублируя серверную симуляцию.

## Приоритеты ChatGPT

### P0
1. Продолжить `task_ai/003`: убрать постепенными безопасными шагами JObject-зависимость CoreGameplay; typed state/patch должен отражать реальный server wire.
2. Продолжить `task_ai/004`: довести диагностические дампы до подтверждённой Unity compile/runtime проверки после пользовательского теста.
3. Перепроверить управление: мышь была слишком чувствительной, спринт слишком коротким; проверить настройки и ввод в контексте всех модальных режимов.
4. Восстановить/проверить взаимодействия NPC/POI: raycast, стены/коллайдеры, дистанция, диалоговые кнопки и крупная читаемая реплика.
5. Проверить layout presentation: дома не перекрываются, деревенский масштаб, двери/проходы и коллайдеры согласованы с серверными POI.
6. Проверить EntityViewRegistry и NPC presentation: стабильные server IDs, WPOS projection, AOI enter/leave, act_code-driven animation; никакого локального NPC spawn.

### P1
7. Инвентарь/рабочая сумка/экипировка: ясное разделение контейнеров и видимый предмет в руке/надетые вещи из server state.
8. Таверна: стойка/корчмарь, кружка, server-confirmed drink, понятная обратная связь; визуальные жители только по server entities.
9. Лошадь: mount/rider presentation, камера/управление, предметы экипировки — по серверному состоянию.
10. Карта: zoom/pan, поселение ↔ большой мир, знания персонажа, туман; не рисовать developer manifest как известную карту.
11. Знаки/шрифты/UI: occlusion, разумная дистанция/размер, читабельные диалоги, не перегружать экран техническими подсказками.
12. Визуальное качество: убрать бросающиеся в глаза placeholder-формы в кружках/инструментах/предметах; URP materials/shaders, vegetation/terrain, дальний туман, базовые анимации.
13. Мини-игры: корректный input focus и анимационная/визуальная обратная связь без локального предсказания результата.

## Ограничения

- Не менять Go server и wire contract без отдельного comms handoff.
- Не создавать локальную истину о NPC, предметах, POI, ресурсах, дорогах или проходимости.
- Не удалять Starter Cauldron или Open World Start.
- Не делать big-bang rewrite CoreGameplay/VerticalSliceController.
- Не маркировать задачу выполненной до Unity compile и соответствующей runtime проверки.
- Все изменения малыми итерациями, фиксировать точные source files и проверки.

## Порядок

Начать с task 003 и P0.1–P0.5. Не полировать ассеты, пока базовые управление/интеракции/геометрия не стабильны. После этого P1 inventory/tavern/horse/map/presentation.

## Критерии готовности

- Чистая Unity compile на 6000.6.7f1.
- Пользователь проходит smoke-test от входа до диалога, поручения, таверны и перемещения.
- Клиент отображает только server-authoritative entities/results.
- Карта не раскрывает неизвестные места.
- Никакие локальные visual success states не обходят server result.
- Все непроверенные runtime аспекты явно помечены.

**Следующий владелец:** ChatGPT продолжает клиент; пользователь выполняет Unity runtime smoke tests; Claude — серверные зависимости по comms 0232.


## Consolidation note — 2026-11-14

Typed HUD work from overlapping PRs #635 and #636 is consolidated in PR #638. Do not merge #635 or #636 independently; both will be closed as superseded once PR #638 is published. The complete status-view implementation from #636, the typed feed slice, DTO coverage inventory, recovery notes and human Unity acceptance checklist are retained. The seamless position parser fix remains in place and must not be duplicated.


## Typed result continuation — 2026-11-14

PR #638 now also types the existing single-slot `me.result` acknowledgement. `ActionResultFrameParser` validates full and fast results; `RequestGate` semantics remain unchanged (one in-flight request, matching reqid only). No server/wire change. Pure C# checks all passed in workflow run #43635516351 (17 steps); Unity Editor/PlayMode remain unverified.


## Typed combat/foe continuation — 2026-11-14

PR #638 now types the existing server `me.combat` view and `place.foes[]` arrays in both full and fast frames. `CoreGameplay` consumes typed combat values and server-owned foe frames; no local foe simulation or server changes. Parser checks and Editor fixture cases were added; all 18 CI steps passed in run #43636660993. Unity Editor/PlayMode remain unverified.


## Current handoff — 2026-11-14

PR #638 is open and ready for review, not merged. All 18 independent C# CI steps passed in run #43636660993. It consolidates typed status/feed/result/combat/foe views and the durable task/handoff documents. The manual Unity acceptance task is now `task_human/004`; do not mark it complete until the owner runs Unity 6000.6.7f1.


## Stacked follow-up — 2026-11-14

PR #638 now includes typed public JourneyView/QuestsView consumers in the same consolidated client change. All 19 independent C# CI steps passed in run #43638732127. No server/wire change; Unity runtime remains unverified.


## Next client DTO slice

Task #007 records the current `me.stats/me.bag/me.horse` server view audit and the existing but unconnected `HorseStatsFrames.cs` scaffold. No server change is requested; `me.bag` must not be misrepresented as a physical backpack.


## Typed inventory foundation — 2026-11-14

Task #007 completed the client-side DTO/consumer foundation for stats, carried load summary and horse state. Next continue with inventory items/equipment, regional map, card game, recipes and party, then settlement stable/site/market/exits/tavern counter. No server change.


## Regional knowledge map — 2026-11-14

The `me.region` projection and UI are now typed under task #008. Privacy checks reject unknown-place name/tier leaks; the client does not use the developer manifest to fill unknown map data. CI run #43638994608 passed all 21 independent C# steps. Runtime map acceptance remains with the project owner.
