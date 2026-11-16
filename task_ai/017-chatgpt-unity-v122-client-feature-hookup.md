!В работе

# Подключение возможностей сервера v1.22 к Unity-клиенту

Исполнитель: ChatGPT / Unity client. Серверные правила и исходы не менять.

## Реализовано в этой ветке

- Добавлен WildPlaceFrame и PlaceFrame.Sites для place.sites[].
- WorldFrameParser нормализует optional sites: некорректные элементы скрываются по одному и не блокируют полный кадр.
- CoreGameplay показывает ближайшие урочища, отправляет goto(site) для подхода и site(site, action) только для действий из acts[].
- На подключении загружается optional GET /api/catalog; ответ проверяется на v1 shape.
- В Inventory добавлен раскрываемый справочник предметов, экипировки, рецептов, подсказок узлов и именованных схем. Каталог также управляет списком покупок по `at`, экипировкой по `hand/slot` и полным набором `usables`; запросы идут через существующие server intents и result gate.
- Weave UI показывает десять серверных узлов, включая heat/chill/veil/eye; preview синхронизирован с приоритетами sim.ReadWeave. Для всех десяти узлов добавлены быстрые клавиши 1–9 и 0.
- WorldTerrainChunkBuilder использует biome/cover для tint и рисует optional chunk-local roads/water ribbon meshes. Heightfield остаётся единственной геометрией земли и источником MeshCollider.
- Обновлены DTO coverage и Unity README.

## Ограничения / следующий этап

- resources[] из /api/terrain отображаются лёгкими маркерами без коллайдеров. wild[] не дублируется: те же persistent landmarks уже строятся из /api/world.wildsites; player actions используют place.sites[].
- water[] отрисовка готова, но сервер PR #662 на момент этой ветки открыт и не слит. В текущем main вода не гарантированно приходит.
- Каталог не меняет состояние сам по себе. Покупка, экипировка и употребление уже подключены через существующие server intents; дальнейшая проверка нужна на реальном сервере, особенно для всех новых item IDs.
- Все десять узлов доступны мышью и клавишами 1–9, 0; клавиатурные привязки добавлены без изменения первых шести.
- Проверить URP transparent water material, порядок/смещение ribbons, узкие разрешения IMGUI, site walk→act, catalog availability и все десять spell nodes в Unity Editor 6000.6.7f1.

## Проверки

- Изменения сделаны в клиентской ветке; серверные файлы не изменялись.
- PR #663 открыт: https://github.com/IskatelApelsinov/pogranichye/pull/663 (ветка `feature/unity-v122-sites-weave-client`).\n- Unity-independent C# workflow #472 завершился success на промежуточном коммите 4c9a0f6ac7c6f03e6489e3bcbadbb117b7066796. Workflow не компилирует все затронутые Unity MonoBehaviours и не покрывает последующие изменения ветки; это не подтверждение полной сборки.
- Unity Editor compile, PlayMode и live-server acceptance не запускались; не объявлять runtime-ready.
