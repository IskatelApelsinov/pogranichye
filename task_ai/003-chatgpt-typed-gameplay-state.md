!В работе
# 003 — ChatGPT: единый типизированный поток состояния для CoreGameplay

**Исполнитель:** ChatGPT  
**Подсистема:** Unity client / protocol integration.  
**Зависит от:** `task_ai/001-chatgpt-client-audit.md`, отчёт `3-dev/comms/0229-chatgpt-unity-client-audit-initial.md`.

## Цель

Убрать архитектурное расхождение, при котором полный snapshot сначала строго разбирается в `WorldFrame`, но затем исходный JSON повторно разбирается внутри `CoreGameplay`, а fast frame обходит `WorldFrameParser`. `CoreGameplay` должен получать ограниченную типизированную модель состояния, а не владеть JSON-парсером.

## До изменения кода

1. Повторно прочитать `AI_WORK_RULES.md`, действующий протокол, `WorldFrame.cs`, `WorldFrameParser.cs`, `CoreGameplay.cs` и соответствующие server comms.
2. Инвентаризировать все поля, которые `CoreGameplay` сейчас читает из `me`, `place`, `feed`, `foes`, `exits`, `journey`, combat/weave/mount и result.
3. Отдельно установить точную форму fast frame по серверному коду/документам; не угадывать DTO и не расширять snapshot parser так, чтобы он принимал неполный frame как полный.
4. Определить минимальный typed gameplay state/adapter, совместимый с текущим wire v1.

## Реализация

- Не менять серверный wire contract.
- Не менять серверные решения, intents, reqid/idempotency или семантику действий.
- Full frame: один строгий parse → typed state → EntityViewRegistry/CoreGameplay/прочие потребители.
- Fast frame: отдельный валидируемый typed DTO/patch с явной семантикой merge; не строковый поиск и не произвольное слияние непроверенного JSON.
- На первом шаге можно мигрировать только одну ограниченную группу потребителей, если полная миграция слишком велика. Не делать big-bang rewrite.
- `CoreGameplay` не должен самостоятельно разбирать JSON. Не сериализовать DTO обратно в JSON для сохранения старого пути — это не решает проблему.

## Проверки и приёмка

1. Использовать protocol fixtures и добавить тесты на валидный full frame, malformed fast frame, пропущенные/null optional fields, несовпадающий reqid/result и корректное обновление частичного fast patch.
2. Старые v1 full snapshots остаются совместимыми.
3. Поля, не пришедшие в fast frame, не затирают последнее full state значениями по умолчанию.
4. Никакие визуальные успехи не появляются без серверного подтверждения.
5. Указать точные тесты/результаты. Если Unity Editor недоступен — явно разделить pure C# тесты и не выполненную PlayMode проверку.

## Следующий владелец

После минимального typed pipeline ChatGPT продолжает выделение UI/ViewModel; Claude нужен только если аудит выявит, что текущий fast contract недостаточен или неоднозначен. В таком случае сначала handoff в `3-dev/comms/`, без молчаливого wire change.


## Промежуточный результат — 2026-11-14

Выполнен первый безопасный шаг, см. `3-dev/comms/0230-chatgpt-client-fast-frame-boundary.md`:
- добавлен отдельный `FastFrameParser` и оболочка `FastFramePatch`;
- fast frame валидируется отдельно и передаётся в `CoreGameplay` не как сырая строка;
- fast place применяется как patch, отсутствующие в нём поля полного состояния сохраняются;
- full frame передаётся в `CoreGameplay` как уже проверенный `WorldFrame`, повторный разбор wire JSON удалён; временный JObject adapter оставлен для legacy selectors;
- добавлены editor fixture checks на валидные/невалидные fast кадры.

Wire v1 не менялся. Unity Editor/fixture menu/PlayMode не запускались, компиляция не подтверждена. Задача остаётся `!В работе`: следующий этап — убрать JObject adapter и перевести используемые gameplay selectors на компактную typed view model после сверки реальной серверной формы fastJSON.
