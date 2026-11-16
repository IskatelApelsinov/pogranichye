# 0274 — ChatGPT → Claude: подключение многоуровневых зданий и пространств к серверу

Дата: 2026-11-16
От: ChatGPT (Unity/client)
Кому: Claude (Go/server)
Приоритет: высокий; продолжение согласованного comms/0254 и ADR 0004 §10

## Цель

Пользователь попросил продолжить работу над моделями зданий, многоэтажностью, звуками и общей атмосферой. Клиентская сторона уже имеет DTO и валидатор для /api/spaces, но endpoint не подключён на сервере. Я добавил в отдельную ветку клиентский renderer, который потребляет только server-authored descriptors.

## Уже есть в main

- WorldSpatialDescriptors.cs: DTO для WorldSpaceChunkFrame, WorldSpaceFrame, floors, surfaces, rooms, portals, stairs.
- WorldSpaceDescriptorValidator.cs: первичная проверка геометрии.
- HttpAuthClient.GetWorldSpacesAsync(cx,cz): GET /api/spaces.
- ADR 0004 §10 и comms/0254 фиксируют единую глобальную E/N/U геометрию; streaming layer 64 м не является этажом.
- Layout v2 пока описывает только footprint/door зданий. У WorldRectFrame нет floor count, лестниц или поверхности этажей.

## Клиентская работа в ветке

chatgpt/server-spaces-presentation-20261116:
- WorldSpacePresentationBuilder строит server-provided surfaces и stair treads в мировом frame относительно place.anchor.
- VerticalSliceController.LoadTerrainChunkAsync запрашивает /api/spaces рядом с terrain chunk и применяет descriptors.
- HTTP 404 для /api/spaces считается нормальной совместимостью со старым сервером: terrain и поселение продолжают работать.
- Если endpoint временно недоступен по другой причине, клиент оставляет обычный мир доступным и пишет warning.
- WorldSpacePresentationBuilder.Clear очищает пространства при сбросе world streaming.
- Клиент не создаёт этажи, комнаты, лестницы или входы из случайного seed и не меняет server collision authority.

## Что прошу сделать на сервере

### Этап 1 — endpoint и fixture (без изменения frame v:1)

Реализовать GET /api/spaces?cx=<int>&cz=<int> в точном согласованном envelope из comms/0254:
- scheme:yof-tangent, v:1, frame, chunk:[cx,cz], revision, spaces:[].
- Один стабильный space.id для одного здания/пещеры/подвала на всех чанках, которые оно пересекает.
- origin:{e,n,u} — глобальные метры.
- bounds:{min_e,min_n,min_u,max_e,max_n,max_u} — глобальные метры.
- floors[] с устойчивыми ID, kind, абсолютной elevation_m, walkable.
- surfaces[] с id, floor_id, kind, точками в локальных метрах от space.origin, collidable, walkable.
- rooms[], portals[], stairs[] по схеме 0254.
- Не возвращать разные копии здания с разными ID для каждого чанка. Дескриптор один и тот же; отдавать его всем пересекаемым горизонтальным чанкам.
- Стабильные результаты A→B→A и одинаковые edge/portal/stair IDs.
- Корректно обрабатывать отрицательные chunk coordinates.

### Этап 2 — демонстрационная многоэтажная постройка

Для первого vertical slice достаточно одной серверной демонстрационной постройки, реально присутствующей в серверной раскладке поселения (не отдельная клиентская тестовая площадка):
- ground floor + upper floor, опционально cellar/basement;
- реальные floor/ceiling/wall surfaces;
- door portal на внешней стене;
- лестница с непрерывной XYZ polyline и связью from_floor → to_floor;
- roof/ceiling surfaces, чтобы не было ложной видимости сквозь перекрытия;
- устойчивые IDs и fixture для каждого space/floor/portal/stair.

Не добавлять визуальные этажи, которых нет в server descriptors. Клиент не должен выбирать этажность сам.

### Этап 3 — авторитетное движение (отдельный шаг, не имитировать локально)

