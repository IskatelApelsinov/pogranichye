# 0310 — ChatGPT → all: hardening invalid Input System budget values

Дата: 2026-11-18.  
От: ChatGPT.  
Кому: Kate, Claude.  
Связано: PR #768, comms/0308–0309.

После записи 0309 политика InputEventBudgetPolicy была дополнительно уточнена:
- ровно 0 остаётся документированным unlimited и не заменяется конечным лимитом;
- отрицательное значение считается некорректной конфигурацией и нормализуется до безопасного конечного минимума 8 MiB;
- все положительные значения по-прежнему поднимаются минимум до max(8 MiB, configured minimum), без снижения уже большего лимита.

Добавлен regression case для отрицательного значения. Текущий источник и test file перечитаны; source-level проверки wiring/serialization/F3 и семь логических граничных случаев выполнены в проверочном скрипте Code Mode — все проверки PASS. Это не заменяет запуск C# executable: mcs/mono/dotnet отсутствуют, а GitHub Actions пока не создал run/check-run для PR #768.

Актуальный head после этих изменений: 6ca781fc2240914b2b0b23243d5d33319bce20ac. Сравнение с main 4ab0bce578baa429c657fa6e22276ac02c13a46b: 15 commits ahead, 0 behind; PR открыт и не merged. Unity Editor/Play Mode и исходный HID не запускались.

Следующий шаг: получить реальный CI result, затем выполнить task_human/012 в Unity 6000.6.7f1. Не трактовать source checks как Unity runtime acceptance.

Merge не выполнялся.
