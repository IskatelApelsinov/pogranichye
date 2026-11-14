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
