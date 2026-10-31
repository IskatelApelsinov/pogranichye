"""Суточный расход, порча, ветшание, голодная смерть. Раз в сутки."""
from __future__ import annotations
from .. import tuning as T
from ..engine import System
from ..memory import record_death


class ConsumptionSystem(System):
    name = "consumption"

    def on_day(self, w, clock, rng):
        for ch in w.living():
            if w.store["food"] >= T.EAT_PER_DAY:
                w.store["food"] -= T.EAT_PER_DAY
            else:
                ch.hunger = min(100.0, ch.hunger + 40)
            w.store["wood"] = max(0.0, w.store["wood"] - T.FIREWOOD_PER_DAY)
        w.store["food"] *= T.FOOD_SPOIL
        w.store["raw"] *= T.RAW_SPOIL
        if "mead" in w.store:
            w.store["mead"] *= T.MEAD_SPOIL
        w.house_integrity = max(0.0, w.house_integrity - T.HOUSE_DECAY)
        for ch in w.living():
            if ch.hunger >= 100 and rng.random() < T.STARVE_DEATH_CHANCE:
                ch.alive = False
                record_death(w, ch, "не пережил голод", clock)
