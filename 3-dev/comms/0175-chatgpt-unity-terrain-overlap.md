# 0175 — Unity: overlap глобальных terrain chunks между поселениями

Дата: 2026-11-13  
Ветка: `feat/unity-terrain-stream-overlap-20261113`  
Цель: убрать искусственную очистку глобального terrain cache при смене settlement ID, не объявляя весь клиент seamless-ready.

## Изменение

В `VerticalSliceController.EnsureTerrainChunkForFrame`:
- Cache identity остаётся `frame:chunkX:chunkZ`, не `place.id`.
- При смене `Place.Id` больше не увеличивается stream generation, не очищаются chunks/requests и не вызывается `WorldTerrainChunkBuilder.Clear`.
- Уже загруженные chunks повторно применяются с текущим `place.anchor`. Это сохраняет их глобальное положение в локальной presentation системе нового settlement anchor.
- Запросное окно остаётся 3×3 (центр первым), максимум три HTTP запроса одновременно.
- Добавлен retention radius 2 chunks: окно запроса 3×3, overlap cache удерживает до 5×5; удаление чанков происходит только за пределами retention radius.
- In-flight response может завершиться после смены settlement и примениться, если его global chunk всё ещё внутри retention radius. Reconnect по-прежнему увеличивает generation и очищает кэш/геометрию старой сессии.

## Почему это безопаснее

Settlement — не идентичность terrain. Одинаковый chunk ID в одном canonical frame описывает ту же глобальную геометрию независимо от того, какое поселение сейчас активно. Перебазирование mesh относительно нового anchor меняет только presentation coordinates, а не server WorldPos.

## Ограничения / что НЕ сделано

- Это не реализует multi-layout. `WorldLayoutPresentationBuilder` по-прежнему строит один `ServerLayoutV2`; `EnsureLayoutForPlace` скрывает старый layout во время загрузки нового. Не удалять эту защиту простым отключением SetVisible(false): иначе геометрия чужого поселения останется в неверном месте.
- Не добавлены глобальные entity coordinates для NPC/players, spatial AOI, floating-origin для всех объектов, дороги/реки между settlement layouts или seamless navigation.
- Серверный `-seamless=on` не является acceptance доказательством Unity.

## Проверка

- Требуется Unity Editor `6000.6.7f1` / URP PlayMode: пройти chunk boundary и A→B→A, проверить отсутствие полосы пустого рельефа, скачка чанков и старых ответов поверх нового центра.
- Отдельно проверить reconnect: ответы старого поколения не должны применяться.
- GitHub C# independent checks запускаются по PR; они не заменяют Unity compilation/PlayMode.
