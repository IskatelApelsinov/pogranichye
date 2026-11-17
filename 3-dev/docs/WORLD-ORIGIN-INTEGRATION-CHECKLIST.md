# Floating Origin — runtime integration audit

Дата: 2026-11-16  
Связанный PR: #656  
Статус: подготовительный аудит; runtime shift не включён.

## Почему нельзя просто передвинуть один Transform

Текущий клиент уже использует несколько разных локальных систем координат. Простое перемещение `_presentationRoot` или камеры нарушит связь между игроком, layout, terrain, NPC, физикой и звуком. Floating Origin должен стать общей координатной трансформацией, а не отдельным скриптом с периодическим `transform.position -= offset`.

## Найденные владельцы представления

| Подсистема | Текущая система | Что потребуется при rebasing |
|---|---|---|
| `VerticalSliceController` | Создаёт `PresentationWorld` под своим Transform | Владеть состоянием origin, очередью сдвига и атомарной фазой применения |
| `EntityViewRegistry` | Игрок из `me.pos` (settlement-local); соседи из global `wpos`, спроецированного относительно игрока | Перевести локального игрока на единый offset; сохранить сглаживание и first-person camera target |
| `WorldTerrainChunkBuilder` | Вершины чанка вычисляются относительно `Place.Anchor`; на каждый чанк есть MeshCollider | Создавать/обновлять mesh в общем local-origin frame; после сдвига синхронно обновлять коллайдеры |
| `WorldLayoutPresentationBuilder` и `SettlementLayoutCache` | Геометрия поселения и POI в локальных координатах layout | Ввести единый local-to-origin adapter; кэшировать геометрию в глобальных double или устойчивых settlement-local координатах |
| `WorldEnvironmentBuilder` | Небо, облака, дальний ландшафт, трава, лес, детали и атмосфера под presentation root | Разделить camera-follow объекты, локальную декорацию и глобальные world cells; не перемещать sky dome как наземную геометрию |
| `WorldWildSitePresentationBuilder` | Дальние объекты из manifest | Проецировать из global E/N/U через общий origin |
| `WorldAudioDirector` / `VillageSoundscape` | Источники звука привязаны к presentation root и локальному игроку | Сдвигать anchors вместе с миром, не сбрасывая playback, doppler и voice range |
| `PresentationCameraRig` / `AudioListener` | Камера может быть сценовой и не быть дочерней controller | Зарегистрировать её как отдельный root и применить тот же shift |
| Свет/частицы/физика | Частично дочерние controller, частично создаваемые в presentation root | Сдвигать Rigidbody/Transform в одной фазе; пересинхронизировать physics и particle simulation |

## Контракт координат

- `wpos.e/n/u` — глобальная серверная система координат в метрах.
- `pos.x/y` — settlement-local координаты для текущей локальной игровой сцены и существующего movement intent.
- Floating Origin меняет только представление `wpos` в Unity local space; он не меняет серверные координаты и payload movement.
- Нельзя смешивать `Place.Anchor`, `me.pos` и текущий floating origin без явного adapter.
- Текущая проекция NPC через разность global doubles относительно игрока уже избегает потери точности для соседей; её нужно сохранить или обобщить через единый origin service.

## Безопасная последовательность runtime внедрения

1. Ввести `WorldOriginState` как источник истины для локального origin и отдельный `WorldOriginCoordinator`, который только планирует shifts.
2. Добавить в координатный adapter преобразование: global E/N/U → local float, а также settlement-local → global/local через подтверждённый anchor.
3. Сначала перевести terrain mesh и его collider на общий origin, не меняя gameplay movement.
4. Перевести EntityViewRegistry и world layout; проверить, что player/NPC/POI не прыгают на границе settlement.
5. Зарегистрировать все активные roots (включая внешнюю Camera) и только после этого включить atomic shift.
6. В одной фазе: заморозить временные обновления, сдвинуть transforms/rigidbodies, обновить origin state, выполнить `Physics.SyncTransforms`, вернуть обновления. Не делать shift из callback сетевого потока.
7. Затем подключить звук, частицы, sky-follow и world map projection.
8. Включать rebasing сначала в Editor/dev build с диагностическим флагом и возможностью выключить.

## Обязательные acceptance tests

- Один и тот же global point имеет одинаковое local отображение до и после rebasing с учётом применённого delta.
- Игрок, NPC, POI, terrain mesh/collider и камера не меняют взаимное положение после shift.
- Сдвиг не меняет `wpos`, `pos`, movement sequence, reqid, chunk ID или server persistence.
- Смена поселения до/после shift не создаёт двойной offset.
- Быстрый бег, лошадь, teleport, reconnect, смерть/respawn и переход через отрицательные E/N координаты.
- После shift нет stale physics contacts, потери AudioSource playback, выброса particle systems или двойных объектов.
- Повторный shift и shift на нулевой delta безопасны.
- Сбор dump показывает старый/новый origin, delta, количество перемещённых roots, physics sync и время применения.

## Что разрешено в PR #656

PR #656 добавляет только double-precision state, обратимые преобразования, квантование origin и тесты; он не включает runtime transform shifts. Это намеренное ограничение до завершения полного inventory и coordinate adapter.
