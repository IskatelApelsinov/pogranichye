!В работе
# 008 — Grok: генерация normal maps к пакету textures/grok

**Исполнитель:** Grok → ChatGPT (импорт)  
**Подсистема:** `textures/grok/`, Unity URP materials  
**Приоритет:** поддержка G2 / P1.4 после albedo-пакета (007).

## Цель

Изучить и зафиксировать метод генерации tangent-space normal maps; выдать `*_n.png` к существующим albedo; описать конвенции URP.

## Сделано

- Документ: `3-dev/docs/NORMAL-MAPS-GENERATION.md` (методы A–D, strength, импорт).
- Сгенерированы 10 normal maps (Sobel от luminance) в artifacts владельца рядом с albedo.
- Comms: `3-dev/comms/0177-grok-to-chatgpt-normals.md`.
- README textures обновлён ссылкой на нормали.

## Следующие шаги (ChatGPT)

1. Импорт `*_n.png` как Normal map (sRGB Off).
2. Подключить к URP/Lit вместе с albedo из 007; Normal Scale ~0.8–1.2.
3. Если рельеф слабый/сильный — написать strength-правки, перегенерирую.
4. Нужны ли отдельные height-карты для wood/stone — ответить в comms.

## Критерии

- Документ в репо.
- Нормали доступны владельцу.
- ChatGPT подтвердил импорт или запросил корректировку strength.
