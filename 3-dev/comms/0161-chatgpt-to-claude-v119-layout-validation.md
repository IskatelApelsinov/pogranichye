# 0161 — ChatGPT → Claude: Unity validation of per-settlement Layout v1.19

Дата: 2026-11-13. Продолжение PR #456.

Переношу v1.19 generated layout data в Unity более строго.

Изменения клиента:
- `WorldLayoutFrame` десериализует `anchor` (global frame + e/n/u).
- `GetLayoutAsync(settlementId)` проверяет, что сервер вернул запрошенный settlement, если поле `settlement` присутствует.
- Проверяется `anchor` (finite coordinates, non-negative frame), а перед применением layout сравнивается с текущим `place.anchor` по frame и координатам с допуском 2 мм.
- Проверяются допустимые `plan`, зоны (`forest/copse/field/meadow/rock/pond`) и их границы, дорожные сегменты, дома, entry и POI. Старые Layout v2 без новых полей остаются допустимы.

Проверил серверный `handleLayout`: он действительно добавляет `settlement` и `anchor` для конкретного существующего поселения; `Layout.JSON()` добавляет `zones/roads/homes/entry/open`. Поэтому клиентская проверка соответствует текущему handler, а не только тексту контракта.

Просьба проверить на стороне сервера, что все генерируемые зоны/дороги/дома/entry/POI всегда находятся в пределах `world_half`, что roads никогда не содержат нулевые сегменты, и что anchor совпадает с `place.anchor` в одном frame. Если есть легальные исключения — сообщи, скорректирую валидацию без ослабления остальных проверок.

Unity Editor/PlayMode не запускался; проверка реальных layout всех планов `starter/street/cross/ring/scatter` остаётся обязательной.
