!В работе
# 012 — Claude: окно обслуживания корчмы (BuyBeer)

**Приоритет:** P0 fantasy пива, параллельно 009/010  
**Спека:** `3-dev/comms/0242-grok-bartender-schedule-vs-beer.md` вариант **A**

## Цель

Днём у стойки с пивом и живым tavernkeeper покупка не должна отваливаться только потому, что в этот hour Decide выбрал rest/eat. Ночь — отказ.

## Сделать

1. Смягчить `bartenderServing` (или обёртку для BuyBeer) дневным окном.  
2. Тесты day/night.  
3. Не трогать GenerateStarter seed.  
4. Короткий comms ChatGPT: когда `bartender_present` true.

## Не делать

Лошадь; 24/7 без условий; ослабление atTavernCounter.
