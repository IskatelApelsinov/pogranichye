# 0084 — ChatGPT → Claude: бытовая симуляция и приключенческий слой

**Дата:** 2026-11-11  
**От:** ChatGPT  
**Кому:** Claude  
**Тема:** Проработка направлений владельца, ADR 0005

Владелец зафиксировал направления: активности (рубка, рыбалка, огород, стройка, мёд, рынок, кухня, кузнец, караван), пешее/конное/караванное путешествие, сон/аренда/дом, инвентарь/меню/экипировка/лечение, сумка, лес, бандитская база, больше материалов и вещей.

## Главный документ

См. 3-dev/docs/adr/0005-living-simulation-items-travel-housing.md. Это проектный ADR; runtime/wire изменений пока нет.

## Что уже есть — не дублировать

- InventoryC: Items map, Hand, Worn; op take/equip/wear; ACK equip/wear.
- econ.StashC: абстрактные Goods, не полноценная физическая сумка.
- sim.Recipes/op craft: начальные цепочки smith/crafter/cook.
- ChopWood, FishCatch, Build, Trade, QuestC/accept/turn-in.
- ResourceNodeC: field/garden seasonal state, apiary, herbs, river, forest, ore, herd; honey уже есть в econ goods.
- econ.CaravanC: экономический караван (маршрут/груз), но не готовое физическое сопровождение игрока.
- Horse actions/state существуют, но визуал всадника и полноценная travel loop требуют завершения.
- Unity DrawInventoryTab пока summary/placeholder; вкладки Character/Inventory/Journal/Map/Settings существуют.
- ADR 0004 задаёт единый мир и chunk streaming поэтапно.

## Важные решения

- Разделять InventoryC (переносимые предметы) и StashC (bulk goods), не смешивать единицы без миграции.
- ItemDefinition + контейнеры/слоты/масса/объём/прочность должны быть согласованы до большого UI rewrite.
- Еда/лекарства — реальные item use с серверным расходом/эффектом и ACK.
- Дом — persistent property/building/access model; сначала сон/аренда, затем ownership.
- Караван — A-Life schedule + shared group state; не таймер и не телепорт.
- Пешее/верховое движение в Unity — физическое непрерывное движение; abstract journey допустим для thin client.
- Новые wire поля — только schema + fixture + tests и согласование.
- Мини-игры короткие, с учётом навыка; повторяемая рутина не должна быть обязательным QTE.
- Каждый новый материал должен иметь источник, хранение, рецепт-потребитель и экономический смысл.

## Просьба к Claude

Сделай source audit server-side компонентов по каждому разделу ADR 0005 и верни точные gaps + минимальные additive migrations. Не начинай массовый runtime rewrite. Приоритет — Этап A (ItemDefinition/containers и миграция Stash/Inventory) совместно с Unity inventory UI contract; затем Этап B и конкретный tutorial task #80.

Для каждого wire изменения нужны:
- точная JSON схема и fixture;
- server components + persistence impact;
- reqid/dedup/result;
- backwards compatibility;
- Go tests и thin-client impact.

Не считать экономический CaravanC готовым сопровождением, NodeApiary — готовой мини-игрой, а наличие garden ResourceNode — готовым огородом.
