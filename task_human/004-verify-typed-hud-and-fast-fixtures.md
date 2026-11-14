!В работе
# 004 — Проверить typed status, feed, result, combat и foe views в Unity

**Исполнитель:** владелец проекта  
**Когда:** после merge консолидированного клиентского PR #638 в main и обновления локального checkout.  
**Подсистема:** Unity 6 client / protocol validation.

## Цель

Подтвердить в настоящем Unity Editor, что typed status/feed/result/combat/foe изменения компилируются, а full/fast protocol fixtures проходят.

## Шаги

1. Обновить локальный репозиторий до main после merge PR #638.
2. Открыть `2-client/game` в Unity **6000.6.7f1** (URP).
3. Дождаться окончания импорта и проверить Console на compile errors.
4. Выполнить меню `Tools → Pogranichye → Validate shared v1 fixture`.
5. Убедиться, что в Console есть сообщение об успешной проверке full/fast frame parser, typed feed, result, combat и foes; ошибок и исключений нет.
6. Запустить Play Mode с совместимым Go server:
   - войти в персонажа;
   - проверить health, stamina, mana и intoxication indicators, если значения присутствуют;
   - получить несколько fast frames и убедиться, что отсутствующие поля не обнуляют HUD;
   - вызвать подтверждённое сервером событие (например, попадание/получение урона или действие с записью в feed);
   - убедиться, что событие отображается один раз, а повторяющиеся fast frames не дублируют строку/floaters;
   - выполнить одно result-bearing действие и убедиться, что подтверждение/отказ показывается только после ответа сервера с совпадающим reqid; повтор старого результата не должен подтверждать новое действие;
   - проверить, что стойка/оружие/блок обновляются от server frame, а враг отображается по server ID и исчезает после удаления из AOI; клиент не должен локально создавать боевых врагов.
7. Если есть ошибка, сохранить полный stack trace и приложить Unity Console log. Не исправлять wire/server вручную.

## Что прислать ChatGPT

- Версия Unity Editor.
- Результат компиляции (успех/ошибка).
- Результат fixture menu (успех/ошибка).
- Play Mode: status indicators стабильны? Feed events показываются один раз? Result reqid корректно сопоставляется? Combat/foe presentation совпадает с server frame?
- При ошибке — Console log и, если возможно, F3 diagnostic dump.

## Ограничения

- Не менять серверный wire contract.
- Не считать успешный fixture menu доказательством полной готовности демо.
- Не ставить `!Выполнено` до выполнения всех доступных шагов; если Play Mode недоступен — оставить задачу в работе и указать блокировку.

**Следующий владелец:** ChatGPT — source audit and typed migration of `me.journey` / `me.quests`.
