"""Пакет мира. Сборка движка с набором модулей по умолчанию.

Добавить новый модуль в игру = написать System в world/systems/ и дописать его
в build_engine (или вызвать engine.register() снаружи). Ядро не трогается.
"""
from __future__ import annotations
import random
from .model import Character, Settlement, WorldClock
from .engine import Engine, System
from . import content
from . import names as _names
from . import traits as _traits
from . import items as _items
from . import economy as _economy
from .persistence import save_world, load_world
from .systems import (NeedsSystem, WorkSystem, TavernSystem, ConsumptionSystem,
                      EventsSystem, PopulationSystem, WeatherSystem, QuestSystem,
                      SeasonSystem, AlifeSystem)

__all__ = ["Character", "Settlement", "WorldClock", "Engine", "System",
           "make_world", "build_engine", "save_world", "load_world"]


def make_world(name: str | None = None, demo_seed: int = 1) -> Settlement:
    s = Settlement(name=name or content.SETTLEMENT_NAME)
    s.store.update(content.STARTING_STORE)
    s.tavern = content.SETTLEMENT_HAS_TAVERN
    rng = random.Random(demo_seed)
    for cname, role in content.STARTING_ROSTER:
        sex = _names.sex_of(cname)
        father, mother = _names.random_parents(rng)
        age = round(rng.uniform(18, 55), 1)
        surname, nick = _names.identity(sex, role, rng)
        ch = Character(name=cname, role=role, sex=sex,
                       age=age, father=father, mother=mother,
                       surname=surname, nick=nick,
                       trait=_traits.assign(rng),
                       coins=round(rng.uniform(3, 25), 1))   # у жителей есть немного денег
        _items.outfit_for_role(ch, rng)                      # одежда/орудие по ремеслу
        s.residents.append(ch)
    if s.tavern and s.residents:                      # назначим корчмаря
        keeper = s.residents[-1]
        keeper.role = "tavernkeeper"
        s.keeper = keeper.name
    # сделаем одного-двух ткачами — чтобы в лавке была одежда (ремесло → товар)
    idlers = [c for c in s.residents if c.role == "idler"]
    for c in idlers[:2]:
        c.role = "weaver"
    _economy.seed_wares(s, rng)                        # начальный ассортимент лавки от мастеров
    return s


# порядок важен: нужды → труд → таверна (за час); расход → события/население (за сутки)
DEFAULT_SYSTEMS = [NeedsSystem, WorkSystem, TavernSystem, ConsumptionSystem,
                   EventsSystem, PopulationSystem, WeatherSystem, QuestSystem,
                   SeasonSystem, AlifeSystem]


def build_engine(world: Settlement, clock: WorldClock, seed: int = 1,
                 systems=None) -> Engine:
    """Собрать движок. systems=None → набор по умолчанию. Передай свой список —
    получишь другой мир (например без погоды, или с будущим модулем магии)."""
    eng = Engine(world, clock, seed)
    for S in (systems if systems is not None else DEFAULT_SYSTEMS):
        eng.register(S())
    return eng
