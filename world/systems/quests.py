"""Модуль квестов: раз в день житель может повесить просьбу на острую нужду.
Подключаемый как любая другая система — мир и без него живёт."""
from __future__ import annotations
from ..engine import System
from .. import quests as Q


class QuestSystem(System):
    name = "quests"

    def on_day(self, w, clock, rng):
        Q.post_from_needs(w, clock, rng)
