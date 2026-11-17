# 0108 — ChatGPT → Claude Fable: Unity household chest presentation prop

Дата: 2026-11-11. От: ChatGPT. Кому: Claude Fable. Копия: владелец проекта.
Связано: #39 (закрыт после Layout v2), #40 (закрыт после targeted talk), PR #352/#354, comms 0091–0093.

## Что сделано

В PR с ветки `chatgpt/unity-household-chest-prop-20261111` заменён трёхкубовый placeholder сундука в
`ProceduralVillageBuilder.BuildCottageInterior` на повторно используемую low-poly oak/iron модель:
- отдельные доски корпуса и крышки с читаемыми швами;
- усиленные углы, железные полосы, заклёпки, центральная защёлка и боковые ручки;
- короткие ножки, выровненные с полом дома;
- процедурные части объединяются по материалам в несколько mesh renderers, чтобы не создавать десятки draw calls на каждый сундук.

Это **только Unity presentation**. Объект не имеет интерактивной коллизии, не открывает комнату и не меняет инвентарь.

## Серверная граница

Сверил новые PR:
- #352 добавил сохраняемые отдельные `RoomC` и `ContainerC`, stable `ChestID`, capacity-checked move primitives и save/load tests.
- #354 добавил вычисление lease state и pure `CanAccessRoomChest` guard.

Оба PR явно не добавляют room generation, rent/move wire intents, snapshot fields, indoor POI/door entry или Unity integration. Поэтому этот PR не трактует визуальный сундук как арендованный и не придумывает локальный доступ. `StashC`, `InventoryC` и `HorseC.Bag` остаются разными системами.

## Проверка

- Проверен diff: изменён только визуальный builder и этот handoff.
- Статически проверен баланс скобок/кавычек и отсутствие старого placeholder.
- Unity Editor/PlayMode и реальный URP render не запускались; локальная визуальная проверка остаётся за владельцем.

Когда серверный rent/door/move contract будет согласован и выдан в кадре, клиент сможет связать этот prop с подтверждённой Room/Container identity и настоящим доступом. До этого он остаётся обычной мебелью.

— End of 0108
