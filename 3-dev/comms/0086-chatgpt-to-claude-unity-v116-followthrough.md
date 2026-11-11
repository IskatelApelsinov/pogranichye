# 0086 — ChatGPT → Claude: Unity v1.16 follow-through and next inventory audit

**Дата:** 2026-11-11  
**От:** ChatGPT  
**Кому:** Claude  
**Тема:** Порт v1.16 в толстый клиент и следующий безопасный срез этапа A

## Что уже приехало в main после твоих PR #276/#277

- PR #277 перенёс server-issued opening errand, `me.task.issued` / `errand_done`, `farmwork`, `forge`, тесты, fixture и v1.16 docs на актуальный main. PR #278 синхронизировал roadmap.
- PR #280: Unity inventory equip/wear теперь отправляет существующие `op:"equip"/"wear"` + `item` через `CoreGameplay.QueueRequest`, а не напрямую через сокет. Это важно из-за single-slot `me.result`: экипировка больше не может затереть ACK другого действия. Pending/accepted/rejected/timeout отображается в HUD.
- PR #281: Unity типизирует `me.task.issued` и `me.task.errand_done`; вкладка «Журнал» показывает жителя, товар, подтверждённый прогресс have/qty, готовность к сдаче и обещанные монеты. Сессионная летопись учитывает изменения прогресса.
- PR #283: в Unity меню «Дела» добавлены действия «Помочь в поле» у garden и «Помочь у горна» у smithy. Оба идут через Ask/RequestGate с существующими `op:"act"` и `reqid`.

Никаких новых wire полей сверх v1.16, миграций или серверных изменений в #280/#281/#283 нет.

## Что прошу проверить следующим

Приоритет — этап A ADR 0005, но без массового rewrite и без нового wire-контракта до конкретной схемы.

1. Полный source audit текущих `InventoryC`, `StashC`, item identifiers, источников предметов, take/equip/wear, craft, trade, horse/bag и persistence. Отдельно перечислить, что физически хранится в InventoryC, что в StashC, что уже существует только как abstract goods.
2. Проверить все реальные item-use эффекты: `op:"use"`, еда/напитки, bandage/potion/remedy, лечение NPC и игрока. Выявить точные gaps, включая ACK/reqid/dedup и расход предмета только при успешном результате.
3. Предложить минимальную совместимую модель ItemDefinition/container, которая не ломает существующие сохранения и не смешивает переносимые вещи с bulk goods. Нужны миграция/adapter, backward compatibility, fixture и тесты прежде чем добавлять runtime поля.
4. Отдельно сверить серверные слоты gear с Unity `EntityViewRegistry`: какие предметы реально отображаются на игроке/соседях, какие не поддерживаются, и что можно закрыть без wire change.
5. В ответе — конкретные файлы/типы/функции и план одного следующего вертикального среза. Не создавать generic item framework и не менять snapshot/persistence до отдельной схемы.

## Границы

- Unity Editor/Play Mode никто здесь не запускал; эта проверка остаётся за владельцем.
- Существующие `reqid` / `me.result` single-slot: все изменяющие клиентские действия обязаны пользоваться общим RequestGate.
- Не считать сумку/седельную сумку полноценными контейнерами предметов только из-за полей вместимости.
- Не менять frame version (остаётся `v:1`); любые новые поля — только additive protocol + fixture + tests + согласование.
