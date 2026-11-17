"""
Предметы, инвентарь и склад. Пока без боевого применения — это косметика и
отработка самой механики владения: купил → лежит в инвентаре → перенёс на склад
дома. Применение (урон оружия, тепло одежды) придёт позже; каталог уже заложен
с нужными полями, чтобы не переписывать потом.

Данные отдельно от логики: предмет — это запись в CATALOG, у персонажа лишь id'шки.
"""
from __future__ import annotations

# slot: weapon | clothing | trinket ; stats зарезервированы под будущее применение
CATALOG = {
    # оружие (пока косметика)
    "axe":        dict(name="Топор",            slot="weapon",   price=18,  icon="🪓", stats={"dmg": 6}),
    "knife":      dict(name="Нож",              slot="weapon",   price=9,   icon="🔪", stats={"dmg": 4}),
    "spear":      dict(name="Рогатина",         slot="weapon",   price=26,  icon="🗡", stats={"dmg": 9}),
    "bow":        dict(name="Лук",              slot="weapon",   price=34,  icon="🏹", stats={"dmg": 11, "range": 1}),
    "sword":      dict(name="Меч",              slot="weapon",   price=70,  icon="⚔", stats={"dmg": 13}),
    # одежда по слотам: wear = head|body|outfit|feet ; warmth суммируется по надетому
    "hat":        dict(name="Шапка",            slot="clothing", wear="head",   price=7,   icon="🎩", stats={"warmth": 2}),
    "ushanka":    dict(name="Ушанка",           slot="clothing", wear="head",   price=14,  icon="🧢", stats={"warmth": 4}),
    "shirt":      dict(name="Рубаха",           slot="clothing", wear="body",   price=8,   icon="👕", stats={"warmth": 2}),
    "vest":       dict(name="Безрукавка",       slot="clothing", wear="body",   price=12,  icon="🦺", stats={"warmth": 3}),
    "coat":       dict(name="Кафтан",           slot="clothing", wear="outfit", price=22,  icon="🧥", stats={"warmth": 5}),
    "fur":        dict(name="Шуба",             slot="clothing", wear="outfit", price=40,  icon="🦫", stats={"warmth": 9}),
    "cloak":      dict(name="Плащ",             slot="clothing", wear="outfit", price=18,  icon="🧣", stats={"warmth": 4}),
    "boots":      dict(name="Сапоги",           slot="clothing", wear="feet",   price=15,  icon="🥾", stats={"warmth": 3}),
    "lapti":      dict(name="Лапти",            slot="clothing", wear="feet",   price=4,   icon="🥿", stats={"warmth": 1}),
    # снасти
    "trap":       dict(name="Силок",            slot="tool",     price=10,  icon="🪤", stats={}),
    # расходники (зелья из трав) — применяются, дают эффект и исчезают
    "tonic":      dict(name="Отвар бодрости",   slot="consumable", price=12, icon="🧪", stats={"fatigue": -35}),
    "balm":       dict(name="Целебная мазь",    slot="consumable", price=16, icon="🧴", stats={"fatigue": -18}),
    # обереги/украшения — дают реальный эффект, пока надеты в слоте trinket
    "amulet":     dict(name="Оберег",           slot="trinket",  price=30,  icon="🧿",
                       stats={"luck": 0.08, "guard": 0.10}),
    "ring":       dict(name="Перстень",         slot="trinket",  price=45,  icon="💍",
                       stats={"trade": 0.06, "charm": 1.0}),
}


# рецепты крафта: из добытого (raw=шкура/мясо, wood, honey) ковать и шить самому
RECIPES = {
    "knife":  {"wood": 2, "raw": 1},
    "spear":  {"wood": 4, "raw": 2},
    "bow":    {"wood": 5, "raw": 3},
    "shirt":  {"raw": 3},
    "boots":  {"raw": 4},
    "hat":    {"raw": 2},
    "coat":   {"raw": 6, "wood": 1},
    "fur":    {"raw": 10},
    "amulet": {"honey": 4, "wood": 2},
    "trap":   {"wood": 3, "raw": 1},
    "sword":  {"ore": 4, "wood": 2},            # кузнечество из руды
    "tonic":  {"herb": 3},
    "balm":   {"herb": 2, "honey": 2},
}


