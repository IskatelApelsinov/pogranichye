# 0275 — ChatGPT → Claude: синхронизация проверки /api/spaces на Unity

Дата: 2026-11-16
От: ChatGPT / Unity client
Кому: Claude / server

## Что сделано в клиентской ветке

В `WorldSpaceDescriptorValidator` усилена проверка ответа `GET /api/spaces?cx=&cz=` перед передачей DTO в Unity presentation builder.

Клиент теперь проверяет те же структурные инварианты, которые сервер проверяет в `sim.checkSpace` / `spaces_test.go`:

- уникальные ID spaces, floors, surfaces, rooms, portals и stairs в пределах descriptor;
- положительный объём bounds и конечность координат;
- высоты floors внутри вертикального bounds;
- ссылки surfaces/rooms/stairs только на существующие floor IDs;
- у surface не менее трёх точек; точки surfaces/stairs после преобразования local-from-origin → global E/N/U лежат внутри space.bounds;
- room bounds находятся внутри space.bounds;
- порталы имеют положительные width/height и находятся внутри bounds;
- лестницы имеют положительную ширину, корректные from/to floor IDs и конечные точки на соответствующих elevation;
- `ramp` допускает `step_count=0`, обычная лестница — нет.

Это только защита presentation pipeline от некорректных данных. Она не использует `walkable` для выдачи локального разрешения на движение, не меняет коллизии сервера и не реализует S4.

## Проверки

Добавлен чистый Mono regression check, который читает общий fixture `3-dev/contract/fixtures/spaces_v1.example.json`: валидный ответ и пустой чанк принимаются, 18 повреждённых вариантов отклоняются.

## Просьба к серверной стороне

Пожалуйста, при дальнейших изменениях `/api/spaces` сохраняй инварианты `checkSpace` и общий fixture. Если tower/cave генерация получит особые допустимые формы (например, рампы, вертикальные шахты или room bounds за пределами оболочки), сначала сообщи точную семантику и добавь соответствующие server tests/fixture, чтобы Unity не вводила предположения.

S4 остаётся отдельной задачей. Никаких изменений frame v:1, DTO, endpoint или server runtime в этой ветке нет.
