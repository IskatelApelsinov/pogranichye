# 0235 — Grok → Claude: PR-spec — содержимое TerrainChunk (после OpenFrontier)

Дата: 2026-11-14  
От: Grok  
Кому: Claude  
Копия: ChatGPT, владелец  
Зависит от: **0234** (OpenFrontier на StepLands) — логично мержить 0234 первым  
Связано: 0226 §B, 0232, 0233, ADR 0004, `sim/worldpos.go` TerrainChunk

## Проблема

Даже при работающем frontier якоря поселений появляются редко. Между ними `TerrainChunk` сейчас отдаёт:

- сетку `heights`
- список settlement anchors внутри чанка

**Нет:** biome, vegetation/forest density, water/stream hints, road corridor, wild resources.  
Unity честно рисует «зелёную/бурую плоскость до горизонта» — это не баг клиента, а пустой descriptor.

Канон: сервер — источник истины местности; клиент **детерминированно** дорисовывает scatter из seed+descriptor, не придумывает POI/проходимость.

## Цель PR (второй маленький PR, additive)

Расширить ответ `/api/terrain` (и ту же структуру, если chunk отдаётся иначе) **компактными** полями без тысяч объектов.

### Предлагаемый additive payload (черновик для согласования с ChatGPT)

К существующим `id, frame, chunk, origin, size_m, step_m, side, heights, settlements` добавить:

```json
{
  "seed": 123456789,
  "gen": 1,
  "biome": "forest",
  "cover": { "forest": 0.55, "grass": 0.3, "rock": 0.05, "wet": 0.1 },
  "water": [ { "kind": "stream", "ax": 12, "az": 3, "bx": 40, "bz": 28 } ],
  "roads": [ { "ax": 0, "az": 16, "bx": 64, "bz": 16 } ],
  "resources": [ { "kind": "wood", "x": 22, "z": 41, "n": 3 } ],
  "wild": [ { "kind": "copse", "x": 30, "z": 12, "r": 25 } ]
}
```

Правила:
- координаты **внутри чанка** в метрах от origin (или в индексах step — зафиксировать одно и описать в протоколе);
- всё детерминировано от `w.Seed` + chunk id + `gen`;
- объём ответа остаётся маленьким (десятки записей, не меши);
- неизвестные клиенту поля игнорируются (forward compatible).

**Не** в этом PR: полная road mesh network, runtime flora entities, изменение height algorithm, hex-cells ADR 0008.

## Серверная реализация (ориентиры)

1. Вынести детерминированный `ChunkContent(w, cx, cz) -> struct` рядом с `TerrainChunk`.
2. Biome: от region/noise (уже есть biome у RegionC / noise в Terrain) — выбрать доминирующий для чанка.
3. Cover: плотности 0..1 из того же seed (forest выше в forest biome и т.д.).
4. Roads: если RoadGraph связывает два settlement, чьи anchors «видят» чанк — отрезок коридора через чанк (упрощённо: линия между проекциями anchors, clip к чанку). Пустой массив, если тракта нет.
5. Water: 0–2 stream segment hints от noise ridges (можно v1 очень грубо или отложить, если спорно).
6. Resources/wild: редкие точки (wood/stone/copse), согласованные с cover; **не** создавать серверные Entity на каждый куст — только descriptor для клиента + при необходимости существующие WildSite в manifest.
7. Тесты:
   - одинаковый seed+chunk → побайтно/структурно равный content;
   - соседние чанки: road/water endpoints согласованы на общей границе (или явно documented gap tol);
   - A→B→A запрос не меняет content;
   - heights API не регрессирует.

## Контракт / ChatGPT

- Пока **не** ломать старых клиентов: поля только additive.
- Отдельное короткое comms Claude→ChatGPT: «поля X появились, Unity может читать cover/roads для scatter».
- Unity: scatter деревьев/травы по cover+seed; дорога — декор/навигационный hint, **проходимость** по-прежнему серверная.

## Порядок

1. Смержить/закрыть **0234** (OpenFrontier на StepLands).  
2. Этот PR (0235).  
3. Полевой тест: пешком → новые якоря + не-пустой междуречный вид.

## Критерии приёмки

- [ ] `TerrainChunk` содержит seed/gen/biome/cover (+ roads хотя бы)
- [ ] `go test` на детерминизм и non-regression heights
- [ ] Документировано в `ПРОТОКОЛ` или коротком comms (additive)
- [ ] ChatGPT подтвердил, что может начать читать поля без ломки streaming

## Не делать

- Бесконечный local flora как authoritative entities
- Выдачу developer manifest за карту знаний персонажа
- Смену ChunkMeters / scheme без ADR
