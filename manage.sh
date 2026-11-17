#!/usr/bin/env bash
# Приграничье — управление сервером живого мира в контейнере.
# Использование:
#   ./manage.sh up       — собрать и поднять сервер (docker compose up -d --build)
#   ./manage.sh update   — подтянуть свежий код из git и пересобрать (git pull + up)
#   ./manage.sh logs     — смотреть логи сервера
#   ./manage.sh stop     — остановить
#   ./manage.sh restart  — перезапустить
#   ./manage.sh demo [дни] — headless-прогон мира с отчётом (по умолчанию 2000 дней), без контейнера
#
# Движок контейнеров берётся автоматически: docker (с compose) или podman-compose.
set -euo pipefail
cd "$(dirname "$0")"

# выбрать доступный инструмент: docker compose / docker-compose / podman-compose
compose() {
  if docker compose version >/dev/null 2>&1; then docker compose "$@";
  elif command -v docker-compose >/dev/null 2>&1; then docker-compose "$@";
  elif command -v podman-compose >/dev/null 2>&1; then podman-compose "$@";
  else echo "не найден ни docker compose, ни podman-compose" >&2; exit 1; fi
}

# ./data — то, что переживает перезапуск контейнера (аккаунты, сохранение мира, docker-compose.yml:
# "./data:/data"). Создаём заранее от своего пользователя, чтобы её не создал за нас Docker (часто
# от root) — именно так раньше ломалась запись accounts.json/world.sav внутри контейнера (ищем
# владельца не на этапе сборки образа, а у docker-entrypoint.sh — он чинит владельца и при уже
# существующей ./data на каждом старте, так что это просто подстраховка для первого запуска).
mkdir -p data

case "${1:-up}" in
  up)       compose up -d --build; echo "Поднято. Открой http://localhost:8090/" ;;
  update)   git pull --ff-only && compose up -d --build; echo "Обновлено и пересобрано." ;;
  logs)     compose logs -f ;;
  stop)     compose down ;;
  restart)  compose restart ;;
  demo)     ( cd 1-server && go run ./cmd/pogserver -demo -days "${2:-2000}" ) ;;
  *)        echo "команды: up | update | logs | stop | restart | demo [дни]"; exit 1 ;;
esac
