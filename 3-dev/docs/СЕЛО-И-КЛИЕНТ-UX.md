# Село: симуляция + Unity UX — консультация Grok

Дата: 2026-11-14

Запрос владельца: купить на рынке, поболтать, порыбачить, порубить, помочь; UI неинтуитивный; классическое управление; странные шрифты/обрезанные сообщения; текстуры и модели.

P0 fantasy (путь + пиво с другом) не отменяется. Этот документ — слой «жизнь в селе» и клиентская читаемость.

## 1. Сервер: что уже есть в селе

| Действие | Код | Условие |
|----------|-----|--------|
| Купить пиво | BuyBeer | стойка + bartender + stock |
| Выпить / co-drink | DrinkBeer, CoDrink | mug / оба у tavern |
| Разговор | StartTalk / Say | reach, settlement |
| Поручение | Errand | знакомый NPC, нужда |
| Рубка | ChopWood | зона woods, энергия |
| Хворост | GatherBrushwood | copse |
| Рыбалка | FishCatch | river / NearWater |
| Сбор | Gather | craft |
| Рынок | Trade | good/side/qty, reqid |
| Помощь в поле | Farmwork | сезон сев/жатва |
| Помощь у горна | Forge | руда |

Симуляция деревни на сервере сильнее, чем ощущается в Unity. Дыра чаще: не видно куда встать, непонятно какую кнопку жать, reason обрезан, NPC не у точки действия.

Клиент должен показывать server reason целиком и подсвечивать POI (river, woods, market, tavern_counter).

## 2. Unity: управление и UI

Сейчас: Input System WASD + много combat/weave binds; панель «Дела»; много IMGUI с fontSize 8…62; world TextMesh 42/48 без единой шкалы.

Целевой UX демо (классика):

- WASD + мышь — ходить/смотреть
- E / Interact — контекст у POI/NPC
- Tab/I — инвентарь
- J — журнал поручения
- Esc — меню

Контекст: у NPC Talk; у counter Beer; у river Fish; в woods Chop; у market Trade. Одна подсказка внизу: [E] … + полный me.result.reason.

Бой/плетение — второй слой, не мешать сельскому циклу.

## 3. Текст и шрифты

Ввести единые размеры: HUD 14–18; toast 15–16 с word wrap (не резать reason); debug 10–11 отдельно; world nameplate один scale. Кириллица — один шрифт с полным набором или TMP+fallback.

## 4. Текстуры и модели

Pack textures/grok уже есть. Дальше: материалы URP; 3–5 props (стойка, прилавок, колодец, инструмент в руке). Readability > density.

## 5. Приоритеты

1. P0 путь 009/010 (Claude)
2. P0 корчма 013/012 + клиент C1/C2
3. Контекст E + полный reason toast (ChatGPT)
4. Единые шрифты HUD/world (ChatGPT)
5. Подсветка POI
6. Рынок UI на Trade
7. Art materials + key props

Новые серверные системы для рыбалки/рубки/разговора не обязательны.
