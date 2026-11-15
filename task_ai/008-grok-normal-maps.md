!В работе
# 008 — Grok: генерация normal maps к пакету textures/grok

**Исполнитель:** Grok → ChatGPT (импорт)  
**Подсистема:** `Assets/Resources/WorldMaterials/Textures/Grok/`, Unity URP materials  
**Приоритет:** поддержка G2 / P1.4 после albedo-пакета (007).

## Цель

Изучить и зафиксировать метод генерации tangent-space normal maps; выдать `*_n.png` к существующим albedo; описать конвенции URP.

## Сделано

- Документ: `3-dev/docs/NORMAL-MAPS-GENERATION.md` (методы A–D, strength, импорт).
- По отчёту Grok сгенерированы 10 normal maps (Sobel от luminance) в artifacts владельца рядом с albedo; бинарные файлы пока не найдены в GitHub checkout.
- Comms: `3-dev/comms/0177-grok-to-chatgpt-normals.md`.
- README textures обновлён ссылкой на нормали.

## Следующие шаги (ChatGPT)

1. Импорт `*_n.png` как Normal map (sRGB Off); importer теперь распознаёт суффикс `_n`.
2. Подключить к URP/Lit вместе с albedo из 007; Normal Scale ~0.8–1.2.
3. Если рельеф слабый/сильный — написать strength-правки, перегенерирую.
4. Нужны ли отдельные height-карты для wood/stone — ответить в comms.

## Критерии

- Документ в репо.
- Нормали доступны в Unity Resources и импортированы как Normal Map (sRGB Off).
- ChatGPT подтвердил импорт или запросил корректировку strength.


## Integration status — 2026-11-15

Runtime loading is wired as an optional fallback. The importer recognizes `_n` suffix. The Unity folder contract and response to Grok are in `3-dev/comms/0247-chatgpt-to-grok-texture-integration.md`. No claim of visual import: actual PNG binaries are still absent from the repository checkout.
