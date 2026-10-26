"""
Магия как ПЛЕТЕНИЕ (слой 3 мега-итерации).

Канон: магия — не кнопка «фаербол», а плетение силы, и доступна она НЕ всем.
Гейт двойной: врождённая предрасположенность (бросок при рождении, `gift`) плюс
обучение (навык `weaving`). У большинства жителей дара нет вовсе — они не колдуют.

Первый ломоть — три простых плетения как известные схемы:
- искра (spark)  — боевое: бьёт в схватке, вне боя лишь вспышка;
- свет  (light)  — бытовое: разгоняет морок, успокаивает, светит в пути;
- лат   (mend)   — лечебное: затягивает собственные раны, унимает кровь.
И СВОБОДНОЕ ПЛЕТЕНИЕ — азарт: сам задаёшь силу (power), выше сила → мощнее эффект,
но ниже шанс и злее откат. Это «на костях + весах», как и весь мир.

Мана — «чем действуешь»: не ресурс-полоска ради полоски, а то, из чего ткёшь.
Провал уводит в опасность: откат бьёт плетельщика (травма через world.body) и
надламывает психику (`psyche` растёт — смычка со слоем E). Собранность (удар по
голове, world.body.stability) и душевный надлом снижают шанс любого плетения.
"""
from __future__ import annotations
from . import body as _body

GIFT_THRESHOLD = 0.78          # ниже — дара нет, магии не научиться

WEAVES = {
    "spark": dict(name="Искра", icon="✨", cost=8.0,  base=0.72, kind="attack",
                  desc="разряд силы — бьёт зверя/врага в схватке"),
    "light": dict(name="Свет",  icon="🔆", cost=4.0,  base=0.86, kind="calm",
                  desc="тёплый свет — гонит морок и усталость, унимает душу"),
    "mend":  dict(name="Лат",   icon="🩹", cost=12.0, base=0.60, kind="heal",
                  desc="стянуть раны и уйму крови на себе"),
}


def roll_gift(ch, rng) -> None:
    """Бросок предрасположенности при рождении. Дар РЕДОК: лишь малая доля одарены,
    и даже у них сила разнится. rng — random.Random или наш Stream (есть .random())."""
    r = rng.random()
    if r < 0.07:                       # ~7% — одарённые
        ch.gift = round(0.78 + rng.random() * 0.22, 3)   # 0.78..1.0
    else:
        ch.gift = round(r * 0.6, 3)    # остальные — заведомо ниже порога


# ── гейт и пробуждение дара ──

def gifted(ch) -> bool:
    """Есть ли врождённая предрасположенность (может учиться магии)."""
    return getattr(ch, "gift", 0.0) >= GIFT_THRESHOLD


def is_mage(ch) -> bool:
    """Уже маг: есть дар И он пробуждён (есть мана/начал учиться)."""
    return gifted(ch) and (getattr(ch, "mana_max", 0.0) > 0 or ch.skill("weaving") > 0)


def awaken(ch) -> dict:
    """Пробудить дар у одарённого: открыть ману и первые азы плетения.
    Без дара — отказ (гейт «только маги»)."""
    if not gifted(ch):
        return {"ok": False, "msg": "дара к плетению нет — это не каждому дано"}
    if getattr(ch, "mana_max", 0.0) > 0:
        return {"ok": True, "msg": "дар уже пробуждён", "mana_max": ch.mana_max}
    ch.mana_max = round(18.0 + (ch.gift - GIFT_THRESHOLD) * 90.0, 1)   # сильнее дар — глубже источник
    ch.mana = ch.mana_max
    if ch.skill("weaving") <= 0:
        ch.train("weaving", 3.0)
    return {"ok": True, "msg": "дар пробуждён — ты чувствуешь силу под пальцами",
            "mana_max": ch.mana_max}


# ── общие множители шанса ──

def _focus(ch) -> float:
    """Собранность для плетения: роняет удар по голове и душевный надлом."""
    stab = _body.stability(ch)                        # 0.1..1.0 (голова)
    calm = 1.0 - min(1.0, getattr(ch, "psyche", 0.0) / 150.0)   # стресс мешает
    return max(0.1, stab * max(0.3, calm))


def _skill_bonus(ch) -> float:
    return ch.skill("weaving") / 200.0 + (getattr(ch, "gift", 0.0) - GIFT_THRESHOLD) * 0.4


def regen(ch, hours: float = 1.0) -> None:
    """Мана медленно восстанавливается у мага (вешается на часовой ход)."""
    if getattr(ch, "mana_max", 0.0) > 0 and ch.mana < ch.mana_max:
        ch.mana = min(ch.mana_max, ch.mana + ch.mana_max * 0.06 * hours)


# ── откат (провал плетения) ──

def _backlash(ch, severity: float, stream) -> dict:
    """Провал/перенапряжение бьёт самого плетельщика: травма + надлом психики."""
    hurt = severity * stream.uniform(0.7, 1.3)
    # откат чаще бьёт по голове (перенапряжение) — отсюда и путаница мыслей
    target = "head" if stream.chance(0.5) else None
    wound = _body.apply_hit(ch, hurt, stream, target=target,
                            crit=stream.chance(0.15)) if hurt > 0.4 else {}
    ch.psyche = min(100.0, getattr(ch, "psyche", 0.0) + severity * 1.2)
    return {"text": "откат силы — " + (wound.get("text") or "тебя тряхнуло"),
            "hurt": round(hurt, 1), "wound": wound}


# ── каст ──

