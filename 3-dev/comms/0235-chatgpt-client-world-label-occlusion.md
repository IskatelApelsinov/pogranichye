# 0235 — ChatGPT: occlusion и читаемость мировых вывесок

Дата: 2026-11-14  
Исполнитель: ChatGPT  
Подсистема: Unity thick client / presentation.  
Связано: `task_ai/005-chatgpt-client-recovery-demo.md`, план восстановления демо P0.3/P1.4.

## Найденная причина

В текущем `WorldLayoutPresentationBuilder` коллизии зданий уже создаются на слое 2 (Unity Ignore Raycast): обычные здания получают footprint BoxCollider, а корчма — отдельные wall-segment colliders вокруг серверного дверного проёма. Стартовые дома также имеют camera-only proxy colliders на слое 2. Однако `SignFaceVisibility` и `BillboardLabel` использовали `Physics.DefaultRaycastLayers`, который исключает слой 2. Поэтому существующие стены не попадали в их occlusion raycasts, и надписи могли просвечивать через здания.

## Изменения в клиентском PR

- Добавлен Unity-independent `WorldLabelVisibilityPolicy`: слой окклюзии 2, предел вывески 24 м, предел world-space nameplate 45 м, проверка facing/distance.
- `SignFaceVisibility` теперь проверяет существующие layer-2 building/camera proxies. `BillboardLabel` включает слой 2 в существующий NonAlloc occlusion path и его saturation fallback.
- Обычные interaction probes не менялись и продолжают исключать layer 2; никаких новых коллайдеров, препятствий или серверных правил не добавлено.
- Увеличен физический размер текста на вывесках стартовой и generated деревни; дальность чтения ограничена.
- Добавлены pure C# regression checks и шаг GitHub Actions.

## Архитектурные границы

- Только presentation/Unity; Go, wire v1, серверная геометрия 300×300 м, POI, навигация, persistence и авторитетность не менялись.
- Используются существующие layer-2 proxies, которые не перехватывают обычный E-interaction raycast.
- Unity Editor 6000.6.7f1 и PlayMode в этой среде не запускались. CI pure C# проверяет общую policy, не физическую сцену.

## Приёмка владельцем

В Unity проверить вывеску корчмы/кузницы/лавки снаружи и изнутри, скрытие NPC nameplates стенами, расстояние чтения и отсутствие блокировки взаимодействия с POI. Если надпись всё ещё просвечивает, приложить скриншот и F3 dump: это позволит отличить другую систему текста от этих двух builder paths.
