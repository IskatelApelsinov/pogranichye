# 0163 — ChatGPT → Claude: Unity world readability, activity access and inventory path

Дата: 2026-11-13. Продолжение PR #456.

Владелец повторно проверял клиент и сообщил о четырёх проблемах: деревья/кусты стоят в зданиях и их мало; неясно назначение зданий (не нашёл корчму); рубка/рыбалка и мини-игры плохо обнаруживаются без входа в меню; карта не масштабируется; купленные предметы не видны в инвентаре.

## Клиентские изменения в PR #456

1. **Растительность**
   - Generated woodland: вместо density 1 дерево на 260 м² с cap 90 — area-based target 1 дерево на 150 м², cap 260 на зону, с retry для отброшенных точек.
   - Добавлен слой низких кустарников: area-based target 1 куст на 38 м², cap 520 на зону.
   - Деревья/кусты — combined meshes, без отдельных GameObjects и colliders.
   - Отбрасываем позиции рядом с footprints зданий, дорогами, POI и непроходимой рекой.
   - Starter woodland поднят до цели 150 деревьев и 300 кустов; старые деревья в core/buildings/paths фильтруются.
2. **Навигация по поселению**
   - На публичных generated buildings (tavern/smithy/granary/market) появились двусторонние вывески с серверным building name.
   - Вывеска стартовой корчмы увеличена и переименована в «КОРЧМА».
3. **Карта**
   - Zoom slider 1×–4×, при увеличении центр — текущая позиция игрока.
   - Projection перестал clamp-ить удалённые точки к краю; геометрия viewport-clipped. Добавлен Editor fixture case на off-screen point.
4. **Доступность рубки/рыбалки**
   - Контекстная панель появляется возле POI леса/реки вне зависимости от активной вкладки и свёрнута ли HUD-панель.
   - Если инструмента нет, панель предлагает купить его через result-gated op take; если есть, но не в руке — equip; если в руке — старт локального timing/bite minigame.
   - G — рубка, J — рыбалка. Добавлены действия в central ClientInput, не Legacy Input polling.
   - Minigame UI теперь рисуется вне прокручиваемого Character tab.
   - Cursor становится видимым, а camera look приостанавливается, когда указатель над интерактивной HUD/context/minigame областью.
5. **Инвентарь**
   - Проверил серверный путь: sim.Take после проверки точки/кошеля/денег добавляет Inv.Items[item]++; meInv сериализует эти Items в me.inv.items. Market trade покупает bulk goods в stash, не в item inventory.
   - Клиент разъясняет это разделение; contextual axe/rod purchase использует тот же Take + RequestGate, без optimistic inventory mutation.

## Просьба к Claude

Проверь контракт и server-side E2E без изменений wire format:
- На успешный take axe/rod/sword/bow сервер возвращает в следующем full snapshot me.inv.items с увеличенным количеством и me.result с тем же reqid; повтор reqid не списывает деньги дважды.
- op trade side=buy остаётся в me.stash, не me.inv.items; подтвердить, что это ожидаемое разделение, и проверить, что Unity UI не показывает это как предмет экипировки.
- Для каждого layout plan (starter/street/cross/ring/scatter) выведи количество/площади forest/copse zones и проверь, что их геометрия не пересекает building footprints/road corridors/POI approaches. Сообщи, если мои clearance/rate значения создают слишком пустой лес или чрезмерную нагрузку.
- Подтверди, что /api/layout building name и kind стабильны и локализованные имена безопасны для TextMesh. Если имя пустое — Unity использует kind fallback.
- Подтверди, что server range check для chop/fish совпадает с POI anchors и радиусом 10 м.

Unity Editor/PlayMode не запускался. Нужен владелецский runtime pass: посмотреть растительность вокруг домов/дорог, видимость вывески корчмы, курсор над UI, G/J near POIs, покупку/экипировку axe/rod, фактический результат мини-игр и map zoom.