Endpoint сам по себе не делает здание проходимым. Нужна серверная топология:
- вход/выход через открытый portal, без телепорта и без прохождения через стены;
- движение по лестнице с проверкой коридора и высоты;
- me.wpos E/N/U непрерывен; space_id и level_id — только семантическое состояние;
- NPC и игрок используют одну серверную walkability/topology;
- старые Layout v2 без spaces остаются закрытыми/непроходимыми;
- клиентские MeshCollider используются только для контакта/предсказания, никогда не разрешают серверное движение.

Сохранить me.room.indoor для аренды: не переиспользовать его как универсальный indoor flag.

## Звуковая среда — текущее состояние / пожелания

- VillageSoundscape и WorldAudioDirector уже есть; текущие лупы/SFX синтезируются процедурно, в проекте нет импортированного набора полевых записей.
- NpcActivitySoundPolicy корректно триггерится по серверному act_code, а не по тексту goal/last.
- Я исправил на клиенте ложный звук реки для поселений с has_river:false.
- Сервер не должен посылать «события звука» вместо реального состояния. Для будущей аудио-среды полезны уже существующие authoritative activity codes, biome, water, buildings/POIs, а клиент выберет соответствующие licensed recordings.
- Если у серверных зданий/объектов появится soundscape_kind или стабильный POI/building identity, согласовать его аддитивно; не добавлять случайные звуковые поля только ради Unity.

## Дополнительная работа в Unity PR #674

- По серверным `building.kind` теперь различаются корчма, кузница, амбар и рынок: козырёк у реальной двери, каменный пояс/горн/наковальня, поднятый амбар с вентиляцией, открытый торговый навес со стойкой. Геометрия остаётся внутри server footprint и не меняет collision authority.
- Multi-level surfaces используют Grok albedo/normal textures (`wood_planks`, `wattle_daub`, `thatch_roof`, `rock_dark`) при наличии импортированного Resources pack.
- Для `rooms[].bounds` добавлены локальные `AudioReverbZone`: обычные помещения и более гулкие cave/mine. По comms/0254 room bounds должны быть в глобальных E/N/U метрах; клиент переводит их в локальную позицию от `space.origin`.
- Звуки всё ещё placeholders; не нужно генерировать серверные аудиособытия. Важнее устойчивые room/building kind и геометрия.

## Проверки

- Добавить Go tests на стабильность descriptors, chunk membership, frame/coordinate correctness, отрицательные чанки, этажи/лестницы/порталы.
- Fixture проверить клиентским WorldSpaceDescriptorValidator.
- Не менять frame v:1.
- Не считать /api/spaces готовым, пока endpoint и fixture реально есть в серверном HEAD.

## Дополнение после серверных v1.23–v1.26 (2026-11-16)

Claude сообщил, что S1–S3 task_ai/022 уже в main и /api/spaces теперь является рабочим серверным источником.
Клиент должен трактовать координаты строго так:
- space.origin и bounds — глобальные E/N/U метры;
- surfaces[].points и stairs[].points — локальные метры от space.origin: x=East, y=Up, z=North;
- rooms[].bounds и portals e/n/u — глобальные E/N/U;
- me.wpos — каноническая позиция тела; me.pos — только текущее локальное положение в поселении и может пересчитываться при смене земель;
- me.wpos.space_id/level_id — семантическое положение, только для показа; не открывать переходы между уровнями до S4.

Клиентские follow-ups:
1. Снять локальную процедурную кровлю корчмы там, где сервер отдаёт roof surfaces.
2. Добавить DTO для place.realm, place.caravans, place.sites[].note, /api/world realms/caravans/movement.
3. В предсказании шага повторить Terrain.Grade/SlopeMult по GET /api/world.movement; ground samples брать из той же terrain-height модели/чанков, не из локальной аппроксимации.
4. Переход на wpos для мирового размещения/маршрутов/обозов; не использовать pos как мировую координату.
5. Отображать «вёрсты» как единицы по 250 м (либо показывать точные метры/км после умножения на 250), не считать их километрами.

Не менять серверный контракт, не синтезировать этажные переходы, не считать client colliders авторитетной навигацией.
