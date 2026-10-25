"""Нужды тела: голод, усталость, сон, еда. Каждый час.
Раз в сутки — пересчёт настроя и здоровья от текущих нужд (та же формула, что у игрока)."""
from __future__ import annotations
from .. import tuning as T
from .. import player as _player
from ..engine import System


class NeedsSystem(System):
    name = "needs"

    def on_hour(self, w, clock, rng):
        hour = clock.hour
        for ch in w.living():
            ch.hunger = min(100.0, ch.hunger + T.HUNGER_PER_HOUR)
            if hour in T.SLEEP_HOURS:
                ch.fatigue = max(0.0, ch.fatigue - T.SLEEP_RECOVER)
                ch.last_act = "спит"
                continue
            ch.fatigue = min(100.0, ch.fatigue + T.SLEEP_PER_HOUR)
            # поесть, если голоден и есть еда
            if ch.hunger > 50 and w.store["food"] >= 1:
                w.store["food"] -= 1
                ch.hunger = max(0.0, ch.hunger - 60)

    def on_day(self, w, clock, rng):
        # настрой и здоровье жителей дрейфуют от нужд (кров — лёгкий отдых)
        for ch in w.living():
            _player.update_condition(ch, rested=(getattr(ch, "home", "none") != "none"))
