"""
Слой игрока поверх живого мира. Игрок — это обычный агент (driver='player'),
но у него есть монеты, услуги-долги (бартер) и жильё. Все действия идут отсюда.

Тонкий клиент — казуальный: активность = мини-игра, её «качество» (0..1) приходит
с клиента и масштабирует награду вместе с навыком и щепоткой удачи (воля богов).
Труд игрока идёт в ОБЩИЙ котёл деревни (его вклад миру настоящий), а игрок получает
плату монетами и растит навык.
"""
from __future__ import annotations
import random
from . import items as _items
from . import traits as _traits

# активность: ресурс в общий котёл, навык, плата игроку, тип мини-игры
ACTIVITIES = {
    "firewood": dict(resource="wood", skill="chopping", base=10, pay=1.8,
                     minigame="mash", label="Наколоть дров"),
    "garden":   dict(resource="food", skill="farming",  base=8,  pay=1.8,
                     minigame="sequence", label="Поработать на огороде"),
    "berries":  dict(resource="honey", skill="gathering", base=6, pay=1.2,
                     minigame="ripe", label="Собрать ягоды и мёд"),
    "fishing":  dict(resource="raw",  skill="fishing",  base=6,  pay=1.8,
                     minigame="hold", label="Порыбачить"),
    "hunting":  dict(resource="raw",  skill="hunting",  base=9,  pay=3,
                     minigame="targets", label="Сходить на охоту"),
    "build":    dict(resource="_house", skill="building", base=10, pay=2.4,
                     minigame="sequence", label="Помочь со стройкой"),
    # личная добыча: уходит в твой груз (не в котёл), под крафт
    "herbs":    dict(resource="herb", skill="herbalism", base=5, pay=0,
                     minigame="ripe", label="Собрать травы", personal=True),
    "mine":     dict(resource="ore",  skill="mining",    base=4, pay=0,
                     minigame="timing", label="Добыть руду", personal=True),
}

COIN_SELL = {"wood": 0.3, "food": 0.6, "raw": 0.5, "honey": 0.7, "mead": 1.2}
# В деревне платят СРАЗУ редко — чаще остаются должны (услуга/долг). Поэтому монеты
# за работу выпадают лишь иногда; основной доход — через услуги, квесты, торг, бой.
PAY_CHANCE = 0.35

RENT_PER_DAY = 2.0
HOUSE_PRICE = 120.0
FURNISH_STEP_COST = 15.0
FURNISH_STEP = 15.0


def update_condition(ch, work_done: bool = False, rested: bool = False,
                     cheered: float = 0.0) -> None:
    """Пересчёт настроя и здоровья от текущих нужд. Вызывается после дел, отдыха,
    медовухи и в суточном тике. Границы 0..100. Черта 'крепкий' ускоряет исцеление."""
    hunger = getattr(ch, "hunger", 0.0)
    fatigue = getattr(ch, "fatigue", 0.0)
    # целевой настрой: сыт и бодр → бодрее духом; голод/усталость/нужда давят
    target = 85.0 - hunger * 0.4 - fatigue * 0.3
    if ch.home != "none":
        target += 5.0
    target += ch.home_comfort * 0.05
    ch.morale += (target - ch.morale) * 0.30
    if work_done:
        ch.morale += 2.0
    if rested:
        ch.morale += 4.0
    ch.morale = max(0.0, min(100.0, ch.morale + cheered))
    # здоровье: при тяжёлой нужде убывает, иначе медленно восстанавливается
    heal_rate = _traits.mod(ch, "heal", 1.0)
    if hunger > 75 or fatigue > 85:
        ch.health -= (1.5 + (hunger - 75) * 0.06 + max(0.0, fatigue - 85) * 0.05)
    else:
        recover = (1.2 + ch.home_comfort * 0.02) * heal_rate
        if rested:
            recover += 2.0
        ch.health += recover
    ch.health = max(0.0, min(100.0, ch.health))


