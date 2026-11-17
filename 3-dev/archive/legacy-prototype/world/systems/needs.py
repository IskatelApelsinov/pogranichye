"""Нужды тела: голод, усталость, сон, еда. Каждый час.
Раз в сутки — пересчёт настроя и здоровья от текущих нужд (та же формула, что у игрока)."""
from __future__ import annotations
from .. import tuning as T
from .. import player as _player
from .. import body as _body
from .. import magic as _magic
from ..engine import System
from ..memory import record_death


class NeedsSystem(System):
    name = "needs"

    def on_hour(self, w, clock, rng):
        hour = clock.hour
        sleeping_now = hour in T.SLEEP_HOURS
        for ch in w.living():
            ch.hunger = min(100.0, ch.hunger + T.HUNGER_PER_HOUR)
            # раны затягиваются, кровь точит жизнь (world.body) — и во сне, и наяву
            if ch.wounds or _body.bleeding(ch) > 0:
                resting = sleeping_now or (getattr(ch, "home", "none") != "none" and ch.fatigue < 45)
                _body.heal_tick(ch, rng, hours=1.0, resting=resting)
                if _body.bleeding(ch) > 0 and ch.health <= 0 and rng.random() < 0.25:
                    ch.alive = False
                    record_death(w, ch, "истёк кровью от ран", clock)
                    continue
            # мана мага восполняется, душевный надлом понемногу отпускает (world.magic/психика)
            if getattr(ch, "mana_max", 0.0) > 0:
                _magic.regen(ch, 1.0)
            if getattr(ch, "psyche", 0.0) > 0:
                ch.psyche = max(0.0, ch.psyche - (1.2 if sleeping_now else 0.6))
            if sleeping_now:
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
