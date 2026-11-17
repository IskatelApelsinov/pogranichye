# 0290 — ChatGPT: Unity thick-client integration for v1.30 gather nodes

Дата: 2026-11-17. Связано с Claude handoff [0289](0289-claude-to-all-frontier-life-and-pickups-v130.md).

## Обнаруженный разрыв

Сервер v1.30 уже отдаёт `place.gather[]` (до 45 м) и принимает:
- `op:"goto"` с `site:"g:<cx>:<cy>"` для подхода к находке;
- `op:"pick"` с `node:"g:<cx>:<cy>"` для сбора в радиусе 3 м.

Тонкий клиент показывает «Под рукой», но Unity thick client не имел DTO/нормализации `place.gather` и не показывал эти узлы. Поэтому новые серверные находки были доступны серверу и тонкому клиенту, но не имели понятного игрового пути в Unity.

## Что сделано

- Добавлен optional typed `GatherNodeFrame` для существующего v1 поля `place.gather`.
- `GatherNodeFrameParser.Normalize` отбрасывает malformed/duplicate nodes по отдельности; отсутствие поля и старые v1 snapshots остаются совместимыми.
- Unity character panel показывает имя, тип добычи, направление, дистанцию и остаток.
- До server `here:true` доступен только `goto/site`; `pick/node` появляется только при server-confirmed proximity.
- Оба действия идут через общий `RequestGate` с автоматически созданным `reqid`; UI ждёт `me.result`. Запасы, энергия, дальность, укус пчёл и добавление предмета остаются серверными.
- Добавлены чистые C# regression assertions и шаг Unity-independent CI.

## Архитектурные границы

- Нет новых wire-полей, действий или изменений версии frame v1: используются только уже существующие v1.30 `place.gather`, `goto` и `pick`.
- Не переиспользуются `resources[]`/ `wild[]`; находки — отдельные реальные gather nodes.
- Клиент не создаёт предметы, не предсказывает остаток и не обходит серверную проверку радиуса.
- Это UI/DTO slice. 3D-модели находок из `/api/terrain.gather` остаются следующим отдельным presentation шагом.

## Приёмка

- CI: parser normalization + typed DTO regression checks.
- Unity 6000.6.7f1: проверить список «Под рукой», auto-walk к находке, кнопку сбора только в пределах 3 м, отказ при пустом запасе/недостатке сил, обновление после нового snapshot.
- Live server: проверить как минимум herb, mushroom, berries, hive и deadwood; у hive проверить серверное предупреждение/урон без огня.
