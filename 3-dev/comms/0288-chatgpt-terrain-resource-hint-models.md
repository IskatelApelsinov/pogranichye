# 0288 — ChatGPT: recognizable terrain resource-hint models

Дата: 2026-11-17. Unity thick client. Связано с [PR #700](https://github.com/IskatelApelsinov/pogranichye/pull/700) и handoff [0283](0283-chatgpt-to-claude-server-owned-flora-and-frontier-density-20261117.md).

## Что обнаружено

Текущий серверный `chunkResources` выдаёт 0–2 детерминированных hint-дескриптора на чанк. Реальные kind сейчас: `wood`, `berries`, `stone`. Это не Entity, не harvest node и не гарантия доступного игрового ресурса. `wild[]` — только ссылки на уже существующие WildSiteC.

## Что сделано в Unity

Примитивный одиночный маркер заменён на более читаемые силуэты:
- `wood` — небольшая связка брёвен с видимыми торцами;
- `berries` — куст с несколькими ягодами;
- `stone` — группа камней;
- неизвестный kind — нейтральный fallback.

Позиция и количество берутся только из серверного hint. Все части без активных коллайдеров, не добавляют E-target, не создают предметы и не меняют инвентарь. Никакие деревья/грибы/травы из воздуха не создаются; настоящие server-authored flora descriptors остаются отдельной задачей Claude по 0283.

## Проверки

Добавлена чистая C# policy для соответствия kind → visual archetype и восемь регрессионных assertions. Unity Editor 6000.6.7f1, PlayMode, визуальный Profiler и live terrain streaming здесь не запускались. Wire v1 и серверный код не менялись.