def do_activity(w, ch, activity: str, quality: float, rng: random.Random) -> dict:
    """Выполнить активность с качеством мини-игры 0..1. Возвращает сводку для клиента."""
    a = ACTIVITIES.get(activity)
    if not a:
        return {"ok": False, "msg": "нет такой активности"}
    quality = max(0.0, min(1.0, quality))
    # ловкость (черта) чуть поднимает эффективное качество мини-игры
    quality = min(1.0, quality + _traits.mod(ch, "quality", 0.0))
    skillv = ch.skill(a["skill"])
    # удача: черта «удачливый» + надетый оберег расширяют разброс вверх
    luck_top = 1.1 + _traits.mod(ch, "luck", 0.0) + _items.trinket_stat(ch, "luck")
    luck = rng.uniform(0.9, luck_top)
    # настрой: бодрый работает лучше, унылый — хуже (±10%)
    morale_f = 0.9 + 0.2 * (getattr(ch, "morale", 60.0) / 100.0)
    factor = (0.4 + 0.6 * quality) * (1.0 + skillv / 100.0) * luck * morale_f
    factor *= _traits.mod(ch, "work", 1.0)             # трудолюбие
    if a["resource"] in ("_house",) or activity == "build":
        factor *= _traits.mod(ch, "home", 1.0)         # хозяйственность — на стройку/дом
    # инструмент помогает: острога на рыбалке, топор на рубке дров
    extra = ""
    if activity in ("hunting", "fishing"):
        dmg = _items.weapon_dmg(ch)
        if dmg:
            wb = dmg * (0.05 if activity == "hunting" else 0.03)
            factor *= 1.0 + wb
            extra = f" ({_items.label(ch.weapon)} в деле: +{wb*100:.0f}%)"
    elif activity == "firewood" and ("axe" in ch.inventory or ch.weapon == "axe"):
        factor *= 1.25                                 # топор ускоряет рубку
        extra = " (топор в деле: +25%)"
    amount = a["base"] * factor
    pay = a["pay"] * factor
    paid = rng.random() < PAY_CHANCE * _traits.mod(ch, "pay", 1.0)   # бережливым платят чаще
    if not paid:
        pay = 0.0
    ch.train(a["skill"], 0.5 + quality)                # навык растёт от дела, больше за мастерство

    if a["resource"] == "_house":
        w.house_integrity = min(100.0, w.house_integrity + amount * 0.6)
        msg = f"Стройка продвинулась (+{amount*0.6:.0f} к целости домов)."
    elif a.get("personal"):
        key = a["resource"]
        ch.cargo[key] = ch.cargo.get(key, 0) + amount
        msg = f"В твой груз: +{amount:.0f} {key}."
    else:
        key = a["resource"]
        w.store[key] = w.store.get(key, 0) + amount
        msg = f"В общий котёл: +{amount:.0f} {key}."
    ch.coins += pay
    if not paid:
        extra += " · на общее благо (сочтёмся)"
    # усталость: в холод тяжелее, тёплая одежда это гасит; черты меняют утомляемость
    tire = 6.0
    if getattr(w, "cold", False):
        chill = max(0.0, 4.0 - _items.warmth(ch) * 0.5) * _traits.mod(ch, "cold", 1.0)
        tire += chill
        if _items.warmth(ch) > 0:
            extra += f" · {_items.label(ch.outfit)} греет"
        else:
            extra += " · холодно без одежды"
    tire *= _traits.mod(ch, "tire", 1.0)
    ch.fatigue = min(100.0, ch.fatigue + tire)
    # труд поднимает настрой (дело спорится), но сильная усталость/голод его точат
    update_condition(ch, work_done=True)
    return {"ok": True, "msg": msg + extra, "pay": round(pay, 1), "activity": activity,
            "skill": a["skill"], "skill_level": round(ch.skill(a["skill"]), 1),
            "coins": round(ch.coins, 1),
            "morale": round(ch.morale), "health": round(ch.health)}


def help_npc(w, ch, npc_name: str, quality: float, rng: random.Random) -> dict:
    """Помочь жителю (квест) — он остаётся тебе должен услугой (бартер встроен в мир)."""
    res = do_activity(w, ch, "build", quality, rng)
    ch.favors[npc_name] = ch.favors.get(npc_name, 0) + 1
    npc = w.by_name(npc_name)
    owes = npc.v("должен", "должна") if npc else "должен"
    res["msg"] = f"Ты помог — {npc_name} теперь тебе {owes} услугу."
    res["favor_with"] = npc_name
    res["favor_count"] = ch.favors[npc_name]
    return res


