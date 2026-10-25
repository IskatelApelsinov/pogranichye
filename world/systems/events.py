"""Редкие события с причиной, устами новостей. Раз в сутки — бросок.
(Рождения/эмиграция — не здесь, а в PopulationSystem: население — отдельная механика.)"""
from __future__ import annotations
from .. import tuning as T
from ..engine import System
from ..memory import record_death


class EventsSystem(System):
    name = "events"

    def on_day(self, w, clock, rng):
        if rng.random() > T.EVENT_CHANCE:
            return
        roll = rng.random()
        if roll < 0.25:
            w.store["raw"] *= 0.5
            w.add_news(clock, "Улов не задался — рыба ушла от берега.")
        elif roll < 0.45:
            w.house_integrity = max(0.0, w.house_integrity - rng.uniform(8, 20))
            w.add_news(clock, "Ночью занялся пожар в одном из домов. "
                              "Отбились, но починки прибавилось.")
        elif roll < 0.60:
            # смертельная опасность: зверь или разбойники (смерть всегда близко)
            liv = w.living()
            if liv:
                victim = rng.choice(liv)
                if rng.random() < 0.5:
                    victim.alive = False
                    record_death(w, victim, rng.choice(
                        ["не вернулся с охоты — задрал зверь",
                         "пал от руки разбойников на большаке"]), clock)
                else:
                    w.add_news(clock, "На дороге видели разбойников — ночью ходить боязно.")
        elif roll < 0.85:
            w.store["food"] += rng.uniform(15, 35)
            w.add_news(clock, "Забрёл бродячий торговец — выменяли у него припасов.")
        else:
            w.axe_wear = min(w.axe_wear, 5.0)
            w.add_news(clock, "Старый мастер заглянул в кузню и подправил инструмент.")
