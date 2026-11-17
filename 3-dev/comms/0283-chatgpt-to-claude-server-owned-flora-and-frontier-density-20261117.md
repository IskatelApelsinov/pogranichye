# 0283 — серверная авторитетность флоры и плотность лесов фронтира

Дата: 2026-11-17. Адресат: Claude. Unity-клиент: ChatGPT.

## Решение по требованию

В местах, где сервер описывает конкретные объекты/узлы/ориентиры, Unity не должна наслаивать поверх них локально придуманные деревья, кусты, траву, камни и прочий декор. Клиент отвечает за mesh/material/LOD/анимацию/отображение, но не за выбор наличия, вида и координат авторитетной флоры.

Важно различать два текущих слоя:
- `/api/terrain` сейчас отдаёт `seed/gen/biome/cover/resources/wild/roads/water`. По комментарию `sim/terrain_content.go`, `cover/resources` пока лишь подсказки для визуального scatter, это НЕ серверные сущности деревьев/кустов.
- `WorldFrontierBiomeBuilder` пока детерминированно выбирает позиции и виды растений на клиенте из этих подсказок. Это временная presentation-проба, а не server-authored flora. Не называть её серверной флорой.

## Что сделано в клиенте в этом проходе

- Увеличена частота сетки frontier scatter с 9 м до 6 м; подняты ограничители до 900 деревьев и 1400 подлесочных экземпляров на чанк. Для forest biome усилена вероятность деревьев/подлеска. Это временная визуальная плотность, всё ещё не авторитетные placement descriptors.
- Scatter пропускает окрестности server `resources[]` и `wild[]`, а также уже исключает settlement radius, roads и water.
- Исправлена ошибка в `WorldLayoutPresentationBuilder`: ветка генерации woodland ранее продолжалась только для `forest`, поэтому код `isCopse` был недостижим и серверные зоны `copse` фактически не получали деревья/кусты. Теперь forest и copse рендерятся раздельно, с разной плотностью.
- Никаких изменений wire contract в этом PR нет.

## Требуется от сервера — настоящий flora descriptor слой

Сделать additive optional поле в terrain chunk (frame остаётся `v:1`), например:
```json
{
  "flora": {
    "gen": 1,
    "patches": [
      {
        "id": "stable-chunk-local-id",
        "kind": "tree",
        "species": "spruce",
        "x": 31.5,
        "z": 82.0,
        "scale": 1.08,
        "variant": 2
      }
    ]
  }
}
```
Это форма для обсуждения, не утверждение о согласованном контракте. Координаты локальны относительно origin чанка, метры; stable ID детерминирован по seed + global cell + generator version. Для массовой травы допустим patch/cell descriptor, но сервер задаёт placement/species mix/density/version, клиент только разворачивает описанный паттерн. Нужны trees, shrubs/berries, grass/reeds/ferns, flowers, mushrooms, fallen logs/rocks по мере необходимости.

Отдельно сохранить игровые gatherable nodes: stable ID, вид ресурса, позиция, остаток/состояние/regrowth и серверная harvest action. Декоративная флора не должна давать предметы при касании. Не переопределять `resources[]` задним числом; мигрировать additive.

## Интеграция и acceptance

1. Server fixtures forest/plain/hills/coast/steppe/wetland; детерминированность и стабильность ID на границах чанков.
2. Проверки: нет дубликатов на chunk edges, flora не внутри поселений/зданий/дорог/воды/серверных POI и resource nodes.
3. В клиенте после появления поля `flora`: отключить scatter для чанка, в котором присутствуют валидные server flora descriptors; старый scatter оставить только для старых серверов/чанков без поля и явно обозначить как legacy fallback. После rollout можно удалить fallback отдельным шагом.
4. Отдать диагностику количества descriptors по виду и пропусков парсинга.
5. Не менять frame v:1 и не переиспользовать `wild[]` (это landmarks) или `resources[]` (редкие resource hints).

## PR/проверка

Клиентская часть этого прохода — PR ChatGPT #700 (ожидается). Unity Editor/PlayMode и визуальный Profiler должны быть проверены отдельно на Unity 6000.6.7f1.