def call_favor(w, ch, npc_name: str, rng: random.Random) -> dict:
    """Призвать долг услугой: NPC отдаёт едой/монетами/помощью."""
    if ch.favors.get(npc_name, 0) <= 0:
        return {"ok": False, "msg": f"{npc_name} тебе ничего не должен."}
    ch.favors[npc_name] -= 1
    npc = w.by_name(npc_name)
    roll = rng.random()
    if roll < 0.5 and w.store.get("food", 0) >= 10:
        w.store["food"] -= 10
        # игрок уносит еду как личный запас → в монетном эквиваленте проще: даём монеты
        gain = 8.0
        ch.coins += gain
        gave = npc.v("отблагодарил", "отблагодарила") if npc else "отблагодарил"
        msg = f"{npc_name} {gave} припасами (≈{gain:.0f} монет)."
    else:
        gain = rng.uniform(5, 12)
        ch.coins += gain
        gave = npc.v("вернул", "вернула") if npc else "вернул"
        msg = f"{npc_name} {gave} долг монетами (+{gain:.0f})."
    return {"ok": True, "msg": msg, "coins": round(ch.coins, 1),
            "favor_count": ch.favors.get(npc_name, 0)}


def barter_sell(w, ch, resource: str, amount: float) -> dict:
    """Продать ресурс из общего котла за монеты (упрощённый рынок)."""
    price = COIN_SELL.get(resource)
    if price is None or w.store.get(resource, 0) < amount:
        return {"ok": False, "msg": "нечего продать"}
    w.store[resource] -= amount
    ch.coins += amount * price
    return {"ok": True, "msg": f"Продано {amount:.0f} {resource} за {amount*price:.0f} монет.",
            "coins": round(ch.coins, 1)}


# ── торговля караваном: купи дёшево в одной деревне, вези и продай дорого в другой ──
TRADE_BASE = {"food": 1.0, "wood": 0.5, "raw": 0.8, "honey": 1.2,
              "mead": 2.2, "tools": 3.0, "herb": 1.0, "ore": 1.8}
BUY_MARKUP = 1.15      # покупка у деревни чуть дороже базы
SELL_CUT = 0.9         # продажа деревне чуть дешевле базы (на разнице рынков и живёт навар)


def buy_price(w, res: str) -> float:
    return round(TRADE_BASE.get(res, 1.0) * w.market * BUY_MARKUP, 2)


def sell_price(w, res: str) -> float:
    return round(TRADE_BASE.get(res, 1.0) * w.market * SELL_CUT, 2)


def rep_discount(ch) -> float:
    """Добрая слава + надетый перстень дают скидку/надбавку в торге (до ~26%)."""
    return min(0.26, getattr(ch, "reputation", 0.0) / 400.0 + _items.trinket_stat(ch, "trade"))


def buy_goods(w, ch, res: str, amount: float) -> dict:
    """Купить припасы из котла деревни себе в груз (караван). Слава удешевляет."""
    if res not in TRADE_BASE:
        return {"ok": False, "msg": "этим не торгуют"}
    amount = min(amount, w.store.get(res, 0))
    if amount <= 0:
        return {"ok": False, "msg": f"у деревни нет {res}"}
    price = buy_price(w, res) * (1.0 - rep_discount(ch))
    cost = amount * price
    if ch.coins < cost:
        amount = ch.coins / price                   # сколько по карману
        cost = amount * price
    if amount < 1:
        return {"ok": False, "msg": "не хватает монет"}
    w.store[res] -= amount
    ch.cargo[res] = ch.cargo.get(res, 0) + amount
    ch.coins -= cost
    return {"ok": True, "msg": f"Куплено {amount:.0f} {res} за {cost:.0f}⌾.",
            "coins": round(ch.coins, 1)}


def sell_goods(w, ch, res: str, amount: float) -> dict:
    """Продать припасы из груза в котёл деревни по её ценам."""
    have = ch.cargo.get(res, 0)
    amount = min(amount, have)
    if amount <= 0:
        return {"ok": False, "msg": f"нет {res} в грузе"}
    gain = amount * sell_price(w, res) * (1.0 + rep_discount(ch))   # слава — выгоднее продажа
    ch.cargo[res] = have - amount
    if ch.cargo[res] <= 0.01:
        ch.cargo.pop(res, None)
    w.store[res] = w.store.get(res, 0) + amount
    ch.coins += gain
    return {"ok": True, "msg": f"Продано {amount:.0f} {res} за {gain:.0f}⌾.",
            "coins": round(ch.coins, 1)}


# ── жильё ──
def rent_room(ch) -> dict:
    if ch.home == "owned":
        return {"ok": False, "msg": "у тебя уже есть свой дом."}
    ch.home = "rented"
    return {"ok": True, "msg": f"Снял комнату (плата {RENT_PER_DAY:.0f} монет в день)."}


