"""Данные мира — чистые сущности, без логики систем.
Производные величины (здоровье, нужды) считаются здесь, потому что это
свойства самих данных, а не поведение какой-то системы."""

from __future__ import annotations
from dataclasses import dataclass, field
from . import tuning as T


@dataclass
class Character:
    """Единая сущность-агент. NPC и игрок — одно и то же тело; разница лишь в
    driver: 'ai' (решает utility AI) или 'player' (решает игрок через command).
    Любого жителя можно «вселить» игроком — мир, где каждый может быть игроком."""
    name: str
    role: str
    hunger: float = 0.0
    fatigue: float = 0.0
    social: float = 0.0           # A-LIFE: тяга к людям (гаснет в таверне)
    ambition: float = 0.0         # A-LIFE: жажда роста/доли (гонит учиться и уходить)
    dwelling: float = 0.0         # A-LIFE: обустроенность своего дома (растёт трудом, гасит тягу уйти)
    morale: float = 60.0          # настрой (0..100): растёт от отдыха/медовухи/дел, падает от нужды
    health: float = 100.0         # телесное здоровье (0..100): точит голод/усталость/холод/раны
    wounds: dict = field(default_factory=dict)   # раны по частям тела (world.body): часть -> урон; пусто = цел
    bleeding: float = 0.0         # кровопотеря (world.body): точит здоровье, пока не остановлена
    # ── дар и психика (world.magic / world.psyche) ──
    gift: float = 0.0             # предрасположенность к плетению (бросок при рождении); >порога = может учиться магии
    mana: float = 0.0             # запас силы для плетений («чем действуешь»)
    mana_max: float = 0.0         # предел маны (у немагов 0)
    psyche: float = 0.0           # душевный надлом/стресс (0..100): растёт от смерти, отката; мешает колдовать и трезво решать
    trait: str = ""               # черта характера (world.traits); '' — обычный
    skills: dict = field(default_factory=dict)
    alive: bool = True
    last_act: str = "—"
    driver: str = "ai"            # 'ai' | 'player'
    command: str = ""             # желаемое действие (игрок или план AI): chop/fish/cook/…
    renowned: bool = False        # уже прославился мастером (чтобы не повторять в хронике)
    # ── демография (у каждого есть пол, возраст и предки — мир из людей, а не юнитов) ──
    sex: str = "м"                # 'м' | 'ж'
    age: float = 20.0             # возраст в годах (растёт со временем)
    father: str = "—"             # имя отца
    mother: str = "—"             # имя матери
    surname: str = ""             # фамилия (родовая) — различать одинаковые имена
    nick: str = ""                # прозвище (от черты/внешности/ремесла)
    # ── экономика игрока (у NPC по умолчанию пусто; каждый может быть игроком) ──
    coins: float = 0.0
    favors: dict = field(default_factory=dict)   # имя NPC -> сколько он тебе должен (услуги/бартер)
    debts: dict = field(default_factory=dict)    # имя -> сколько монет ты должен ему (долги)
    home: str = "none"            # 'none' | 'rented' | 'owned'
    home_comfort: float = 0.0     # обустройство жилья (0..100) — лучше отдых/настрой
    inventory: list = field(default_factory=list)  # предметы при себе (id из items)
    storage: list = field(default_factory=list)    # склад в доме (id из items)
    cargo: dict = field(default_factory=dict)       # припасы при себе для торга (караван)
    weapon: str = ""              # надетое оружие (id из items) — помогает на охоте/рыбалке
    # одежда по слотам (как в толстом клиенте: голова/тело/верх/ноги + оберег)
    head: str = ""
    body: str = ""
    outfit: str = ""              # верхняя одежда (кафтан/шуба) — исторически «outfit»
    feet: str = ""
    trinket: str = ""             # оберег/перстень
    afk: bool = False             # офлайн-игрок под AI: пользуется предметами, только прибавляет
    priority: str = "balanced"    # в AI-режиме: work | balanced | rest | full_rest
    reputation: float = 0.0       # добрая слава: растёт от дел, улучшает цены и открывает выбор
    origin: str = ""              # предыстория персонажа (местный / странник)
    heir_name: str = ""           # имя наследника (если заведена семья) — род продолжится
    plot: list = field(default_factory=list)        # постройки на своём участке
    known: dict = field(default_factory=dict)       # туман войны: индекс деревни -> 0/1/2 (нет/слышал/был)
    journal: list = field(default_factory=list)     # дневник занятий в отсутствие игрока
    deliveries: list = field(default_factory=list)  # взятые поклажи-доставки (цепочки квестов)

    def skill(self, name: str) -> float:
        return self.skills.get(name, 0.0)

    def train(self, name: str, amount: float) -> None:
        cur = self.skills.get(name, 0.0)
        self.skills[name] = cur + amount / (1.0 + cur / 50.0)

    def v(self, male: str, female: str) -> str:
        """Согласование рода в тексте: ch.v('ушёл','ушла')."""
        return female if self.sex == "ж" else male

    def full_name(self) -> str:
        """Имя Фамилия «Прозвище» — чтобы отличать одинаковые имена."""
        parts = [self.name]
        if self.surname:
            parts.append(self.surname)
        s = " ".join(parts)
        if self.nick:
            s += f" «{self.nick}»"
        return s


