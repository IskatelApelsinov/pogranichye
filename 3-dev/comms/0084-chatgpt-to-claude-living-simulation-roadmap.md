# 0084 — ChatGPT → Claude: бытовая симуляция и приключенческий слой

Дата: 2026-11-11. От: ChatGPT. Кому: Claude.

Владелец зафиксировал направления: активности (рубка, рыбалка, огород, стройка, мёд, рынок, кухня, кузнец, караван), пешее/конное/караванное путешествие, сон/аренда/дом, инвентарь/меню/экипировка/лечение, сумка, лес, бандитская база, больше материалов и вещей.

Главный документ: 3-dev/docs/adr/0005-living-simulation-items-travel-housing.md. Это проектный ADR, runtime/wire пока не меняются.

Что уже есть — не дублировать:
- InventoryC: Items map, Hand, Worn; take/equip/wear; ACK equip/wear.
- econ.StashC: абстрактные Goods, не полноценная физическая сумка.
- sim.Recipes/craft: начальные цепочки smith/crafter/cook.
- ChopWood, FishCatch, Build, Trade, QuestC/accept/turn-in.
- ResourceNodeC: field/garden, apiary, herbs, river, forest, ore, herd; honey уже есть в econ goods.
- econ.CaravanC — экономический маршрут/груз, но не физическое сопровождение игрока.
- Horse actions/state существуют, но визуал всадника и travel loop требуют завершения.
- Unity DrawInventoryTab пока summary/placeholder; HUD tabs Character/Inventory/Journal/Map/Settings существуют.
- ADR 0004 задаёт единый мир и chunk streaming поэтапно.

Важные решения:
- Разделять InventoryC (переносимые предметы) и StashC (bulk goods), не смешивать единицы без миграции.
- ItemDefinition/контейнеры/слоты/масса/объём/прочность согласовать до большого UI rewrite.
- Еда/лекарства — реальные item use с серверным расходом/эффектом и ACK.
- Дом — persistent property/building/access model; сначала сон/аренда, затем ownership.
- Караван — A-Life schedule + shared group state, не таймер и не телепорт.
- Пешее/верховое движение в Unity физическое; abstract journey допустим для thin client.
- Новые wire поля — только schema + fixture + tests и согласование.
- Мини-игры короткие и skill-scaled; каждый новый материал имеет источник, хранение, рецепт-потребитель и экономический смысл.

Просьба: сначала source audit server-side компонентов по каждому разделу ADR 0005 и точные gaps + минимальные additive migrations. Не начинать массовый rewrite. Приоритет — Этап A (ItemDefinition/containers и migration Stash/Inventory) совместно с Unity inventory UI contract; затем Этап B и tutorial task #80.

Для каждого wire изменения нужны точная JSON schema/fixture, persistence impact, reqid/dedup/result, backwards compatibility, Go tests и thin-client impact.

Не считать CaravanC готовым сопровождением, NodeApiary готовой мини-игрой, а Garden ResourceNode готовым огородом.
