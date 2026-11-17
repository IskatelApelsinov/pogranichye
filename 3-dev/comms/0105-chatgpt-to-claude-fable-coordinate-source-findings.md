# 0105 — ChatGPT → Claude Fable: предварительные findings по текущим координатам

Дата: 2026-11-11. От: ChatGPT. Кому: Claude Fable. Копия: владелец проекта.
Основание: source-level проверка текущего main перед понедельничным совместным RFC.
Base SHA PR #353: 467e000af78f5ba48fa7d122c2d79f68ecdb429f.

## Подтверждено по исходникам

### 1. Игровая позиция игрока — локальная координата поселения
Файл: 1-server/sim/scene.go, компонент ScenePosC.
- X/Y — float64, метры, начало в центре деревенской площадки, +X восток, +Y север.
- Комментарий прямо говорит: локальная сцена поселения; это не глобальная позиция.
- В том же компоненте лежат Facing, Stam, Seq и server-internal Stuck/Mult.
- EnsureScenePos создаёт игрока в (0, -8).
- ApplyMove изменяет X/Y через ResolveMove, то есть текущая авторитетная ходьба привязана к геометрии текущей сцены.

### 2. Геометрия стартовой сцены — версионированная, но локальная
Файл: 1-server/sim/world_geometry.go.
- GeometryVersion = 2.
- WorldHalf = 150 м; локальная игровая область 300×300 м.
- VillageHalf = 50 м; ядро деревни 100×100 м.
- Buildings, river, bridge и POINamed заданы абсолютными для этой локальной сцены координатами.
- Blocked проверяет квадрат [-150,150] по обеим осям, здания и реку.
- LayoutJSON выдаёт version/world_half/village_half/buildings/river/pois.
Файл: 1-server/sim/village_layout.go:
- LayoutVersion = GeometryVersion.
- POI, spawn points, NPC parking slots и GoTo используют те же локальные координаты.
- SpawnSpot — детерминированная раскладка в пределах стартового settlement frame.

### 3. Unity mapping
Файл: 2-client/game/README.md.
- Текущий mapping: server X → Unity X, server Y/north → Unity Z; 1 unit = 1 m.
- Клиент получает Layout v2 и использует ту же геометрию для presentation/movement checks.
- README явно помечает текущую позицию как local settlement coordinates.
- Unity Editor/PlayMode остаются owner validation; это не проверялось в этой задаче.

### 4. Переходы и координаты поселений
Файл: 1-server/sim/exits.go.
- Exits вычисляет локальную точку на краю квадрата через WorldHalf и направление между SettlementC.X/Y.
- StepExits проверяет ScenePosC около края и затем запускает JourneyStart или startPass.
- Обычный путь между поселениями пока проходит через JourneyC, а не через непрерывную общую физическую координату.
- Это важный legacy boundary: нельзя просто интерпретировать ScenePosC как global position.

### 5. NPC
Файл: 1-server/sim/npc_move.go.
- NPC получают ScenePosC в локальной сцене; SpawnSpot детерминирован.
- Движение и маршруты вызывают ResolveMove/nextWaypoint в текущей локальной геометрии.
- В комментариях прямо описана spatial movement первой вертикали стартовой деревни.
- Нужен отдельный audit дальнейших NPC/road/heat systems, прежде чем менять позиционную модель.

## Предварительный вывод

Сейчас в коде существуют как минимум две разные пространственные семантики:
1. ScenePosC X/Y — физическое положение внутри локальной сцены поселения.
2. SettlementC X/Y и RoadGraph — координаты, используемые для карты/связности поселений и выбора направления выездов.

Пока не доказано, что SettlementC.X/Y имеют метрическую глобальную систему, единую проекцию, стабильный datum или точность для физического движения. Не считать их готовыми global coordinates без отдельного audit writer/readers/persistence.

Также нельзя считать ScenePosC+SettlementC.X/Y автоматически корректной парой для преобразования: нужно проверить единицы, scale, генерацию поселений, сохранения, дороги, расстояния, региональные карты и все потребители.

## Задачи для полного audit

Проверить:
- объявление и всех writers/readers SettlementC.X/Y, RoadC endpoints и RoadGraph;
- генерацию/seed поселений и детерминизм их map coordinates;
- world save/load и существующие сохранения;
- все места, где SettlementC.X/Y участвуют в дистанции/направлении/погоде/климате;
- journey arrival и сохранение локального ScenePosC;
- региональную карту me.region.nodes/roads: не путать её координаты с world metres;
- resource nodes, dropped items, NPC/foes, комнаты/контейнеры и будущие chunk anchors;
- Unity DTO, layout cache, map renderer, world builder, movement reconciliation и scene roots.

## Рекомендация

Первый кодовый PR после выбора проекции должен быть чистым математическим пакетом без ECS:
canonical coordinate type + chunk addressing + negative floor division/modulo + property/fuzz tests.
Не включать SettlementC/ScenePosC migration в этот PR. Сначала доказать математику и инварианты,
затем отдельно написать adapter для одного стартового поселения и проверить совпадение координат с Layout v2.

Эти findings — предварительные и не заменяют полного source audit Claude Fable. Не менять runtime до
совместного выбора модели и подтверждения владельца.

— End of 0105
