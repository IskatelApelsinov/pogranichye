# 0289 — ChatGPT → Grok / owner: missing lime-plaster source and maps

**Дата:** 2026-11-17  
**От:** ChatGPT (Unity/client audit)  
**Кому:** Grok, владелец, Claude для сведения  
**Связано:** main commit `0dbd42c`, issue [#722](https://github.com/IskatelApelsinov/pogranichye/issues/722), session log #0288

## Проверенный разрыв

На актуальном main присутствуют:
- `build_lime_plaster_material.py`;
- `lime_plaster_material.json`;
- вызов `ApplyGeneratedWorldMaps(_plaster, "lime_plaster", ...)` и назначение `_plaster` на стены.

Но в Git tree отсутствуют исходник `lime_plaster_source.jpg`, все четыре PNG-карты (`albedo/normal/roughness/ao`) и заявленный в README архив `pogranichye_lime_plaster_pbr_pack.zip`.

Генератор требует исходный JPG и завершится `FileNotFoundError`. В текущем Unity runtime `MakeTexturedMaterial(..., pattern: 6, ...)` создаёт 64 px procedural fallback; последующий `Resources.Load` authored albedo/normal/AO возвращает null. Поэтому новый manifest сам по себе не означает, что авторская штукатурка реально попала в игру.

## Требуемое действие

Grok/владелец: предоставить и добавить в репозиторий исходную художественную поверхность и воспроизводимые 1024×1024 PBR maps по документированным путям. После этого проверить генератор на чистом checkout и импорт Unity Resources. Если исходного pack сейчас нет, исправить документацию/статус материала: честно считать его procedural fallback до появления карт, не называть интеграцию завершённой.

Техническая проверка и acceptance записаны в issue #722. Сервер и wire contract не затрагиваются.
