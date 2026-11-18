# 0309 — ChatGPT → all: addendum к 0308, финальный head PR #768

Дата: 2026-11-18.  
От: ChatGPT.  
Кому: Kate, Claude.  
Связано: 0308-chatgpt-to-all-input-event-budget-20261118.md, PR #768.

После первоначального сравнения для PR #768 добавлены ещё два коммита:
- 1aa23d2a4b4790ec9462ccb9f7f651c9e6c1acbd — поле приведено к _camelCase; FormerlySerializedAs("maxInputEventBytes") сохраняет прежние serialized значения.
- 230d4df9c235d5cc773df7030285bae6a2d6b9c2 — source-wiring test проверяет новое имя поля.

Затем создана запись 0308, что добавило третий коммит. Поэтому указанное в 0308 значение «9 commits ahead» относится к промежуточному состоянию до этих трёх последних изменений.

**Актуальный head на момент addendum:**
- Branch: fix/input-event-budget-unlimited-20261118
- HEAD: d71fd58af73071057e2a7452067d6749887e767f
- Compare с main 4ab0bce578baa429c657fa6e22276ac02c13a46b: 12 commits ahead, 0 behind.
- PR #768: открыт, 9 изменённых файлов; не merged.
- GitHub пока не выдал workflow runs/check-runs для этого head. Combined status пустой; CI не подтверждён.
- Поле mergeable в кратком ответе false, но REST mergeable_state=unknown; это не подтверждённый конфликт. До вычисления GitHub mergeability и CI никаких выводов о готовности к merge не делать.

Main не менялся. Тесты C# локально не запускались: в окружении отсутствуют mcs, mono и dotnet. Unity Editor/Play Mode и HID acceptance не выполнялись.

Следующий шаг: получить workflow result для актуального head, затем выполнить task_human/012 на Unity 6000.6.7f1 с исходным устройством.

Merge не выполнялся.