def info(item_id: str) -> dict | None:
    return CATALOG.get(item_id)


_RECIPES_CACHE = None


def recipes_list() -> list:
    global _RECIPES_CACHE
    if _RECIPES_CACHE is None:
        out = []
        for iid, cost in RECIPES.items():
            it = CATALOG[iid]
            out.append({"id": iid, "name": it["name"], "icon": it["icon"],
                        "slot": it["slot"], "cost": cost})
        _RECIPES_CACHE = out
    return _RECIPES_CACHE


def can_craft(ch, item_id: str) -> bool:
    r = RECIPES.get(item_id)
    return bool(r) and all(ch.cargo.get(res, 0) >= amt for res, amt in r.items())


def craft(ch, item_id: str) -> dict:
    """Скрафтить предмет из припасов в грузе. Растит навык crafting."""
    r = RECIPES.get(item_id)
    if not r:
        return {"ok": False, "msg": "нет такого рецепта"}
    missing = [f"{res} {amt}" for res, amt in r.items() if ch.cargo.get(res, 0) < amt]
    if missing:
        return {"ok": False, "msg": "не хватает: " + ", ".join(missing)}
    for res, amt in r.items():
        ch.cargo[res] -= amt
        if ch.cargo[res] <= 0:
            ch.cargo.pop(res, None)
    ch.inventory.append(item_id)
    ch.train("crafting", 1.0)
    return {"ok": True, "msg": f"Скрафтено: {label(item_id)}.",
            "skill_level": round(ch.skill("crafting"), 1)}


def label(item_id: str) -> str:
    it = CATALOG.get(item_id)
    return f"{it['icon']} {it['name']}" if it else item_id


_CATALOG_CACHE = None


def catalog_list() -> list:
    # каталог статичен — собираем один раз и отдаём один и тот же объект
    global _CATALOG_CACHE
    if _CATALOG_CACHE is None:
        _CATALOG_CACHE = [dict(id=k, **v) for k, v in CATALOG.items()]
    return _CATALOG_CACHE


def buy(ch, item_id: str) -> dict:
    """Купить предмет: монеты → предмет в инвентарь (при себе)."""
    it = CATALOG.get(item_id)
    if not it:
        return {"ok": False, "msg": "нет такого товара"}
    if ch.coins < it["price"]:
        return {"ok": False, "msg": f"нужно {it['price']} монет, у тебя {ch.coins:.0f}."}
    ch.coins -= it["price"]
    ch.inventory.append(item_id)
    return {"ok": True, "msg": f"Куплено: {label(item_id)}.",
            "coins": round(ch.coins, 1)}


def sell(ch, item_id: str) -> dict:
    """Продать предмет из инвентаря (за полцены — рынок есть рынок)."""
    it = CATALOG.get(item_id)
    if item_id not in ch.inventory or not it:
        return {"ok": False, "msg": "нечего продать"}
    ch.inventory.remove(item_id)
    back = round(it["price"] * 0.5, 1)
    ch.coins += back
    return {"ok": True, "msg": f"Продано: {label(item_id)} (+{back:.0f} монет).",
            "coins": round(ch.coins, 1)}


def to_storage(ch, item_id: str) -> dict:
    """Переложить из инвентаря на склад дома (нужно жильё)."""
    if ch.home == "none":
        return {"ok": False, "msg": "склад появится, когда будет жильё."}
    if item_id not in ch.inventory:
        return {"ok": False, "msg": "этого нет при себе"}
    ch.inventory.remove(item_id)
    ch.storage.append(item_id)
    return {"ok": True, "msg": f"На склад: {label(item_id)}."}


def from_storage(ch, item_id: str) -> dict:
    """Забрать со склада дома в инвентарь."""
    if item_id not in ch.storage:
        return {"ok": False, "msg": "этого нет на складе"}
    ch.storage.remove(item_id)
    ch.inventory.append(item_id)
    return {"ok": True, "msg": f"Взято со склада: {label(item_id)}."}


CLOTH_SLOTS = ("head", "body", "outfit", "feet")
WORN_SLOTS = ("weapon", "head", "body", "outfit", "feet", "trinket")


