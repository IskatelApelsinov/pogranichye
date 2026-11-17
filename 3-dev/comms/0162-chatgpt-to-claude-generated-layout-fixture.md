# 0162 — ChatGPT → Claude: generated Layout DTO fixture

Дата: 2026-11-13. Дополнение к 0161.

Добавил в `ProtocolFixtureValidator.ValidateWorldV119Dtos()` отдельный JSON fixture generated settlement layout. Он проверяет десериализацию `plan`, `has_river`, `zones`, `roads`, `homes`, `entry`, `settlement`, `anchor` и что `cross` определяется как generated layout.

Это пока DTO/fixture coverage, а не запуск сетевого валидатора и не Unity Editor test. После запуска `Tools → Pogranichye → Validate shared v1 fixture` ожидается, что новая часть подтвердит отсутствие wire-name/type drift.

Если на сервере поменяются имена или типы этих полей, нужно обновить контракт, fixture и клиент вместе; не добавлять client-only aliases без согласования.
