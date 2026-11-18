# Нагрузочный стенд pogload (synthetic load)

Ответ на comms 0284. Инструмент `1-server/cmd/pogload` проверяет не только
ECS/симуляцию, но и полный серверный путь: HTTP API → регистрация/вход →
WebSocket → резервирование тела → полные снапшоты → действия/ACK → AOI →
отключения/reconnect. Только стандартная библиотека Go; контракт клиент-сервер
(кадр `v:1`) не меняется, новых intents нет. Источник истины по протоколу —
`3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md` и тесты пакета `server`.

## Что делает каждый бот

1. `POST /api/register` и `POST /api/login` — настоящий аккаунт (уникальный
   префикс логина `pl-<runID>-<N>-<idx>`), пароль и токен в лог не пишутся.
2. `GET /ws?token=...&fast=<hz>` — реальное WS-рукопожатие (сверяется
   `Sec-WebSocket-Accept`), затем чтение и разбор реальных кадров `v:1`.
3. Шлёт только поддерживаемые intents: `op:"goal"` (c `reqid` — проверяется ACK
   `me.result`) и `op:"move"` (поток шага). Открытый TCP без валидного первого
   кадра игроком НЕ считается.
4. Параллельный опрос публичных `GET /api/world|stats|layout|terrain|spaces`
   (латентность и статусы по endpoint).
5. churn-боты уходят и переподключаются — проверяется освобождение сессий,
   отсутствие двойного управления телом (`body.duplicate`) и время reconnect.

## Сценарии (`-scenario`)

- `co-located` — все игроки на месте (worst-case AOI/neighbor-сериализация).
- `distributed` — игроки расходятся (локальные снапшоты не должны тянуть весь мир).
- `churn` — плавный старт, затем `-churn`% уходят и переподключаются.
- `mixed` — idle + ходьба + активные действия + фоновый HTTP-опрос.

## Ступенчатая нагрузка

`-ramp 1,10,25,50,100` прогоняет ступени по очереди (acceptance target — 100).
Опционально 150/250 для поиска предела. Большой мир — `-villages` у pogserver
(диагностика: 300; 1000 — только как профиль, не acceptance).

## Запуск

Изолированный сервер (отдельный save, чтобы тестовые аккаунты/мир не загрязняли
пользовательское сохранение; быстрый тик):

    go run ./cmd/pogserver -addr :8090 -villages 40 -save pogload.sav -tick 400

Нагрузка:

    go run ./cmd/pogload -addr http://localhost:8090 -n 100 -scenario distributed -duration 60s -report pogload-report.json

Против удалённого сервера нужен явный флаг подтверждения `-i-understand-remote`
(по умолчанию разрешён только localhost — защита от попадания на production).

## Отчёт

JSON (`-report`) + краткая читаемая сводка в stdout:
- попытки/успехи register/login/WS, активные клиенты, busy/none резервирования тел;
- connect / login / first-frame / ACK / reconnect latency p50/p95/p99;
- снапшоты: count, bytes total, bytes/sec, размер кадра p50/p95/p99, malformed;
- HTTP latency p50/p95/p99 по endpoint + статусы;
- AOI max/avg (размер `place.neighbors`);
- `/api/stats` сервера на пике; runtime самого загрузчика (goroutines/heap/GC —
  это метрики loadgen-процесса, не сервера; серверный CPU/RSS снимать внешне, без
  публичного pprof в production);
- конфигурация прогона, commit SHA (через `-ldflags -X main.commitSHA`), версия Go, OS/arch, wall-clock.

## Жёсткие функциональные критерии (`passed`)

- все запрошенные WS-клиенты получили валидный первый snapshot (`valid_first_frame == players`);
- нет дублированного управления телом (`body.duplicate == 0`);
- после закрытия сессии освобождаются (контрольный бот снова получает тело);
- нет panic; HTTP API продолжает отвечать во время нагрузки.

Пороги производительности здесь НЕ заданы: сначала собрать baseline на CI и на
машине разработчика, затем назначить regression-пороги.

## Автоматизация

- Модульные тесты парсера перцентилей/ACK/конфига и интеграционный «живой» дым
  (4 клиента in-process) — в `cmd/pogload` (гоняется обычным `go test ./...`).
- Полный 100-игроков / большой мир прогон — ручной workflow
  `.github/workflows/pogload-nightly.yml` (`workflow_dispatch`), отчёт
  сохраняется как artifact. Не на каждый PR.

## Границы

- Не менять frame v1 и gameplay-семантику ради нагрузки.
- Не использовать production-аккаунты/сохранения.
- Unity-профилирование (FPS, draw calls, RX/TX при 1/10/25/50/100 видимых
  игроках) — отдельный клиентский acceptance-чеклист на стороне ChatGPT, не
  подменяется серверным бенчем.
