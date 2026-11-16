!В работе

# Подключение возможностей сервера v1.22 к Unity-клиенту

Исполнитель: ChatGPT / Unity client. Серверные правила и исходы не менять.

## Реализовано в этой ветке

- Добавлен WildPlaceFrame и PlaceFrame.Sites для place.sites[].
- WorldFrameParser нормализует optional sites: некорректные элементы скрываются по одному и не блокируют полный кадр.
- CoreGameplay показывает ближайшие урочища, отправляет goto(site) для подхода и site(site, action) только для действий из acts[].
- На подключении загружается optional GET /api/catalog; ответ проверяется на v1 shape.
- В Inventory добавлен раскрываемый справочник предметов, экипировки, рецептов, подсказок узлов и именованных схем.
- Weave UI показывает десять серверных узлов, включая heat/chill/veil/eye; preview синхронизирован с приоритетами sim.ReadWeave. Номерные быстрые клавиши 1–6 сохранены.
- WorldTerrainChunkBuilder использует biome/cover для tint и рисует optional chunk-local roads/water ribbon meshes. Heightfield остаётся единственной геометрией земли и источником MeshCollider.
- Обновлены DTO coverage и Unity README.

## Ограничения / следующий этап

- resources[] и wild[] из /api/terrain пока не визуализируются. Глобальные landmarks из /api/world.wildsites продолжают рендериться отдельно; player actions используют place.sites[].
- water[] отрисовка готова, но сервер PR #662 на момент этой ветки открыт и не слит. В текущем main вода не гарантированно приходит.
- Каталог пока справочный: реальные кнопки покупки/экипировки должны использовать существующие server intents и проверенные точки доступа, а не делать локальную выдачу предметов.
- Новые четыре узла доступны мышью; номерные быстрые клавиши 1–6 оставлены совместимыми. Добавить клавиатурные привязки 7–0 отдельным UX изменением.
- Проверить URP transparent water material, порядок/смещение ribbons, узкие разрешения IMGUI, site walk→act, catalog availability и все десять spell nodes в Unity Editor 6000.6.7f1.

## Проверки

- Изменения сделаны в клиентской ветке; серверные файлы не изменялись.
- GitHub PR / Unity-independent CI: заполнить после создания PR.
- Unity Editor compile, PlayMode и live-server acceptance не запускались; не объявлять runtime-ready.
