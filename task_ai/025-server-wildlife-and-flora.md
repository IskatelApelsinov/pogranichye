# Task 025 — Серверная фауна, флора и диагностика природного спавна

**Владелец сервера:** Claude  
**Unity/client:** ChatGPT  
**Статус:** handoff отправлен в comms/0274; требуется аудит и план реализации.

## Проблема
В лесах игрок не встречает животных. Нужно знать, создаёт ли их сервер, и получать серверные сведения о природных объектах: деревьях, траве, грибах, кустах и цветах.

## Аудит текущего main (2026-11-16)
- `/api/terrain` отдаёт heightfield + biome/cover/resources/wild/roads/water.
- `sim/terrain_content.go`: cover/resources — подсказки для визуального scatter, resources — 0–2 редкие точки wood/berries/stone; не реальные деревья или gatherable plants.
- `WorldFrontierBiomeBuilder`: Unity детерминированно выбирает позиции/виды деревьев и подлеска на основе серверных seed/biome/cover. Это presentation-only.
- `wildsites.go`/`wildlands.go`: урочища и landmarks, не животные.
- Отдельная wildlife entity/simulation/AOI система не обнаружена; horse и hunt/herd activity сами по себе не являются дикой фауной.

## Требуемый результат
1. Серверная wildlife/ecology с детерминированным spawn, population caps, active/dormant simulation и stable IDs.
2. AOI payload для животных с global WPOS, species, heading, act_code; не использовать neighbors[] без ADR.
3. Dev-only spawn diagnostics: expected capacity, candidates, spawned/active/dormant, AOI count, no-spawn reasons.
4. Серверные flora descriptors с species/placement/patch density/version для деревьев, кустарников, трав, папоротников, цветов, грибов.
5. Разделить декоративную флору и реальные gatherable nodes с количеством, regrowth и server-authoritative harvest.
6. Fixture/tests: determinism, stable IDs across chunks, no duplicates, habitat constraints, AOI bounds, population cap, save/load and regrowth.

## Координация
См. `3-dev/comms/0274-chatgpt-to-claude-wildlife-and-flora.md`.
Frame остаётся v:1, новые поля additive/optional или отдельные endpoints. Существующие resources/wild поля не переопределять.

## Unity deliverables (ChatGPT)
После фиксации DTO/API:
- typed DTO/parser;
- chunk/AOI streaming + cache;
- animal interpolation/act_code animation;
- flora species renderer + LOD/instancing;
- gatherable object presentation linked to server IDs.

## Acceptance
В тестовом forest chunk есть воспроизводимая популяция хотя бы 2–3 видов; snapshot отдаёт ближайших животных; diagnostics объясняет нулевую популяцию в пустом чанке; трава/грибы/кусты/деревья приходят как server-authored descriptors; gatherable harvest меняет состояние для всех клиентов. CI зелёный. Unity Editor/PlayMode проверяется отдельно.
