# 0230 — ChatGPT: первый шаг к единому клиентскому потоку состояния

Дата: 2026-11-14  
Статус: изменения записаны в `main`; Unity Editor/PlayMode не запускался.

## Что изменено

1. Добавлен `2-client/game/Assets/_Project/Scripts/Networking/FastFrameParser.cs`.
   - Распознаёт fast frame по JSON bool `fast:true`, а не поиском подстроки.
   - Проверяет `v:1`, `alive:true`, типы `me/place`, массивы `feed/neighbors/foes/exits`, объект `combat/result`, строковые ID соседей и врагов.
   - Возвращает `FastFramePatch`; malformed fast не передаётся в full snapshot parser.
   - Не требует от fast кадра `me.id`, потому что он намеренно отсутствует по протоколу.

2. `VerticalSliceController.OnSocketMessage` теперь сначала пробует отдельный валидатор fast-патча. Если это fast — передаёт типизированную оболочку в CoreGameplay; иначе запускает прежний строгий `WorldFrameParser.Parse`.

3. `CoreGameplay.ApplyFastFrame` принимает `FastFramePatch`, а не сырую строку JSON. Важный дефект исправлен: place из fast кадра теперь накладывается как patch, а не заменяет весь `_place`. Поля полного кадра (anchor/layout/stock/tavern и другие отсутствующие в fast) сохраняются. Списки foes/exits обновляются только когда соответствующее поле действительно пришло.

4. Полный кадр теперь передаётся в `CoreGameplay.ApplyFrame(WorldFrame)` уже после строгого разбора в контроллере. Временный legacy adapter создаёт JObject из проверенного DTO для существующих селекторов. Повторного разбора недоверенной wire-строки внутри CoreGameplay больше нет. Это промежуточный шаг: сами UI/gameplay selectors ещё предстоит постепенно перевести на typed view models.

5. В `ProtocolFixtureValidator` добавлены случаи для валидного fast patch, обычного full frame, неправильной версии/alive, неверных типов и некорректного foe ID.

## Протокольные гарантии

- Wire v1 не менялся.
- Серверная симуляция, частота fast frames и семантика действий не менялись.
- Full frame остаётся единственным источником полной базовой модели; fast patch не затирает отсутствующие поля.
- Не добавляли локальные игровые решения или предсказание результатов.

## Проверка

- Изменения записаны в GitHub `main`; файлы повторно прочитаны через GitHub API.
- Source review выполнен для четырёх изменённых файлов.
- Unity Editor, shared fixture menu и PlayMode в этой среде не запускались. Поэтому компиляция/поведение Unity пока не объявляются проверенными.

## Следующий маленький шаг

Проверить фактическую форму fastJSON на серверной стороне (совместно с отчётом Claude), затем убрать временный JObject adapter: выделить небольшой immutable GameplayState/ViewModel для полей, реально читаемых CoreGameplay. Не переносить весь DTO graph одним большим rewrite.


## Дополнение — точная сверка с сервером и второй проход клиента

Проверен фактический `1-server/server/server.go:fastJSON` и тест `1-server/server/fast_frame_test.go:TestFastFrameShapeAndNoLeak`. Форма fast кадра закреплена серверным тестом:
- envelope: `v, fast, alive, me, place`;
- `me`: `combat, energy, feed, health, mana, pos, result, wounds`;
- `place`: `foes, neighbors` (только когда игрок в поселении и не на дороге);
- `neighbors[]`: ровно `id, pos`; серверный тест запрещает утечку имён/вещей/кошельков.
Ни `exits`, ни другие поля не являются частью текущего fast wire shape; парсер оставляет их совместимыми как необязательные additive-поля, но сервер их сейчас не присылает. Серверный контракт не менялся.

Дополнительно устранён риск промежуточного адаптера: `JObject.FromObject(frame)` мог материализовать DTO default values для полей, отсутствовавших в старом v1 JSON. Теперь `WorldFrameParser.Parse(json, out document)` возвращает вместе с DTO тот же самый уже проверенный JObject, созданный при единственном разборе. `CoreGameplay.ApplyFrame(frame, document)` использует исходное дерево токенов; wire JSON не разбирается повторно и отсутствующие поля не превращаются в искусственные нули. В editor fixture добавлена проверка отсутствующих `hunger/pos`.

Уточнение ошибки: если JSON синтаксически повреждён, fast-классификатор не угадывает тип кадра по подстроке, а передаёт payload строгому full parser, который отклоняет его обычной ошибкой формата. Если JSON корректен и явно содержит `fast:true`, но нарушает fast contract, он отклоняется fast parser и не интерпретируется как полный snapshot.
