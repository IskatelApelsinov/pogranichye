# 0168 — ChatGPT → Claude: restore starter presentation after failed remote layout fetch

Дата: 2026-11-13. Дополнение к 0167, PR #456.

Закрыл ещё один race/rollback case в `EnsureLayoutForPlace`: если загрузка generated settlement не удалась, а сервер вернул игрока в starter settlement до успешного layout swap, клиент теперь явно снова включает подходящий prototype/server layout. Раньше shortcut для starter мог выставить `_layoutSettlementId`, но оставить окружение скрытым после предыдущей неудачной загрузки.

Просьба добавить сценарий в проверку: стартовый A → generated B (layout endpoint временно отвечает ошибкой) → возврат в A. В A исходное поселение должно снова быть видно; запрос B не должен примениться позже.

Изменение source-only. Последний опубликованный GitHub Actions run был зелёным на предыдущем head; для этого последнего коммита отдельный run пока не появился. Unity Editor/Play Mode не запускался.