@dataclass
class WorldClock:
    total_hours: int = 0

    @property
    def day(self) -> int:
        return self.total_hours // T.HOURS_PER_DAY + 1

    @property
    def hour(self) -> int:
        return self.total_hours % T.HOURS_PER_DAY


@dataclass
class Settlement:
    name: str
    residents: list = field(default_factory=list)
    store: dict = field(default_factory=lambda: {"food": 0.0, "wood": 0.0,
                                                 "raw": 0.0, "tools": 0.0,
                                                 "honey": 0.0, "mead": 0.0})
    tavern: bool = True                               # есть ли место сбора (таверна)
    keeper: str = ""                                  # корчмарь (кому платят за стол, кто помнит долг)
    tavern_tonight: list = field(default_factory=list)  # кто сейчас в таверне (со-присутствие)
    market: float = 1.0                               # ценовой множитель рынка (торг между деревнями)
    x: float = 0.0                                    # положение на карте региона (для путей)
    y: float = 0.0
    cold: bool = False                                # сейчас холодная волна (ставит WeatherSystem)
    season: str = "весна"                             # сезон (ставит SeasonSystem)
    weather: str = "ясно"                             # погода дня (ставит WeatherSystem)
    axe_wear: float = 0.0
    house_integrity: float = 100.0
    wares: dict = field(default_factory=dict)         # лавка: {item_id: число} — товары на продажу (конечны)
    news: list = field(default_factory=list)
    departures: list = field(default_factory=list)   # ушедшие — мир направит их дальше
    temperature: float = 101.0                        # активность: 101 горячо, <0 холодно
    # ── архивное (холодное) состояние: жители выгружены из RAM ──
    archived: bool = False
    archived_pop: int = 0                             # население в архиве (числом)
    pending_arrivals: list = field(default_factory=list)  # пришли, пока было холодным
    # ── ДОЛГАЯ ПАМЯТЬ (мир помнит): переживает смерть жителей и архивацию ──
    chronicle: list = field(default_factory=list)     # легенды/память о жизнях и смертях
    graves: int = 0                                   # сколько похоронено здесь

    def living(self) -> list:
        return [c for c in self.residents if c.alive]

    def by_name(self, name: str):
        for c in self.residents:
            if c.name == name:
                return c
        return None

    def pop(self) -> int:
        """Население независимо от режима: в горячем — живые тела, в холодном — число из архива."""
        if self.archived:
            return self.archived_pop + len(self.pending_arrivals)
        return len(self.living())

    def food_days(self) -> float:
        p = max(1, self.pop())
        return self.store["food"] / (p * T.EAT_PER_DAY)

    def avg_mood(self) -> float:
        if self.archived:
            # в архиве настрой приближаем по сытости (тел в RAM нет)
            return 80.0 if self.store["food"] > 0 else 20.0
        liv = self.living()
        if not liv:
            return 0.0
        m = sum(100 - 0.5 * c.hunger - 0.3 * c.fatigue for c in liv) / len(liv)
        return max(0.0, min(100.0, m))

    def health(self) -> float:
        if self.pop() == 0:
            return -1.0
        food = min(100.0, self.food_days() * 20.0)
        h = 0.5 * food + 0.3 * self.house_integrity + 0.2 * self.avg_mood()
        if self.food_days() < 0.5 and self.house_integrity < 20:
            return -1.0
        return round(max(-1.0, min(100.0, h)), 1)

    def needs(self) -> dict:
        pop = max(1, self.pop())
        return {
            "food": max(0.0, 100.0 - self.food_days() * 20.0),
            "wood": max(0.0, 100.0 - self.store["wood"] / (pop * T.FIREWOOD_PER_DAY) * 20.0),
            "repair": max(0.0, 100.0 - self.house_integrity),
            "tool": 100.0 if self.axe_wear >= T.AXE_BROKEN_AT else 0.0,
        }

    def add_news(self, clock: WorldClock, text: str) -> None:
        # текучие слухи — живут недолго
        self.news.append(f"День {clock.day}: {text}")
        if len(self.news) > 60:
            self.news = self.news[-60:]

    def add_chronicle(self, clock: WorldClock, text: str) -> None:
        # долгая память — живёт долго, переживает смерти и архивацию
        year = clock.day // 365 + 1
        self.chronicle.append(f"Год {year}, день {clock.day}: {text}")
        if len(self.chronicle) > 200:
            self.chronicle = self.chronicle[-200:]
