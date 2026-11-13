# 0176 — Unity: сохранять старую геометрию поселения во время загрузки новой

Дата: 2026-11-13  
Ветка: `feat/unity-preserve-layout-during-transition-20261113`

## Изменение

- Добавлен `WorldLayoutPresentationBuilder.SetPresentationOffset`: cached `ServerLayoutV2` можно размещать относительно активного presentation origin.
- `VerticalSliceController.TryKeepCurrentLayoutAtGlobalPosition` вычисляет offset как `oldLayout.anchor - currentPlace.anchor` в том же frame.
- При переходе между поселениями прежний layout остаётся видимым на своём глобальном месте, пока новый layout загружается и проверяется по settlement ID + anchor.
- После успешной валидации новый layout строится в нулевом локальном offset относительно нового anchor и заменяет старый.
- Если игрок возвращается в старое поселение до ответа, layout возвращается к offset zero, запрос отменяется через generation guard.
- Если у старой геометрии нет проверенного anchor или frame не совпадает, применяется прежняя безопасная логика скрытия; не показываем неизвестную геометрию поверх другого поселения.

## Что это решает

Убирает искусственное исчезновение уже загруженного поселения на время сетевого запроса при переходе между generated settlements. Старая геометрия не остаётся в координатах нового поселения и не накрывает его здания: она смещается по разнице canonical anchors.

## Границы

- Пока отображается только один layout instance; это не multi-layout cache и не генерация дорог/рек между поселениями.
- Если игрок уже ушёл далеко за границы старого layout, его геометрия остаётся в правильной точке, но новые здания появятся только после ответа сервера.
- Starter prototype без validated `WorldLayoutFrame.Anchor` не получает этот offset path.
- Terrain overlap PR #463 остаётся отдельной основой.
- Не заявлять полную бесшовность без spatial AOI, global entity presentation, multi-layout cache, road/nav continuity и Unity A→B→A PlayMode.

## Проверка

Unity 6000.6.7f1: A→B с искусственно задержанным /api/layout; A остаётся видимым в правильной стороне мира, B показывается после anchor validation; затем B→A до завершения запроса B — поздний ответ B не заменяет A. Проверить разные anchors, frame mismatch и failed layout response.