def buy_house(ch) -> dict:
    if ch.home == "owned":
        return {"ok": False, "msg": "дом уже твой."}
    if ch.coins < HOUSE_PRICE:
        return {"ok": False, "msg": f"нужно {HOUSE_PRICE:.0f} монет, у тебя {ch.coins:.0f}."}
    ch.coins -= HOUSE_PRICE
    ch.home = "owned"
    return {"ok": True, "msg": "Теперь у тебя свой дом!", "coins": round(ch.coins, 1)}


def furnish(ch) -> dict:
    if ch.home == "none":
        return {"ok": False, "msg": "сначала нужно жильё."}
    if ch.coins < FURNISH_STEP_COST:
        return {"ok": False, "msg": f"нужно {FURNISH_STEP_COST:.0f} монет."}
    ch.coins -= FURNISH_STEP_COST
    ch.home_comfort = min(100.0, ch.home_comfort + FURNISH_STEP)
    return {"ok": True, "msg": f"Обустроил жильё (уют {ch.home_comfort:.0f}).",
            "coins": round(ch.coins, 1), "comfort": ch.home_comfort}


# постройки на участке: стоят ресурсов (из груза), дают уют и пассивную пользу
PLOT = {
    "garden_bed": dict(name="Грядки",       icon="🌾", cost={"wood": 6},  comfort=5, daily={"food": 2}),
    "woodshed":   dict(name="Дровник",      icon="🪵", cost={"wood": 10}, comfort=4, winter=True),
    "bench":      dict(name="Лавка у дома",  icon="🪑", cost={"wood": 4},  comfort=6),
    "well":       dict(name="Колодец",       icon="🪣", cost={"wood": 8},  comfort=8),
    "beehive":    dict(name="Пасека",        icon="🐝", cost={"wood": 6},  comfort=3, daily={"honey": 2}),
}


_PLOT_CACHE = None


def plot_list() -> list:
    global _PLOT_CACHE
    if _PLOT_CACHE is None:
        _PLOT_CACHE = [dict(id=k, **v) for k, v in PLOT.items()]
    return _PLOT_CACHE


def build_structure(ch, sid: str) -> dict:
    """Возвести постройку на своём участке (нужен свой дом). Платится из груза."""
    p = PLOT.get(sid)
    if not p:
        return {"ok": False, "msg": "нет такой постройки"}
    if ch.home != "owned":
        return {"ok": False, "msg": "строить можно только на своём дворе (нужен свой дом)"}
    if sid in ch.plot:
        return {"ok": False, "msg": f"{p['name']} уже стоит"}
    miss = [f"{res} {amt}" for res, amt in p["cost"].items() if ch.cargo.get(res, 0) < amt]
    if miss:
        return {"ok": False, "msg": "не хватает: " + ", ".join(miss)}
    for res, amt in p["cost"].items():
        ch.cargo[res] -= amt
        if ch.cargo[res] <= 0:
            ch.cargo.pop(res, None)
    ch.plot.append(sid)
    ch.home_comfort = min(100.0, ch.home_comfort + p["comfort"])
    return {"ok": True, "msg": f"{p['icon']} {p['name']} возведена (+{p['comfort']} уюта).",
            "comfort": ch.home_comfort}


def player_day(ch, cold: bool = False) -> None:
    """Суточное для игрока: аренда, отдых по уюту, стужа, и пассив с построек."""
    if ch.home == "rented" and not getattr(ch, "afk", False):
        ch.coins = max(0.0, ch.coins - RENT_PER_DAY)
    if ch.home != "none":
        ch.fatigue = max(0.0, ch.fatigue - ch.home_comfort * 0.2)   # уютный дом лучше восстанавливает
    # пассив построек участка — каждый день немного припасов в груз
    has_woodshed = False
    for sid in getattr(ch, "plot", []):
        p = PLOT.get(sid)
        if not p:
            continue
        if p.get("winter"):
            has_woodshed = True
        for res, amt in p.get("daily", {}).items():
            ch.cargo[res] = ch.cargo.get(res, 0) + amt
    if cold:
        # ночь в стужу выматывает; одежда, кров и дровник защищают
        chill = 6.0 - _items.warmth(ch) * 0.6 - (2.0 if ch.home != "none" else 0.0) - (2.0 if has_woodshed else 0.0)
        chill *= _traits.mod(ch, "cold", 1.0)
        ch.fatigue = min(100.0, ch.fatigue + max(0.0, chill))
    # суточный пересчёт настроя и здоровья (кров даёт лёгкий отдых)
    update_condition(ch, rested=(ch.home != "none"))
