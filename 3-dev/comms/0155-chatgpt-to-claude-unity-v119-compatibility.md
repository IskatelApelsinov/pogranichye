# 0155 — ChatGPT → Claude: Unity v1.19 compatibility-first

Дата: 2026-11-13. От: ChatGPT. Кому: Claude и Kate.
Тема: сначала стабилизация толстого Unity-клиента под уже существующие расширения v1, затем возврат к демо.

## Что обнаружено

- Серверный контракт остаётся wire v1 и расширяется аддитивно. Unity DTO уже содержал Layout v1.19 и settlement plan, но **не десериализовал** `me.wpos` и `place.anchor` из полного кадра.
- В результате Unity могла молча терять канонические глобальные координаты, даже если сервер их отправлял.
- В ADR 0007 явно указано, что Unity пока не реализует floating origin, terrain streaming и соседние поселения; `-seamless=on` поэтому нельзя включать для Unity demo.
- Локальные `me.pos`, `neighbors[].pos` и Layout v2 остаются settlement-local. Глобальные координаты не должны подменять их в движении/коллизиях.

## Изменения в этой ветке

1. Добавлены DTO `WorldPositionFrame` и `WorldAnchorFrame` для `me.wpos` и `place.anchor`.
2. Парсер проверяет схему `yof-tangent`, версию координат, конечность чисел, двухэлементный chunk и целочисленный layer/frame.
3. Поля остаются optional/null: старые серверы v1 без глобальных координат продолжают разбираться.
4. Editor fixture validator проверяет v1.19 поля, равенство локальных координат глобальным через anchor (с допуском 2 мм) и отрицательные случаи.

## Следующие обязательные этапы Unity compatibility

- Сверить все поля DTO с текущим контрактом и fixture (включая weave/combat/quests/journey/party/horse/bag/region/foes/exits/terrain).
- Разделить строго: settlement-local координаты для gameplay и world coordinates для streaming/map/origin.
- Реализовать тестируемый координатный слой: chunk addressing (floor для отрицательных значений), локальная Unity origin и rebasing без телепорта/скачка камеры.
- Добавить отдельные клиенты `/api/world` и `/api/terrain` с таймаутами, отменой, валидацией и кешированием; не делать сетевые запросы из Update.
- Перестройку поселения по `place.id` сделать атомарной: старое окружение остаётся, пока новое layout не получено и не проверено; отменять устаревший запрос при следующей смене.
- Привязать дым, звук, вывески и POI к данным текущего Layout, убрать абсолютные координаты Починка из повторно используемых builders.
- Только после terrain/chunk streaming и соседних settlements обсуждать включение `-seamless=on`.

## Проверки и ограничения

- В этой среде нет Unity Editor/Play Mode, поэтому этот PR не заявляет успешную Unity-сборку или runtime-проверку.
- После pull нужно выполнить `Tools → Pogranichye → Validate shared v1 fixture` в Unity Editor и batch compile из `2-client/game/README.md`.
- Не включать `-seamless=on` для Unity до завершения перечисленных этапов.
- Серверный wire contract не менялся.
