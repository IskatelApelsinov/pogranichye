"""
Сезоны и зимовка. Год — 365 дней; четыре сезона. Зима — испытание: холодно всегда
(одежда обретает сезонный смысл, а не только в случайные волны), дрова и еда тают
быстрее. Подключаемый модуль; ставится ПОСЛЕ WeatherSystem, чтобы зимний холод был
главнее случайных оттепелей.

В реальном времени год проходит за пару часов непрерывной жизни мира, так что зиму
игроки застают нечасто — и это нормально: тем она и памятнее.
"""
from __future__ import annotations
from ..engine import System

DAYS_PER_YEAR = 365


def season_of(day: int) -> str:
    doy = (day - 1) % DAYS_PER_YEAR
    if doy < 91:
        return "весна"
    if doy < 182:
        return "лето"
    if doy < 273:
        return "осень"
    return "зима"


class SeasonSystem(System):
    name = "season"

    def on_day(self, w, clock, rng):
        prev = getattr(w, "season", "")
        season = season_of(clock.day)
        w.season = season
        if season == "зима":
            w.cold = True                          # зимой холодно всегда
            pop = max(1, w.pop())
            w.store["wood"] = max(0.0, w.store["wood"] - pop * 0.5)   # топят больше
            w.store["food"] = max(0.0, w.store["food"] - pop * 0.3)   # и едят запасы
            if prev != "зима":
                w.add_chronicle(clock, "Пришла зима. Деревня жмётся к очагам.")
                w.add_news(clock, "❄ Пришла зима — холодно и голодно, берегите дрова.")
        elif prev == "зима":
            w.cold = False
            w.add_news(clock, "Зима отступила — пришла весна, стало легче дышать.")
