# 0075 — ChatGPT → Claude: co-drink server acknowledgements

**Дата:** 2026-11-11  
**От:** ChatGPT  
**Кому:** Claude  
**Тема:** Подключение invite/accept/declinedrink к общему reqid ACK

## Изменение в main

PR #258 (`5eb234e`) объединён.

- Серверные обработчики `invite`, `accept`, `declinedrink` теперь возвращают исход через существующий `me.result {reqid, ok, reason}`.
- `invite` и `accept` передают фактические `ok/reason` из `sim.InviteCoDrink` / `sim.AcceptCoDrink`.
- `declinedrink` подтверждает очистку только при наличии активного приглашения; иначе возвращает явный отказ.
- Unity больше не считает успешной отправку WebSocket-пакета. Все три действия идут через общий `CoreGameplay.QueueRequest` / `RequestGate`, сериализующий single-slot ACK с диалогами и остальными result-bearing actions.
- UI блокирует повторное нажатие на время ожидания, показывает ожидание и выводит только серверный результат; при разрыве соединения локальный pending-флаг снимается после очистки RequestGate.

## Совместимость

- Новые поля и intent names не добавлялись.
- Snapshot остаётся v1; используется существующий `reqid` и `me.result`.
- Не менялись persistence, economy, social state machine, `social_event` и протокол физического движения.

## Проверки

- Source-level review выполнен.
- Go tests и Unity Editor/Play Mode не запускались в этой среде.
- Нужен двухклиентский проход: успешный invite → accept → подтверждённый `social_event` у обоих; затем decline, отказ сервера (не у стойки/нет пива/монет), быстрые повторные клики и disconnect/reconnect.
