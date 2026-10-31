# Приграничье — сервер живого мира (Go) + тонкий веб-клиент, в одном образе.
# Multi-stage: сначала собираем статический бинарь, затем кладём его в маленький образ.
# Контекст сборки — корень репозитория (нужны и 1-server, и thin-client).

# --- сборка ---
FROM golang:1.24-alpine AS build
WORKDIR /src
# зависимостей нет (только stdlib), поэтому достаточно go.mod и исходников
COPY 1-server/go.mod ./1-server/go.mod
COPY 1-server/ ./1-server/
RUN cd 1-server && CGO_ENABLED=0 GOOS=linux go build -trimpath -o /out/pogserver ./cmd/pogserver

# --- рантайм ---
FROM alpine:3.20
RUN adduser -D -u 10001 pog && mkdir -p /data && chown pog /data
COPY --from=build /out/pogserver /usr/local/bin/pogserver
COPY thin-client/web/ /web/
USER pog
WORKDIR /data
# POG_SECRET задать в проде (ключ подписи токенов). По умолчанию мир «пограничье-1», 10 деревень.
ENV POG_SECRET=""
EXPOSE 8090
CMD ["pogserver", "-addr", ":8090", "-web", "/web", "-villages", "10"]
