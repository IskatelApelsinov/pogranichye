# 0235 — ChatGPT: occlusion и читаемость мировых вывесок

Дата: 2026-11-14  
Исполнитель: ChatGPT  
Подсистема: Unity thick client / presentation.  
Связано: `task_ai/005-chatgpt-client-recovery-demo.md`, план восстановления демо P0.3/P1.4.

## Найденная причина

В текущем `WorldLayoutPresentationBuilder` стены generated Layout v2 создавались через `Create(..., keepCollider:false)`. При этом `SignFaceVisibility` и `BillboardLabel` проверяли окклюзию только физическими raycast-ами. Для сгенерированных поселений стеновых коллайдеров не существовало, поэтому надписи могли просвечивать через здания. В стартовой процедурной деревне уже были camera-only proxy colliders на слое 2 (Ignore Raycast), но обычные label raycasts этот слой не включали.

## Изменения в клиентском PR

- Добавлен Unity-independent `WorldLabelVisibilityPolicy`: слой окклюзии 2, предел вывески 24 м, предел world-space nameplate 45 м, проверка facing/distance.
- Generated building wall segments получают BoxCollider на слое 2. Обычные interaction probes продолжают использовать DefaultRaycastLayers и не сталкиваются с этими proxy; label/camera queries включают слой 2 явно.
- `SignFaceVisibility` проверяет только dedicated occluder layer, чтобы NPC/игрок не скрывали вывеску случайным попаданием в луч.
- `BillboardLabel` включает слой 2 в существующий NonAlloc occlusion path и его saturation fallback.
- Увеличен физический размер текста на вывесках стартовой и generated деревни; дальность чтения ограничена.
- Добавлены pure C# regression checks и шаг GitHub Actions.

## Архитектурные границы

- Только presentation/Unity; Go, wire v1, серверная геометрия 300×300 м, POI, навигация, persistence и авторитетность не менялись.
- Слой 2 — только локальные camera/label occlusion proxies; он не объявляет новые игровые препятствия серверу и не блокирует обычный E-interaction raycast.
- Unity Editor 6000.6.7f1 и PlayMode в этой среде не запускались. CI pure C# проверяет только общую policy, не физическую сцену.

## Приёмка владельцем

В Unity проверить вывеску корчмы/кузницы/лавки снаружи и изнутри, скрытие NPC nameplates стенами, расстояние чтения и отсутствие блокировки взаимодействия с POI. Если надпись всё ещё просвечивает, приложить скриншот и F3 dump; это позволит отличить другую систему текста от этих двух builder paths.
