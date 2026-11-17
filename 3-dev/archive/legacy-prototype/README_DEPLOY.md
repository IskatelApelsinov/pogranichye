# Приграничье — чистая установка (Docker)

Живой мир из деревень: сервер-авторитетная симуляция, WebSocket, PostgreSQL.

## Запуск с нуля

```bash
# 1) задать пароль БД (и при желании порт/режим)
export DB_PASSWORD='сильный-пароль'
# export GAME_ENV=dev       # включить гостевой вход (по умолчанию prod — только аккаунты)
# export GAME_SECURE=1      # если сервер за TLS/обратным прокси (куки только по https)
# export APP_PORT=8090      # внешний порт

# 2) поднять сервер + базу
docker compose up -d --build

# 3) проверить здоровье
curl http://localhost:8090/healthz      # {"ok": true}
```

Открыть игру: `http://<host>:8090`. Данные БД — в томе `pgdata` (переживают перезапуск).

## Переменные окружения

| Переменная      | Назначение                                   | По умолчанию |
|-----------------|----------------------------------------------|--------------|
| `DB_PASSWORD`   | пароль пользователя БД `game`                | changeme ⚠   |
| `GAME_ENV`      | `prod` — без гостя; `dev` — гость включён    | prod         |
| `GAME_STORAGE`  | `postgres` / `sqlite` / `json`               | postgres     |
| `GAME_SECURE`   | `1` — куки сессии только по https (за TLS)    | 0            |
| `DATABASE_URL`  | строка подключения к PostgreSQL              | из compose   |
| `GAME_HOST`/`GAME_PORT` | адрес и порт сервера                 | 0.0.0.0:8090 |

⚠ В проде обязательно сменить `DB_PASSWORD`, поставить сервер за TLS (обратный
прокси — nginx/caddy) и выставить `GAME_SECURE=1`.

## Без Docker (разработка)

```bash
pip install -r requirements.txt
GAME_STORAGE=sqlite python3 netserver.py      # SQLite-файл, гость включён (dev)
```

## Проверка безопасности/оптимизации — см. SECURITY.md
