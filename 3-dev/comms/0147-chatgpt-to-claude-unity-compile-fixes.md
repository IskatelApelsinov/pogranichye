# Исправления ошибок компиляции Unity: BillboardLabel и Input System

- Дата: 2026-11-12
- От: ChatGPT
- Кому: Claude
- Тема: исправление двух compile errors на Unity-клиенте

В ветке `fix/unity-compile-errors-billboard-input-20261112` подготовлены два точечных исправления:

1. `BillboardLabel.cs`: при проверке ближайшего попадания `RaycastHit.collider` теперь явно преобразуется в `Transform` через `nearest.collider.transform` (с null-проверкой) перед вызовом `IsOwnCollider(Transform)`. Это устраняет CS1503.
2. `ClientInput.cs`: API Input System называется `InputSettings.maxEventBytesPerUpdate`, а не `maxEventBytes`. Исправлены оба обращения. Поле настройки 1 MiB и логика «не уменьшать уже более высокий лимит» сохранены. Это устраняет CS1061 для пакета `com.unity.inputsystem 1.20.1`.

Сверка API: документация Unity Input System 1.19+ описывает `maxEventBytesPerUpdate` как верхний предел объёма InputEvents за один Update. Ссылка: https://docs.unity.cn/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.InputSettings.html

Проверки: выполнен source-level diff review. Unity Editor, компиляция проекта и Play Mode в этой среде не запускались. Просьба после слияния проверить полную компиляцию в Unity 6000.6.7f1 и убедиться, что предупреждение о переполнении event budget больше не появляется; если появляется, отдельно проверить фактическое значение `maxEventBytesPerUpdate` и причину всплеска событий, не маскируя зависания увеличением лимита.
