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


## Progress — 2026-11-14: world-label occlusion and readability

- Source review confirmed that the generated Layout v2 already has server-footprint collision proxies on layer 2: a footprint BoxCollider for ordinary buildings and wall-segment BoxColliders around the tavern door. Starter prototype houses also have layer-2 camera-only proxies. The bug was that `SignFaceVisibility` and `BillboardLabel` queried only `Physics.DefaultRaycastLayers`, which excludes layer 2.
- Added a shared `WorldLabelVisibilityPolicy`: layer 2 remains excluded from ordinary interaction probes, while sign/nameplate occlusion queries explicitly include the existing proxies.
- No new collider geometry or gameplay obstruction was added; the fix reuses the existing presentation/camera collision proxies and leaves server movement and POI geometry unchanged.
- Reduced building-sign range to 24 m and NPC/world-label range to 45 m; increased the physical glyph size on the settlement and starter signs.
- Added pure C# regression checks for range, facing threshold, and the reserved occluder layer. GitHub Actions result is pending; Unity Editor/PlayMode has not been run.
- Runtime acceptance still required: inspect signs from both sides of the tavern/shops, verify that walls hide nameplates, and confirm text readability at normal gameplay distance.
