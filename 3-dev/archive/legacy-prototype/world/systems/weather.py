"""ПРИМЕР ПОДКЛЮЧАЕМОГО МОДУЛЯ. Погода: иногда приходит холодная волна —
жители жгут больше дров (ниже порог нужды в дереве). Ничего в ядре не меняет,
подключается одной строкой: engine.register(WeatherSystem()).
Так же потом подключатся магия, бой, торговля, сезоны.

Отключить: engine.unregister("weather").
"""
from __future__ import annotations
from ..engine import System


class WeatherSystem(System):
    name = "weather"

    def __init__(self):
        self.cold_days_left = 0

    def on_day(self, w, clock, rng):
        # холодные волны (как было)
        if self.cold_days_left > 0:
            self.cold_days_left -= 1
            w.store["wood"] = max(0.0, w.store["wood"] - len(w.living()) * 1.0)
            if self.cold_days_left == 0:
                w.cold = False
                w.add_news(clock, "Холода отступили, снова можно выдохнуть.")
        elif rng.random() < 0.06:
            self.cold_days_left = rng.randint(2, 4)
            w.cold = True
            w.add_news(clock, "С севера пришёл холод — дров теперь нужно больше.")

        # погодные события дня (помимо холода): влияют на дома и урожай
        pop = max(1, w.pop())
        roll = rng.random()
        if roll < 0.05:
            w.weather = "буря"
            w.house_integrity = max(0.0, w.house_integrity - 8.0)
            w.add_news(clock, "⛈ Буря трепала крыши — избы просят починки.")
        elif roll < 0.12:
            w.weather = "ливень"
            w.store["food"] = w.store.get("food", 0) + pop * 0.6     # всходам на пользу
            w.add_news(clock, "🌧 Добрый ливень — всходы пошли в рост.")
        elif roll < 0.17:
            w.weather = "засуха"
            w.store["food"] = max(0.0, w.store.get("food", 0) - pop * 0.6)
            w.add_news(clock, "🌵 Засушливый день — урожай подвял.")
        elif roll < 0.23:
            w.weather = "вёдро"
            w.store["honey"] = w.store.get("honey", 0) + pop * 0.3   # пчёлам раздолье
            w.add_news(clock, "☀ Ясное вёдро — пчёлы несут мёд.")
        else:
            w.weather = "ясно"
