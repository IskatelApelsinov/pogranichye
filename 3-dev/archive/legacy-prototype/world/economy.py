"""Экономика вещей: откуда в лавке товары.

Товары не берутся из воздуха — их ПРОИЗВОДЯТ местные мастера (кузнец куёт оружие,
ткачи шьют одежду), а КАРАВАНЫ развозят их между деревнями ради выгоды (купить там,
где дёшево, продать там, где дорого). Лавка деревни — конечный запас `wares`
{item_id: число}. Купил — стало меньше; мастера и караваны пополняют.

Всё детерминировано от переданного rng — мир воспроизводим.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from . import items as I

# что производит ремесло (идёт в лавочный запас деревни)
ROLE_PRODUCES = {
    "smith":      ["knife", "axe", "spear", "sword"],
    "woodcutter": ["axe"],
    "hunter":     ["bow", "trap"],
    "herbalist":  ["tonic", "balm", "cloak"],
    "cook":       ["shirt", "hat"],          # при кухне и прядут, и шьют по мелочи
    "fisher":     ["boots", "trap"],
    "tavernkeeper": ["hat", "lapti"],
    "weaver":     ["shirt", "vest", "coat", "hat", "boots"],
    "trader":     ["ring", "amulet", "ushanka", "fur"],
}
WARE_CAP = 6                     # не больше стольких одинаковых в лавке


def seed_wares(s, rng) -> None:
    """Начальный ассортимент лавки — из того, что умеют делать местные жители."""
    if s.wares:
        return
    for c in s.living():
        for iid in ROLE_PRODUCES.get(c.role, []):
            if rng.random() < 0.5:
                s.wares[iid] = min(WARE_CAP, s.wares.get(iid, 0) + 1)
    # базовая одежда почти всегда есть
    for iid in ("shirt", "lapti", "hat"):
        s.wares[iid] = max(s.wares.get(iid, 0), 1 + rng.randint(0, 2))


def restock_artisans(s, rng) -> None:
    """Раз в сутки мастера подкладывают в лавку немного своих изделий (с потолком)."""
    for c in s.living():
        prod = ROLE_PRODUCES.get(c.role)
        if not prod:
            continue
        # шанс и качество растут с мастерством ремесла
        if rng.random() < 0.35:
            iid = rng.choice(prod)
            if s.wares.get(iid, 0) < WARE_CAP:
                s.wares[iid] = s.wares.get(iid, 0) + 1


def ware_price(s, item_id: str) -> int:
    """Цена товара в лавке с учётом рыночного множителя деревни."""
    it = I.CATALOG.get(item_id)
    if not it:
        return 0
    return max(1, round(it["price"] * getattr(s, "market", 1.0)))


def buy_ware(ch, s, item_id: str) -> dict:
    """Купить товар из лавки деревни: есть запас → монеты → предмет в котомку."""
    if s.wares.get(item_id, 0) <= 0:
        return {"ok": False, "msg": "этого в лавке сейчас нет — жди мастера или каравана"}
    price = ware_price(s, item_id)
    if ch.coins < price:
        return {"ok": False, "msg": f"нужно {price}⌾, у тебя {ch.coins:.0f}"}
    ch.coins -= price
    s.wares[item_id] -= 1
    if s.wares[item_id] <= 0:
        s.wares.pop(item_id, None)
    ch.inventory.append(item_id)
    return {"ok": True, "msg": f"Куплено: {I.label(item_id)} (-{price}⌾).",
            "coins": round(ch.coins, 1)}


def wares_list(s) -> list:
    """Ассортимент лавки для клиента: id, имя, иконка, цена, сколько в наличии."""
    out = []
    for iid, n in sorted(s.wares.items()):
        it = I.CATALOG.get(iid)
        if not it or n <= 0:
            continue
        out.append({"id": iid, "name": it["name"], "icon": it["icon"],
                    "price": ware_price(s, iid), "count": n, "slot": it["slot"]})
    return out


# ── КАРАВАН: торговец, что ходит между деревнями деревнями в реальном времени ──
@dataclass
class Caravan:
    name: str
    at: int                      # локальный индекс деревни в шарде (где стоит), или -1 в пути
    dest: int                    # куда идёт
    arrive: int                  # час прибытия (по часам региона)
    coins: float = 120.0
    cargo: dict = field(default_factory=dict)   # {item_id: count}

    def to_dict(self) -> dict:
        return dict(name=self.name, at=self.at, dest=self.dest, arrive=self.arrive,
                    coins=round(self.coins, 1), cargo=dict(self.cargo))


def _dist_hours(a, b) -> int:
    dx = (a.x - b.x); dy = (a.y - b.y)
    return max(3, int(round((dx * dx + dy * dy) ** 0.5)))


def new_caravan(name, settlements, clock, rng) -> Caravan:
    start = rng.randrange(len(settlements))
    cav = Caravan(name=name, at=start, dest=start, arrive=clock.total_hours + rng.randint(2, 6))
    # стартовый груз — немного ходового товара
    for iid in ("shirt", "hat", "knife", "boots"):
        cav.cargo[iid] = rng.randint(1, 3)
    return cav


def step_caravan(cav, settlements, clock, rng) -> str | None:
    """Шаг каравана (раз в час). В пути — ждём; прибыл — торгуем и выбираем новую цель.
    Возвращает текст новости для деревни прибытия (или None)."""
    if clock.total_hours < cav.arrive:
        return None
    news = None
    if cav.at >= 0:
        # прибыл: сначала продаёт (пополняет лавку), потом закупается подешевле
        s = settlements[cav.at]
        sold = _caravan_sell(cav, s)
        bought = _caravan_buy(cav, s, rng)
        bits = []
        if sold:
            bits.append(f"привёз {sold} товаров")
        if bought:
            bits.append(f"скупил {bought} на продажу")
        news = f"Пришёл караван {cav.name}" + (": " + ", ".join(bits) if bits else ".")
    # выбрать новую цель: туда, где рынок дороже (выгоднее продать), иногда случайно
    cur = cav.at if cav.at >= 0 else cav.dest
    others = [i for i in range(len(settlements)) if i != cur]
    if not others:
        cav.arrive = clock.total_hours + 6
        return news
    if rng.random() < 0.7:
        dest = max(others, key=lambda i: getattr(settlements[i], "market", 1.0))
    else:
        dest = rng.choice(others)
    travel = _dist_hours(settlements[cur], settlements[dest])
    cav.at = -1
    cav.dest = dest
    cav.arrive = clock.total_hours + travel
    return news


def _caravan_sell(cav, s) -> int:
    """Караван сбывает часть груза в лавку деревни, беря монеты по местной цене."""
    sold = 0
    for iid in list(cav.cargo.keys()):
        if s.wares.get(iid, 0) >= WARE_CAP:
            continue
        move = min(cav.cargo[iid], WARE_CAP - s.wares.get(iid, 0), 2)
        if move <= 0:
            continue
        s.wares[iid] = s.wares.get(iid, 0) + move
        cav.cargo[iid] -= move
        if cav.cargo[iid] <= 0:
            cav.cargo.pop(iid, None)
        cav.coins += ware_price(s, iid) * move * 0.6     # оптом дешевле розницы
        sold += move
    return sold


def _caravan_buy(cav, s, rng) -> int:
    """Караван скупает излишки лавки подешевле (там, где рынок низкий), чтобы увезти."""
    bought = 0
    if getattr(s, "market", 1.0) > 1.05:
        return 0                                         # где дорого — не закупаемся
    for iid in list(s.wares.keys()):
        if s.wares[iid] <= 1:
            continue
        take = min(2, s.wares[iid] - 1)
        cost = ware_price(s, iid) * take * 0.5
        if cav.coins < cost:
            continue
        s.wares[iid] -= take
        cav.cargo[iid] = cav.cargo.get(iid, 0) + take
        cav.coins -= cost
        bought += take
    return bought
