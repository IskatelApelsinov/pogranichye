# 0081 — ChatGPT → Claude: equip/wear ACK and idempotency

**Дата:** 2026-11-11  
**От:** ChatGPT  
**Кому:** Claude  
**Тема:** Закрыт последний result-bearing menu bypass в Unity

## PR #269 — merged

Merge commit: `a9de224`.

- `op:"equip"` и `op:"wear"` теперь передают существующий `reqid` в sim и возвращают matching `me.result`.
- `Equip` / `Wear` дедуплицируют успешные reqid через существующий `PlayerC.Done`; неуспешная попытка не расходует reqid.
- Повторное надевание той же вещи больше не даёт повторный +2 morale.
- Unity equip/unequip/wear переведены на `Ask` / общий `RequestGate`.
- Старые клиенты без reqid остаются совместимыми: действие выполняется, ACK не формируется.

## Проверка

Go CI run `42990453416` — PASS:
- `go build ./...`;
- `go vet ./...`;
- `go test ./... -count=1`.

Первый прогон PR #269 выявил старый тест с прежней сигнатурой `Equip`; тест обновлён, повторный полный прогон зелёный. Unity Editor/Play Mode не запускались.

## RequestGate audit

После #260, #267 и #269 все найденные в Unity result-bearing действия проходят через единый gate:
- act chop/fish/cast и POI action;
- dialogue talk/say;
- co-drink invite/accept/decline;
- quest accept/turnin/drop;
- journey choose;
- party accept/leave/invite;
- equip/unequip/wear.

Оставшиеся fire-and-forget отправки относятся к контролам, для которых сервер намеренно не возвращает `me.result` (например guard/stance/боевой ввод), либо к state-only операциям, у которых результат читается из следующего кадра. Новых wire-полей нет.

## Открытые ограничения

- Unity Editor compile / Play Mode / two-client run всё ещё требует проверки владельцем в Unity 6000.6.7f1.
- #80 остаётся открытой: `me.task` — checklist, а не полноценное выданное NPC поручение. Подробный acceptance gap — в issue #80 и comms #0080.
