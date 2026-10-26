"""
Регион — мир из многих поселений под одними часами. Это фундамент Stage 2 и
механизм масштабирования:

  * ГОРЯЧИЕ поселения (рядом игрок/событие) считаются детально, почасово — полный
    движок систем.
  * ХОЛОДНЫЕ считаются дёшево — один суточный offline-шаг (world/offline.py).
  * МИГРАЦИЯ: ушедшие из одного поселения приходят в другое (под ёмкость) —
    население сохраняется и течёт по миру, а не исчезает.

Температура (активность) поселения: 101 — игрок прямо здесь; >0 — тёплое; <=0 —
холодное. Переключение режима по порогу. Так детально живёт лишь малая горячая
часть мира, а остальное дышит почти даром — это и есть рычаг масштабирования.
"""
from __future__ import annotations
import random
from . import tuning as T
from .model import Settlement, WorldClock, Character
from .engine import Engine
from .offline import offline_day
from .archive import archive_settlement, restore_settlement
from . import economy as _econ

_CARAVAN_NAMES = ["Гужевой обоз Третьяка", "Купеческий обоз Доброни",
                  "Коробейники Весняка", "Обоз Мирона Долгого", "Торг Белояра"]
from .systems import (NeedsSystem, WorkSystem, TavernSystem, ConsumptionSystem,
                      EventsSystem, PopulationSystem, WeatherSystem, QuestSystem,
                      SeasonSystem, AlifeSystem, NpcEconomySystem)

HOT_THRESHOLD = 50.0        # выше — детальный режим, ниже — offline
DEFAULT_SYSTEMS = [NeedsSystem, WorkSystem, TavernSystem, ConsumptionSystem,
                   EventsSystem, PopulationSystem, WeatherSystem, QuestSystem,
                   SeasonSystem, AlifeSystem, NpcEconomySystem]


def _fresh_systems():
    return [S() for S in DEFAULT_SYSTEMS]


# несколько деревень на карте: имя, рынок (ценовой множитель), координаты.
# 10 поселений — чтобы гонять LoD/оптимизацию на реальном числе и видеть караваны.
REGION_PLAN = [
    ("Тихий Брод", 1.00,  0.0,  0.0),    # родная — средние цены
    ("Светлояр",   1.35,  7.0,  2.0),    # дорогая — тут выгодно продавать
    ("Кривичи",    0.75,  3.0,  8.0),    # дешёвая — тут выгодно закупаться
    ("Залесье",    1.10, 10.0,  9.0),
    ("Отрадное",   0.90,  2.0, 13.0),
    ("Белогорье",  1.25, 15.0,  5.0),    # дальняя дорогая
    ("Медвежий Угол", 0.80, 13.0, 15.0), # глухая дешёвая
    ("Соловьёво",  1.05,  6.0, 18.0),
    ("Горелое",    0.95, 18.0, 12.0),
    ("Усть-Ворон", 1.15, 20.0, 20.0),    # край мира — редкий товар, высокие цены
]


def _build_settlement(gi: int, seed: int):
    from . import make_world
    name, market, x, y = REGION_PLAN[gi]
    s = make_world(name=name, demo_seed=seed + gi)
    s.market, s.x, s.y = market, x, y
    return s


def make_shards(groups, seed: int = 1) -> list:
    """Собрать мир из нескольких ШАРДОВ. groups — список групп глобальных индексов
    REGION_PLAN, по группе на шард (напр. [[0,1],[2]] — два шарда). Каждый шард —
    самостоятельный Region со своими часами; тикают в ногу из mp.World. Так мир
    делится на части, которые можно считать (а в бою — и держать) по отдельности."""
    shards = []
    for sid, group in enumerate(groups):
        setts = [_build_settlement(gi, seed) for gi in group]
        reg = Region(setts, seed=seed + 100 + sid)
        reg.set_hot(range(len(setts)))
        shards.append(reg)
    return shards


def make_region(n: int = 3, seed: int = 1) -> "Region":
    """Собрать регион из n деревень (динамическое число). Население и демография —
    через make_world, так что в каждой деревне живые люди с полом/возрастом/роднёй."""
    from . import make_world
    n = max(1, min(n, len(REGION_PLAN)))
    setts = []
    for i in range(n):
        name, market, x, y = REGION_PLAN[i]
        s = make_world(name=name, demo_seed=seed + i)
        s.market, s.x, s.y = market, x, y
        setts.append(s)
    reg = Region(setts, seed=seed)
    reg.set_hot(range(n))        # в живом клиенте держим все горячими — проверяем тики
    return reg


