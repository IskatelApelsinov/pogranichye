# 0214 — ChatGPT: сохранить стартовый Layout v2 как игровые данные

- Дата: 2026-11-13
- От: ChatGPT (Unity/client)
- Кому: владелец и Claude
- Основа: `main` `da7cc124a3939ad051e2bad43db08fcda3b041af`
- Связано: issues #39, #40, #80; PR #571–#579; comms 0211–0213

## Что проверено

- Актуальный `main`, последние коммиты и открытые PR #571–#578; issue #80 остаётся открытым, #39 и #40 закрыты.
- Полностью прочитаны handoff 0211 и ответ Claude 0212; проверены новые PR #573, #575–#578 и сообщение 0213 из PR #578.
- `HttpAuthClient.GetLayoutAsync()` документирует no-id `/api/layout` как tutorial layout. При подключении Unity загружает этот Layout v2 и строит `ServerLayoutV2`.
- В `VerticalSliceController.EnsureLayoutForPlace` первый snapshot со `plan:"starter"` (и первый legacy snapshot без plan) сбрасывал `_layout = null`. При этом `ResolveMove` переходил на fallback `Vector2.ClampMagnitude(..., 29.5f)`, `IsNearActivityPoi` переставал находить точки, а вкладка карты показывала «серверная карта недоступна».
- При возвращении из generated settlement требовалось восстановить именно default tutorial layout, а не оставить последнюю generated geometry.

## Изменено

- Добавлен `SettlementLayoutSelectionPolicy.cs`: явный starter plan имеет приоритет; legacy first snapshot и возврат к известному starter ID обрабатываются отдельно; generated plan не маскируется старым ID.
- `VerticalSliceController.cs` сохраняет default no-id layout в `_starterLayout` и повторно выбирает его для tutorial place.
- Добавлен `SettlementLayoutSelectionPolicyCheck.cs` — 12 проверок.
- Workflow `unity-independent-csharp-checks.yml` запускает новый тест.
- В `2-client/game/README.md` описана гарантия сохранения collision/POI/map данных.

## GitHub

- Ветка: `fix/unity-retain-starter-layout-gameplay-20261113`
- PR: https://github.com/IskatelApelsinov/pogranichye/pull/579 (Draft, открыт, mergeable=true)
- Коммиты: `00a4924`, `5d11094`, `d7e4ab0`, `311b3e3`, `5191899`, `5d6a5fb`, `90ba3a2`.
- Файлы: workflow, новый policy, `VerticalSliceController.cs`, README, новый regression check.
- Main не менялся; wire v1, server state, POI координаты, навигация и размеры мира не менялись.

## Проверки

GitHub Actions run #145: https://github.com/IskatelApelsinov/pogranichye/actions/runs/43488822272 — **success**.

Фактический output:
- `SettlementLayoutSelectionPolicy: 12 checks passed`
- RequestGate — ALL CHECKS PASSED
- MovementIntentQueuePolicy — 3 checks passed
- InputEventDebugState — ALL CHECKS PASSED
- NpcActivitySoundPolicy — ALL CHECKS PASSED
- Drink presentation policy — all checks passed
- Hex cell coordinate — 24 checks passed
- Hex cell streaming policy — 222 checks passed

## Ограничения и риск

- Локально в контейнере нет `mcs`/Mono и Unity Editor. C# policy реально собран и запущен GitHub Actions, но весь Unity-проект не компилировался.
- Unity 6000.6.7f1 Editor/PlayMode не запускались. Нужно вручную проверить движение за пределами прежнего 29.5 м fallback, коллизии домов/реки, карту, контекстные кнопки POI и возврат A→starter.
- Вход в таверну этим PR не решается: как зафиксировано в 0209/0212, нужен согласованный серверный door/portal контракт.

## Следующая задача

После локальной проверки PR #579 — отдельно сверить POI-объекты и `WorldLayoutPresentationBuilder` с локальными координатами `layout.Pois` и anchor, не меняя координаты сервера. Затем согласовать минимальный doorway/room контракт для физического входа в корчму и общего интерьера.

Merge не выполнялся.
