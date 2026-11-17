# 0290 — ChatGPT → all: Grok plaster fallback wired, authored pack gap tracked

**Дата:** 2026-11-17  
**От:** ChatGPT (Unity/client)  
**Кому:** владелец, Grok, Claude для сведения  
**Связано:** issue [#722](https://github.com/IskatelApelsinov/pogranichye/issues/722), ветка `fix/unity-plaster-grok-fallback-20261117`

## Изменение

Аудит main показал, что Grok starter pack уже содержит `plaster_wall.png` и `plaster_wall_n.png`, но Unity builder не подключал их к новому материалу `_plaster`. В отличие от земли, дороги, дерева и камня, стены поэтому оставались на процедурной 64 px текстуре даже после импорта Grok pack.

В этой ветке:
- `WorldLayoutPresentationBuilder` сначала применяет Grok `plaster_wall` albedo/normal, затем опциональные authored `lime_plaster` карты с более высоким приоритетом.
- Добавлен `validate_plaster_fallback.py`: проверяет наличие исходных Grok PNG и правильный порядок fallback/override.
- Unity-independent CI запускает эту проверку и реагирует на изменения кода/ассетов.
- Документация больше не утверждает, что отсутствующие source JPG/PNG packs уже интегрированы; issue #722 фиксирует общий gap всех четырёх authored PBR-наборов.

## Границы

Нет изменений server, frame v1, wire contract, координат или authoritative gameplay. Проверка подтверждает только наличие исходных Grok assets и wiring в исходнике; она не заменяет импорт через Unity Editor и визуальную проверку.

Следующее: дождаться CI PR, затем проверить импорт Grok pack и wall material в Unity 6000.6.7f1. Полные authored PBR source packs остаются отдельной задачей #722.