class Region:
    def __init__(self, settlements: list[Settlement], seed: int = 1):
        self.settlements = settlements
        self.clock = WorldClock()
        self.rng = random.Random(seed)
        # детальные движки держим только для горячих поселений (ленивое создание)
        self._engines: dict[int, Engine] = {}
        self.wanderers: list[Character] = []     # в пути между поселениями (не теряются)
        # караваны-торговцы ходят между деревнями шарда в реальном времени
        self.caravans: list = []
        if len(settlements) >= 2:
            n_cav = 1 + (len(settlements) >= 4)
            for k in range(n_cav):
                nm = _CARAVAN_NAMES[(seed + k) % len(_CARAVAN_NAMES)]
                self.caravans.append(_econ.new_caravan(nm, settlements, self.clock, self.rng))

    # ── температура: кто горячий. Смена режима архивирует/восстанавливает тела ──
    def set_hot(self, indices) -> None:
        hot = set(indices)
        for i, s in enumerate(self.settlements):
            s.temperature = 101.0 if i in hot else -1.0
            if i in hot and s.archived:
                restore_settlement(s)            # холодное → горячее: вернуть тела в RAM
            elif i not in hot and not s.archived:
                archive_settlement(s)            # горячее → холодное: выгрузить тела из RAM

    def _engine_for(self, i: int) -> Engine:
        eng = self._engines.get(i)
        if eng is None:
            eng = Engine(self.settlements[i], self.clock, seed=0)
            eng.rng = self.rng                 # общий поток случайности региона
            for sys in _fresh_systems():
                eng.register(sys)
            self._engines[i] = eng
        return eng

    # ── шаг времени всего региона ──
    def step_hour(self) -> None:
        hot = [i for i, s in enumerate(self.settlements) if s.temperature >= HOT_THRESHOLD]
        # горячие — детально, почасово
        for i in hot:
            self._run_hot_hour(i)
        # часы двигаем один раз на весь регион
        self.clock.total_hours += 1
        # караваны-торговцы ходят каждый час (товары текут по миру в реальном времени)
        for cav in self.caravans:
            news = _econ.step_caravan(cav, self.settlements, self.clock, self.rng)
            if news and 0 <= cav.at < len(self.settlements):
                self.settlements[cav.at].add_news(self.clock, news)
        if self.clock.hour == 0:
            # сперва расселяем вчерашних путников (миграция в приоритете над рождением)
            self._migrate()
            # мастера пополняют лавки своими изделиями
            for s in self.settlements:
                _econ.restock_artisans(s, self.rng)
            # затем холодные — дёшево, раз в сутки (могут дать новых путников)
            for i, s in enumerate(self.settlements):
                if s.temperature < HOT_THRESHOLD:
                    offline_day(s, self.clock, self.rng)

    def _run_hot_hour(self, i: int) -> None:
        eng = self._engine_for(i)
        for sys in eng.systems:
            sys.on_hour(eng.world, self.clock, self.rng)
        # суточные хуки горячего поселения — на стыке суток (clock ещё не сдвинут)
        if (self.clock.total_hours + 1) % T.HOURS_PER_DAY == 0:
            for sys in eng.systems:
                sys.on_day(eng.world, self.clock, self.rng)

    # ── миграция: ушедшие → странники → в поселение под ёмкость (никто не теряется) ──
    def _migrate(self) -> None:
        # собрать вчерашних ушедших в общий пул странников
        for s in self.settlements:
            if s.departures:
                self.wanderers.extend(s.departures)
                s.departures = []
        if not self.wanderers:
            return
        still_walking: list[Character] = []
        for ch in self.wanderers:
            dests = [s for s in self.settlements if s.pop() < T.SETTLEMENT_CAPACITY]
            if not dests:
                still_walking.append(ch)         # мест нет — идёт дальше, не исчезает
                continue
            dest = self.rng.choice(dests)
            ch.role = "idler"
            if dest.archived:
                dest.pending_arrivals.append(ch) # придёт в тела при разогреве
            else:
                dest.residents.append(ch)
            dest.add_news(self.clock, f"В поселение пришёл новый житель — {ch.name}.")
        self.wanderers = still_walking

    def dist_hours(self, i: int, j: int) -> int:
        """Время в пути между поселениями (часы) по карте региона."""
        a, b = self.settlements[i], self.settlements[j]
        d = ((a.x - b.x) ** 2 + (a.y - b.y) ** 2) ** 0.5
        return max(2, int(round(d)))

    # ── сводка ──
    def totals(self) -> dict:
        return {"settlements": len(self.settlements),
                "population": sum(s.pop() for s in self.settlements),
                "hot": sum(1 for s in self.settlements if s.temperature >= HOT_THRESHOLD),
                "bodies_in_RAM": sum(len(s.residents) for s in self.settlements),
                "avg_health": round(sum(s.health() for s in self.settlements) / len(self.settlements), 1)}
