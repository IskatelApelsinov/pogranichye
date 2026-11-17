# Go CI/CD — текущее состояние и предложение усиления

Дата: 2026-11-14  
Автор: Grok (консультант)  
Статус: предложение. Существующий workflow не изменён.

## 1. Что уже есть

Файл: `.github/workflows/go-server-ci.yml`

- Триггеры: PR + push в `main` только по `1-server/**`
- Go-версия берётся из `1-server/go.mod` (сейчас `go 1.24`)
- Шаги: `go build ./...` → `go vet ./...` → `go test ./... -count=1`
- Дополнительно 5 прогонов `TestVerticalSliceTalkWorkBeer` (тайминги)
- Concurrency + cancel-in-progress — нормально

Замечания:
- `go.sum` отсутствует (модуль без внешних зависимостей) → `cache-dependency-path: 1-server/go.sum` бесполезен, но не ломает.
- Нет race detector.
- Нет coverage.
- Нет staticcheck / golangci-lint.
- Нет короткого smoke-прогона симуляции (`-demo -days N`).

## 2. Предлагаемый усиленный workflow

Скопировать содержимое ниже в `.github/workflows/go-server-ci.yml` (или сделать PR).  
Изменения аддитивные по смыслу, совместимы с текущим стилем проекта.

```yaml
name: Go server CI

on:
  pull_request:
    paths:
      - "1-server/**"
      - ".github/workflows/go-server-ci.yml"
  push:
    branches: [main]
    paths:
      - "1-server/**"
      - ".github/workflows/go-server-ci.yml"
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: go-server-ci-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  go-server:
    name: Build, vet, test, race, coverage
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: 1-server

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-go@v5
        with:
          go-version-file: 1-server/go.mod
          cache: true
          # go.sum пока нет — не указываем cache-dependency-path

      - name: Build
        run: go build ./...

      - name: Vet
        run: go vet ./...

      - name: Test
        run: go test ./... -count=1 -timeout=10m

      - name: Race (core packages)
        run: go test ./core ./sim ./mind ./memory ./dice ./econ -race -count=1 -timeout=15m

      - name: Coverage
        run: |
          go test ./... -coverprofile=coverage.out -covermode=atomic -count=1
          go tool cover -func=coverage.out | tail -8

      - name: Vertical-slice E2E (timing)
        run: go test ./server -run=TestVerticalSliceTalkWorkBeer -count=5

      - name: Short demo simulation (smoke)
        run: go run ./cmd/pogserver -demo -days 30
```

## 3. Что можно добавить позже (не обязательно сейчас)

- `golangci-lint` отдельным job (после того, как базовый pipeline стабилен).
- Загрузка coverage в GitHub Summary / Codecov.
- Матрица версий Go, если понадобится обратная совместимость.
- Кэш модулей, когда появятся внешние зависимости и `go.sum`.

## 4. Связь с работоспособностью

Усиленный CI не чинит клиентские жалобы (движение, таверна, мир до горизонта), но:
- быстрее ловит регрессии симуляции и race;
- даёт coverage-сигнал по ядру;
- smoke `-demo -days 30` подтверждает, что мир вообще крутится после изменений.

Приоритет по демо — см. `3-dev/docs/ПЛАН-ВОССТАНОВЛЕНИЯ-ДЕМО.md` (P0).