def cast(ch, weave_id: str, stream, power: float = 0.0) -> dict:
    """Сплести известную схему. Возвращает сводку + при боевом плетении — descriptor
    эффекта (mp применит к схватке). power игнорируется для известных схем."""
    if not is_mage(ch):
        if gifted(ch):
            return {"ok": False, "msg": "дар не пробуждён — сперва пробуди его"}
        return {"ok": False, "msg": "плести не дано — нет дара"}
    wv = WEAVES.get(weave_id)
    if not wv:
        return {"ok": False, "msg": "нет такой схемы"}
    if ch.mana < wv["cost"]:
        return {"ok": False, "msg": f"мало силы ({ch.mana:.0f}/{wv['cost']:.0f})"}

    focus = _focus(ch)
    p = min(0.97, (wv["base"] + _skill_bonus(ch)) * focus)
    ch.mana -= wv["cost"]
    ch.train("weaving", 0.6)

    if not stream.chance(p):
        # провал: часть силы впустую + откат соразмерно схеме
        bl = _backlash(ch, wv["cost"] * 0.5 * (1.3 - p), stream)
        return {"ok": True, "success": False, "weave": weave_id, "icon": wv["icon"],
                "mana": round(ch.mana, 1), "backlash": bl,
                "msg": f"{wv['icon']} {wv['name']}: плетение сорвалось — {bl['text']}"}

    out = {"ok": True, "success": True, "weave": weave_id, "icon": wv["icon"],
           "mana": round(ch.mana, 1)}
    kind = wv["kind"]
    if kind == "heal":
        healed = _heal(ch, 10.0 + ch.skill("weaving") * 0.3)
        out["msg"] = f"{wv['icon']} {wv['name']}: раны затянулись ({healed})"
    elif kind == "calm":
        ch.fatigue = max(0.0, ch.fatigue - 10.0)
        ch.psyche = max(0.0, getattr(ch, "psyche", 0.0) - 8.0)
        try:
            from . import player as _player
            _player.update_condition(ch, cheered=4.0)
        except Exception:
            ch.morale = min(100.0, getattr(ch, "morale", 60.0) + 4.0)
        out["msg"] = f"{wv['icon']} {wv['name']}: морок отступил, на душе светлее"
    else:  # attack
        powr = (6.0 + ch.skill("weaving") * 0.5) * (0.8 + 0.4 * focus)
        out["effect"] = {"kind": "spark", "power": round(powr, 1)}
        out["msg"] = f"{wv['icon']} {wv['name']} сорвалась с пальцев!"
    return out


def free_weave(ch, power: float, stream) -> dict:
    """Свободное плетение — АЗАРТ: сам задаёшь силу 0..1. Больше силы → мощнее,
    но ниже шанс и злее откат. Универсальный эффект (лечение/разряд по обстоятельствам)."""
    if not is_mage(ch):
        return {"ok": False, "msg": "свободно плести может лишь маг"}
    power = max(0.1, min(1.0, float(power)))
    cost = 6.0 + power * 20.0
    if ch.mana < cost:
        return {"ok": False, "msg": f"мало силы ({ch.mana:.0f}/{cost:.0f})"}

    focus = _focus(ch)
    p = min(0.95, (0.55 + _skill_bonus(ch) - power * 0.5) * focus)
    ch.mana -= cost
    ch.train("weaving", 0.8 + power)

    if not stream.chance(p):
        bl = _backlash(ch, cost * (1.4 - p), stream)       # злее отката нет
        return {"ok": True, "success": False, "free": True, "power": power,
                "mana": round(ch.mana, 1), "backlash": bl,
                "msg": f"🌀 Вольное плетение рухнуло — {bl['text']}"}

    # удачное вольное плетение: мощный разряд + немного залечивает (сила слушается)
    powr = (8.0 + ch.skill("weaving") * 0.7) * (0.6 + 1.4 * power)
    healed = _heal(ch, 4.0 * power)
    ch.psyche = max(0.0, getattr(ch, "psyche", 0.0) - 2.0)
    return {"ok": True, "success": True, "free": True, "power": power,
            "mana": round(ch.mana, 1),
            "effect": {"kind": "spark", "power": round(powr, 1)},
            "msg": f"🌀 Вольное плетение удалось! сила {int(power*100)}%" +
                   (f", раны чуть стянулись ({healed})" if healed else "")}


def _heal(ch, amount: float) -> str:
    """Залечить самые тяжёлые раны и унять кровь. Возвращает краткое описание."""
    getattr(ch, "wounds", {})  # ensure attr
    w = ch.wounds
    ch.bleeding = max(0.0, getattr(ch, "bleeding", 0.0) - amount)
    left = amount
    # лечим от самой тяжёлой части к лёгким
    for part in sorted(w.keys(), key=lambda k: -w[k]):
        if left <= 0:
            break
        take = min(w[part], left)
        w[part] -= take
        left -= take
        if w[part] <= 0.01:
            w.pop(part, None)
    ch.health = min(100.0, ch.health + amount * 0.3)
    return f"+{amount:.0f}"


# ── вид для клиента ──

def view(ch) -> dict:
    """Что показать магу (немагу панель магии не рисуем вовсе)."""
    if not gifted(ch):
        return {"mage": False}
    weaves = [{"id": k, "name": v["name"], "icon": v["icon"],
               "cost": v["cost"], "desc": v["desc"],
               "can": is_mage(ch) and ch.mana >= v["cost"]} for k, v in WEAVES.items()]
    return {"mage": True, "awakened": is_mage(ch),
            "gift": round(getattr(ch, "gift", 0.0), 2),
            "mana": round(getattr(ch, "mana", 0.0), 1),
            "mana_max": round(getattr(ch, "mana_max", 0.0), 1),
            "weaving": round(ch.skill("weaving"), 1),
            "psyche": round(getattr(ch, "psyche", 0.0), 1),
            "weaves": weaves}
