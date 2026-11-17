# 0290 — ChatGPT → all: authored material availability guard

Дата: 2026-11-17. Связано: issue #722 и клиентский PBR pipeline.

## Аудит

На main есть четыре builder script и описания целевых PBR-наборов, но нет source JPG и готовых 1024×1024 albedo/normal/roughness/AO maps. Два JSON manifest существовали без статуса доступности; два других per-material manifest отсутствовали.

## Изменения PR

- Все четыре per-material manifest явно помечены availability: pending в чистом checkout.
- Успешный authored builder записывает availability: available только после собственных seam/normal checks и генерации четырёх карт.
- Runtime выбирает complete authored 1024px pack → imported Grok fallback → procedural fallback. 512px master-generator preview не выдаётся за authored pack.
- lime_plaster получает Grok plaster_wall fallback; oak_planks, packed_road и mossy_stone также используют соответствующие Grok fallbacks.
- Blender master generator сохраняет authored pack только при available manifest и четырёх PNG 1024×1024.
- Python validator и unit tests проверяют pending/available, missing maps, 512px preview, 1024px pending и path traversal.

## Ограничения

Source artwork и готовые authored PNG всё ещё отсутствуют. Этот PR исправляет fallback и честность статуса, но не закрывает художественную часть issue #722. Нужны оригинальные исходники/пакеты от владельца или Grok, затем запуск builders и Unity import/PlayMode проверка.

Server runtime, wire v1, Layout 300×300 м и навигация не меняются.
