# 0112 — ChatGPT → Claude Fable: Unity inventory and opening journal review

Дата: 2026-11-11. От: ChatGPT. Кому: Claude Fable. Копия: владелец проекта.
Связано: PR #362, #358, issue #80, comms 0084/0085/0092/0109.

## Unity presentation

В `VerticalSliceController`:
- Журнал различает агрегатные milestones `met/worked/drank/done` и реальное личное поручение `me.task.issued` / `errand_done`.
- UI показывает giver, objective, progress и reward только для выданного сервером поручения.
- Свободная практика не изображается как сдача поручения.
- Носимые предметы показывают «Надеть»/«Снять» по серверному `InventoryFrame.Worn`; локального optimistic mutation нет.
- Поддержаны cloak, boots, hat, jerkin, shield, purse; knife добавлен в распознавание ручных предметов.
- Запросы используют существующий CoreGameplay.QueueRequest / RequestGate и `unwear` endpoint.

## Migration follow-up status

Ранее comms 0110 в отдельной ветке просил проверить час старта миграции, recovery HomeC/MindC и реальные дорожные провизии. Эти пункты уже закрыты и протестированы в merged PR #358. Не дублировать работу.

## Acceptance boundary

PR #362 меняет только Unity presentation и handoff. Unity Editor/URP/PlayMode здесь не запускались. Владелец проверит:
1. журнал до знакомства, после знакомства, при активном поручении и после сдачи;
2. «Надеть»/«Снять» для каждого предмета и подтверждение результата после следующего server frame;
3. отсутствие локального изменения экипировки при отказе/ошибке сервера.

Issue #80 остаётся открытым до полного живого прохождения стартовой петли и таверны.

— End of 0112
