#!/usr/bin/env bash
# Управление сервером «Приграничья» на ВМ: запуск в разных режимах, обновление, логи.
# Использование:
#   ./manage.sh dev            — запустить в DEV (гостевой вход включён — нужен для толстого клиента)
#   ./manage.sh prod           — запустить в PROD (гость выключен, вход по аккаунту)
#   ./manage.sh update         — git pull + пересборка + перезапуск (режим сохраняется)
#   ./manage.sh stop           — остановить (данные БД сохраняются)
#   ./manage.sh reset          — остановить и СТЕРЕТЬ БД мира, затем поднять заново (dev)
#   ./manage.sh logs           — смотреть логи сервера (Ctrl+C — выйти)
#   ./manage.sh status         — статус контейнеров и адрес
#   ./manage.sh install-docker — поставить Docker на Debian/Ubuntu (один раз, нужен sudo)
#
# Порт берётся из APP_PORT в .env (по умолчанию 8090). Пароль БД — DB_PASSWORD.
set -euo pipefail
cd "$(dirname "$0")"

ENV_FILE=".env"

# создать .env с дефолтами, если его нет
ensure_env() {
  if [ ! -f "$ENV_FILE" ]; then
    cat > "$ENV_FILE" <<EOF
# настройки запуска (меняй под себя)
GAME_ENV=dev
GAME_SECURE=0
APP_PORT=8090
DB_PASSWORD=user
EOF
    echo "[i] создан $ENV_FILE со значениями по умолчанию (GAME_ENV=dev, порт 8090)."
  fi
}

# выставить GAME_ENV в .env (dev|prod)
set_mode() {
  ensure_env
  if grep -q '^GAME_ENV=' "$ENV_FILE"; then
    sed -i "s/^GAME_ENV=.*/GAME_ENV=$1/" "$ENV_FILE"
  else
    echo "GAME_ENV=$1" >> "$ENV_FILE"
  fi
  echo "[i] режим: GAME_ENV=$1"
}

port() { grep -E '^APP_PORT=' "$ENV_FILE" 2>/dev/null | cut -d= -f2 || echo 8090; }

up() {
  ensure_env
  echo "[i] сборка и запуск…"
  docker compose up -d --build
  echo "[✓] запущено. Проверка: http://localhost:$(port)/healthz"
  echo "    тонкий клиент: http://localhost:$(port)   наблюдатель: http://localhost:$(port)/observe"
}

case "${1:-}" in
  dev)   set_mode dev;  up ;;
  prod)  set_mode prod; up ;;
  update)
    ensure_env
    echo "[i] обновление из git…"
    git pull --ff-only || { echo "[!] git pull не прошёл — разреши конфликты вручную"; exit 1; }
    up ;;
  stop)  docker compose down; echo "[✓] остановлено (БД сохранена)";;
  reset)
    read -r -p "Стереть БД мира и поднять заново? (y/N) " a
    [ "${a:-N}" = "y" ] || { echo "отмена"; exit 0; }
    docker compose down -v
    set_mode dev; up ;;
  logs)   docker compose logs -f app ;;
  status)
    docker compose ps
    echo "адрес: http://localhost:$(port)  (режим: $(grep -E '^GAME_ENV=' "$ENV_FILE" 2>/dev/null | cut -d= -f2))" ;;
  install-docker)
    echo "[i] установка Docker (Debian/Ubuntu, нужен sudo)…"
    sudo apt-get update
    sudo apt-get install -y ca-certificates curl gnupg
    sudo install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/debian/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg || \
      curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    sudo chmod a+r /etc/apt/keyrings/docker.gpg
    . /etc/os-release
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/$ID $VERSION_CODENAME stable" | \
      sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
    sudo apt-get update
    sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    sudo usermod -aG docker "$USER" || true
    echo "[✓] Docker установлен. Перелогинься (или 'newgrp docker'), затем: ./manage.sh dev" ;;
  *)
    echo "Приграничье — управление сервером"
    echo "команды: dev | prod | update | stop | reset | logs | status | install-docker"
    echo "пример первого запуска:  ./manage.sh install-docker  &&  ./manage.sh dev" ;;
esac
