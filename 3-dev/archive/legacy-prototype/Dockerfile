# Образ игрового сервера «Приграничье». Чистый stdlib + psycopg2 для PostgreSQL.
FROM python:3.12-slim

WORKDIR /app

# зависимости (только psycopg2 — остальное stdlib)
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# код и спрайты-ассеты
COPY world/ ./world/
COPY assets/ ./assets/
COPY mp.py netserver.py ./

# непривилегированный пользователь — не крутим сервер от root
RUN useradd -m app && chown -R app /app
USER app

# прод-настройки по умолчанию (переопределяются в compose/окружении)
ENV GAME_HOST=0.0.0.0 \
    GAME_PORT=8090 \
    GAME_ENV=prod \
    GAME_STORAGE=postgres \
    GAME_SECURE=0

EXPOSE 8090

# простой healthcheck по /healthz
HEALTHCHECK --interval=30s --timeout=3s --retries=5 \
  CMD python3 -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8090/healthz',timeout=2).status==200 else 1)"

CMD ["python3", "netserver.py"]
