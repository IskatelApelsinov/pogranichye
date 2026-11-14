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


## Второй проход — 2026-11-14

- Сверена реальная форма fast кадра с `server.go:fastJSON` и серверным тестом `TestFastFrameShapeAndNoLeak`; подтверждены точные поля и отсутствие `me.id`.
- `WorldFrameParser.Parse(json, out document)` теперь отдаёт исходное проверенное дерево токенов, созданное при том же разборе, что и DTO.
- `CoreGameplay.ApplyFrame(frame, document)` использует это дерево. Убран небезопасный промежуточный `JObject.FromObject(frame)`, который мог раскрыть отсутствующие поля как значения DTO по умолчанию.
- Добавлена editor fixture проверка, что отсутствующие `hunger/pos` остаются отсутствующими в документе.
- Исправлена классификация синтаксически повреждённого JSON: fast-парсер не угадывает его тип по строке, строгий full parser отклоняет его обычным путём.

См. дополнение в `3-dev/comms/0230-chatgpt-client-fast-frame-boundary.md`. Wire и сервер не менялись. Задача остаётся `!В работе`: следующий этап — начать миграцию одной ограниченной группы читателей `CoreGameplay` с JObject на typed view state, не расширяя DTO наугад.


Дополнение к результату второго прохода:
- Проверен терминальный fast envelope из `server.go:fastJSON` (`alive:false` при исчезнувшей сущности). Он теперь проходит через обычный full parser и штатное завершение сессии.
- Fast parser дополнительно валидирует типы `me.pos/combat/wounds/result`, `health/energy/mana`, feed, neighbors и foes.
- Fixture cases покрывают terminal fast lifecycle, неверный тип маркера fast, нечисловое health и неверный тип wounds.
- Все пять изменённых C# файлов повторно считаны из GitHub main после записи.


## Третий проход — инвентаризация typed DTO против фактических потребителей CoreGameplay (2026-11-14)

Повторно сопоставлены текущие обращения к `_me`/`_place` в `CoreGameplay.cs` с `CharacterFrame`/`PlaceFrame` из `WorldFrame.cs`.

**Вывод: мигрировать UI на существующие DTO напрямую пока небезопасно.** DTO описывает лишь часть wire-модели, а gameplay читает больше полей. Среди фактически используемых разделов, которым требуется точная типизированная модель или extension-data с проверенной схемой:
- `me.feed`, `me.stats`, `me.horse`, `me.bag`, `me.region`, `me.journey`, `me.quests`, `me.card_game`, `me.combat`, `me.weave`, `me.task`, `me.talk`, `me.party`, `me.recipes`, `me.world`, `me.inv`, `me.stash`, `me.result`;
- `place.foes`, `place.exits`, `place.site`, `place.life`, `place.market`, `place.stable`, `place.tavern` и связанные поля.

Существующий fast DTO также должен быть отдельным patch-контрактом: он содержит только fastJSON-поля, не полную CharacterFrame/PlaceFrame. Нельзя превращать отсутствующие в fast кадре поля в нули/false и нельзя обновлять ими полный snapshot.

**Следующий безопасный шаг:** отдельная таблица coverage (wire path → DTO type/property → full/fast/optional/null semantics → потребитель CoreGameplay) и добавление только тех DTO, чья форма подтверждена серверным кодом и fixtures. Затем ввести immutable/read-only typed gameplay view с явным merge full + fast patch. Лишь после этого переносить один ограниченный UI-раздел с JObject на typed view.

До выполнения coverage table и fixtures не менять массово `_me`/`_place` selectors. Это предотвращает ложную типизацию и потерю additive v1 полей.

Unity compile/Editor/PlayMode по-прежнему не запускались.


## Четвёртый проход — coverage table и первый typed gameplay consumer (2026-11-14)

- Добавлен source audit `3-dev/docs/CLIENT-WIRE-DTO-COVERAGE.md`: фактические поля `stateJSON/fastJSON`, покрытие DTO, потребители CoreGameplay и список оставшихся gaps.
- В `CharacterFrame` добавлено отображение уже существующего `me.drunk`; wire не менялся.
- Компактный `DrawStatus` переведён с JObject-селекторов health/stamina/mana/drunk на immutable `GameplayStatusView`.
- `FastFrameParser` теперь дополнительно строит presence-aware `GameplayStatusPatch` для health/energy/mana/pos.stam. Отсутствующее поле сохраняет предыдущее значение; явный null сбрасывает значение в ноль, как прежний JObject selector после shallow merge. Drunk остаётся из полного кадра, поскольку сервер не присылает его в fast.
- Добавлен pure C# regression check и editor fixture check для full → partial fast → explicit-null merge.
- Остальные потребители `CoreGameplay` по-прежнему используют проверенное исходное JSON-дерево; массовой миграции нет.

Проверки в этой среде: только source/diff review. Pure C# check должен быть выполнен GitHub Actions после push. Unity compile, Editor fixture menu и PlayMode не запускались; задача остаётся `!В работе`.

- GitHub Actions: [run #43633890291](https://github.com/IskatelApelsinov/pogranichye/actions/runs/43633890291) — `Unity-independent C# checks` PASS; `GameplayStatusView checks passed` (mcs + mono). Unity Editor/fixture menu/PlayMode остаются непроверенными.