# ── экипировка: надеть оружие/одежду/оберег по своему слоту ──
def equip(ch, item_id: str) -> dict:
    it = CATALOG.get(item_id)
    if not it or item_id not in ch.inventory:
        return {"ok": False, "msg": "этого нет при себе"}
    if it["slot"] == "weapon":
        ch.weapon = item_id
        return {"ok": True, "msg": f"В руках: {label(item_id)}."}
    if it["slot"] == "clothing":
        setattr(ch, it.get("wear", "body"), item_id)
        return {"ok": True, "msg": f"Надето: {label(item_id)}."}
    if it["slot"] == "trinket":
        ch.trinket = item_id
        return {"ok": True, "msg": f"Надето: {label(item_id)}."}
    return {"ok": False, "msg": "это не надевается"}


def unequip(ch, slot: str) -> dict:
    if slot in WORN_SLOTS:
        setattr(ch, slot, "")
        return {"ok": True, "msg": "снято"}
    if slot == "clothing":                 # обратная совместимость — снять верх
        ch.outfit = ""
        return {"ok": True, "msg": "снято"}
    return {"ok": False, "msg": "?"}


def worn(ch) -> dict:
    """Что надето по слотам -> {slot: {id,label}} (для паперdoll)."""
    out = {}
    for s in WORN_SLOTS:
        iid = getattr(ch, s, "")
        if iid:
            out[s] = {"id": iid, "label": label(iid)}
    return out


def use(ch, item_id: str) -> dict:
    """Применить расходник (зелье): даёт эффект и исчезает."""
    it = CATALOG.get(item_id)
    if not it or it["slot"] != "consumable" or item_id not in ch.inventory:
        return {"ok": False, "msg": "это не применить"}
    ch.inventory.remove(item_id)
    df = it["stats"].get("fatigue", 0)
    if df:
        ch.fatigue = max(0.0, min(100.0, ch.fatigue + df))
    return {"ok": True, "msg": f"Выпито: {label(item_id)} (усталость {df:+.0f}).",
            "fatigue": round(ch.fatigue)}


def weapon_dmg(ch) -> float:
    it = CATALOG.get(ch.weapon)
    return float(it["stats"].get("dmg", 0)) if it else 0.0


# стартовое снаряжение жителя по ремеслу — чтобы у НПС была одежда и орудие труда
# (и чтобы паперdoll/экономика не были пустыми). Возвращает dict слотов + инвентарь.
ROLE_GEAR = {
    "woodcutter":   dict(weapon="axe",   body="shirt", feet="boots", head="hat"),
    "smith":        dict(weapon="knife", body="vest",  feet="boots"),
    "cook":         dict(weapon="knife", body="shirt", feet="lapti"),
    "fisher":       dict(weapon="spear", body="shirt", feet="boots"),
    "hunter":       dict(weapon="bow",   body="shirt", outfit="coat", feet="boots", head="ushanka"),
    "herbalist":    dict(weapon="knife", body="shirt", outfit="cloak", feet="lapti"),
    "tavernkeeper": dict(body="vest",  feet="boots"),
    "trader":       dict(weapon="knife", body="vest", outfit="cloak", feet="boots", head="hat"),
}
_BASIC_GEAR = dict(body="shirt", feet="lapti")


def outfit_for_role(ch, rng) -> None:
    """Надеть на жителя стартовое снаряжение по его ремеслу (прямо в слоты).
    Бедные/обычные — базовая одежда; мастера — орудие труда и потеплее."""
    gear = dict(ROLE_GEAR.get(ch.role, _BASIC_GEAR))
    for slot, iid in gear.items():
        if iid in CATALOG:
            setattr(ch, slot, iid)
    # немного запаса в котомке (иногда), чтобы было чем торговать/меняться
    if rng.random() < 0.3:
        ch.inventory.append(rng.choice(["hat", "shirt", "lapti", "knife"]))


def warmth(ch) -> float:
    total = 0.0
    for s in CLOTH_SLOTS:
        it = CATALOG.get(getattr(ch, s, ""))
        if it:
            total += float(it["stats"].get("warmth", 0))
    return total


def trinket_stat(ch, key: str, default: float = 0.0) -> float:
    """Эффект надетого оберега/перстня: luck/guard/trade/charm. 0, если нет."""
    it = CATALOG.get(getattr(ch, "trinket", "") or "")
    if not it:
        return default
    return float(it["stats"].get(key, default))
