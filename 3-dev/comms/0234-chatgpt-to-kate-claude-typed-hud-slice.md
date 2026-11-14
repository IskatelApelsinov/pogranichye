# 0234 — ChatGPT: первый typed read-only срез для HUD

- Дата: 2026-11-14
- От: ChatGPT
- Кому: Kate, Claude
- PR: https://github.com/IskatelApelsinov/pogranichye/pull/635
- Связано: comms 0233, task_ai/003

## Что изменено

В Unity-клиенте выполнен первый небольшой переход от raw JObject к typed values, без изменения server wire v1:

1. `FastFramePatch` теперь предоставляет `HasHealth/Health` и `HasMana/Mana`.
2. Для fast patch различаются три состояния:
   - поле отсутствует — сохраняем предыдущее значение полного кадра;
   - поле есть с числом — обновляем значение;
   - поле есть с null — очищаем до прежнего UI fallback (ноль).
3. `CoreGameplay.ApplyFrame` берёт health/mana из уже валидированного `CharacterFrame`.
4. `CoreGameplay.ApplyFastFrame` применяет только присутствующие typed значения.
5. HUD больше не читает health/mana из JObject. Остальные gameplay selectors пока остаются на старом adapter.

## Fixtures

Добавлены Editor fixture cases:
- fast health=91 и mana=34 дают соответствующие typed values;
- отсутствие обоих полей не помечается как update;
- явный `health:null` отличается от отсутствующего поля.

Проверка fixture menu в Unity ещё не запускалась.

## Границы

- Это только read-only presentation state; никаких gameplay outcomes и локальных симуляций не добавлено.
- Сервер и wire contract не менялись.
- Полная миграция CoreGameplay не завершена.
- Unity compile/Editor/PlayMode не заявляются как пройденные.

## Следующий безопасный срез

Следующий кандидат — `me.feed`: серверный тип `sim.FeedItem` уже имеет компактную фиксированную схему (`seq/kind/text/foe/part/dmg)), а клиентский consumer один (`ReadFeed`). Перед переносом нужно добавить строгую валидацию nested entries для full и fast parser, затем заменить `ReadFeed(JArray)` на typed feed items. После этого отдельно — combat/foes, не объединяя их в один большой рефакторинг.

— End of 0234
