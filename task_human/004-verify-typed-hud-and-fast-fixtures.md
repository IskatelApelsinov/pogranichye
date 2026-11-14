!В работе
# 004 — Проверить typed HUD и fast-frame fixtures в Unity

**Исполнитель:** владелец проекта  
**Когда:** после merge PR #635 в main и обновления локального checkout.  
**Подсистема:** Unity 6 client / protocol validation.

## Цель

Подтвердить в настоящем Unity Editor, что первый ограниченный переход HUD health/mana на typed full/fast state компилируется и что parser fixtures проходят.

## Шаги

1. Обновить локальный репозиторий до main после merge PR #635.
2. Открыть `2-client/game` в Unity **6000.6.7f1** (URP).
3. Дождаться окончания импорта и проверить Console на compile errors.
4. Выполнить меню `Tools → Pogranichye → Validate shared v1 fixture`.
5. Убедиться, что в Console есть сообщение об успешной проверке full/fast frame parser; ошибок и исключений нет.
6. Запустить Play Mode с совместимым Go server:
   - войти в персонажа;
   - проверить health bar;
   - при наличии маны проверить mana bar;
   - получить несколько fast frames и убедиться, что HUD не мигает в ноль между обновлениями.
7. Если есть ошибка, сохранить полный stack trace и приложить Unity Console log. Не исправлять wire/server вручную.

## Что прислать ChatGPT

- Версия Unity Editor.
- Результат компиляции (успех/ошибка).
- Результат fixture menu (успех/ошибка).
- Play Mode: health/mana отображаются стабильно или нет.
- При ошибке — Console log и, если возможно, F3 diagnostic dump.

## Ограничения

- Не менять серверный wire contract.
- Не считать успешный fixture menu доказательством полной готовности демо.
- Не ставить `!Выполнено` до выполнения всех доступных шагов; если Play Mode недоступен — оставить задачу в работе и указать блокировку.

**Следующий владелец:** ChatGPT — разбор ошибок и следующий typed consumer slice.
