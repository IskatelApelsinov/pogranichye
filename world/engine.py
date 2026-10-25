"""Ядро мира. Знает ТОЛЬКО о времени и о списке систем — и ничего о конкретных
механиках. Любая механика (экономика, нужды, события, в будущем магия/бой/торговля)
— это System, которую подключают в движок. Добавить модуль = написать System и
зарегистрировать его; ядро при этом не меняется.

System — это объект с необязательными хуками:
    on_hour(world, clock, rng)   — каждый игровой час
    on_day(world, clock, rng)    — в начале новых суток
Порядок систем = порядок вызова (важно: нужды до работы, расход до событий).
"""

from __future__ import annotations
import random
from . import tuning as T
from .model import Settlement, WorldClock


class System:
    """База модуля. Переопредели нужные хуки. Имя — для отладки и включения/выключения."""
    name: str = "system"
    def on_hour(self, world: Settlement, clock: WorldClock, rng: random.Random) -> None: ...
    def on_day(self, world: Settlement, clock: WorldClock, rng: random.Random) -> None: ...


class Engine:
    def __init__(self, world: Settlement, clock: WorldClock, seed: int = 1):
        self.world = world
        self.clock = clock
        self.rng = random.Random(seed)
        self.systems: list[System] = []

    def register(self, system: System) -> "Engine":
        """Подключить модуль. Возвращает self — можно цепочкой."""
        self.systems.append(system)
        return self

    def unregister(self, name: str) -> None:
        """Отключить модуль по имени — модульность работает в обе стороны."""
        self.systems = [s for s in self.systems if s.name != name]

    def step_hour(self) -> None:
        for s in self.systems:
            s.on_hour(self.world, self.clock, self.rng)
        self.clock.total_hours += 1
        if self.clock.hour == 0:                 # наступили новые сутки
            for s in self.systems:
                s.on_day(self.world, self.clock, self.rng)

    def step_days(self, days: int) -> None:
        for _ in range(days * T.HOURS_PER_DAY):
            self.step_hour()
