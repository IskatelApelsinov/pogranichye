"""
Бой со зверьём и лихими людьми. Server-authoritative: зверя выбирает сервер,
клиент лишь присылает качество боевой мини-игры (0..1), исход считает мир.

Оружие обретает прямой смысл: его урон (dmg) — основа силы удара, навык (hunting/
combat) и качество мини-игры множат. Одежда (warmth как грубая «броня») смягчает
ответный урон. Победа — добыча в личный груз + монеты + рост навыка; поражение —
травма (усталость/голод) и, с разбойником, потеря части груза. Прямой смерти в бою
нет (перма-смерть — через голод по канону), но травма делает уязвимым.
"""
from __future__ import annotations
from . import items as _items
from . import body as _body

# power — боевая мощь зверя; loot — добыча в груз; coins — монеты; danger — ответный урон
BEASTS = {
    "hare":   dict(name="Заяц",       icon="🐇", power=2,  loot={"raw": 3},  coins=2,  skill="hunting", danger=1),
    "fox":    dict(name="Лиса",       icon="🦊", power=4,  loot={"raw": 4},  coins=4,  skill="hunting", danger=2),
    "boar":   dict(name="Кабан",      icon="🐗", power=6,  loot={"raw": 9},  coins=6,  skill="hunting", danger=5),
    "wolf":   dict(name="Волк",       icon="🐺", power=8,  loot={"raw": 7},  coins=8,  skill="hunting", danger=7),
    "bear":   dict(name="Медведь",    icon="🐻", power=12, loot={"raw": 16}, coins=15, skill="hunting", danger=10),
    "bandit": dict(name="Разбойник",  icon="🗡", power=9,  loot={},          coins=12, skill="combat",  danger=8),
}

# «тиры» по опасности — из них выбираем в зависимости от повода (охота/дорога)
TIERS = {
    "small":  ["hare", "fox"],
    "medium": ["boar", "wolf"],
    "big":    ["bear"],
    "road":   ["wolf", "bandit", "boar"],
}


def pick(rng, tier: str = "small") -> str:
    return rng.choice(TIERS.get(tier, TIERS["small"]))


def info(beast_id: str) -> dict | None:
    return BEASTS.get(beast_id)


def attack_power(ch, quality: float) -> float:
    """Сила удара: база + оружие, умноженные на навык и качество мини-игры."""
    dmg = _items.weapon_dmg(ch)
    skill = max(ch.skill("hunting"), ch.skill("combat"))
    # голыми руками зверя не взять (база мала) — нужна снасть; оружие и навык множат.
    # Раны бьющей руки/корпуса ослабляют удар (системное последствие ран, world.body).
    return (1.0 + dmg) * (0.35 + 0.65 * quality) * (1.0 + skill / 100.0) * _body.attack_mult(ch)


def resolve(ch, beast_id: str, quality: float, rng) -> dict:
    """Разрешить схватку. Возвращает сводку для клиента."""
    b = BEASTS.get(beast_id)
    if not b:
        return {"ok": False, "msg": "зверя уже нет"}
    quality = max(0.0, min(1.0, quality))
    atk = attack_power(ch, quality)
    foe = b["power"] * rng.uniform(0.8, 1.2)
    skill = b["skill"]
    ch.train(skill, 0.6 + quality)                 # опыт боя растёт в любом исходе
    win = atk >= foe

    # ответный урон зверя, смягчённый «бронёй» (тепло одежды) и оберегом (guard)
    armor = _items.warmth(ch) * 0.4
    hurt = max(0.0, b["danger"] * (1.3 - quality) - armor)
    hurt *= (1.0 - _items.trinket_stat(ch, "guard"))     # оберег бережёт в бою
    ch.fatigue = min(100.0, ch.fatigue + hurt * 0.4)     # часть «боли» — просто усталость от схватки
    # остальное — настоящая рана по части тела (кости выбирают куда, если не целишься)
    wound = {}
    if hurt > 0.5:
        crit = rng.chance(0.10 + 0.18 * (1.0 - quality))  # промах чаще пропускает тяжёлый удар
        wound = _body.apply_hit(ch, hurt, rng, crit=crit)

    if win:
        gained = {}
        for res, amt in b["loot"].items():
            take = round(amt * (0.5 + 0.5 * quality))
            if take:
                ch.cargo[res] = ch.cargo.get(res, 0) + take
                gained[res] = take
        pay = round(b["coins"] * (0.5 + 0.5 * quality), 1)
        ch.coins += pay
        wtext = f" · {wound['text']}" if wound.get("text") else ""
        return {"ok": True, "win": True, "beast": b["name"], "icon": b["icon"],
                "loot": gained, "coins_gain": pay, "coins": round(ch.coins, 1),
                "hurt": round(hurt), "wound": wound, "skill": skill,
                "skill_level": round(ch.skill(skill), 1),
                "msg": f"{b['icon']} {b['name']} повержен! добыча: "
                       + (", ".join(f"{k} {v}" for k, v in gained.items()) or "—")
                       + f", +{pay}⌾" + wtext}
    # поражение: травма; разбойник отнимает часть груза
    stolen = {}
    if beast_id == "bandit" and ch.cargo:
        res = rng.choice(list(ch.cargo))
        amt = min(ch.cargo[res], max(1, round(ch.cargo[res] * 0.5)))
        ch.cargo[res] -= amt
        if ch.cargo[res] <= 0:
            ch.cargo.pop(res, None)
        stolen[res] = amt
    wtext = f", {wound['text']}" if wound.get("text") else ""
    return {"ok": True, "win": False, "beast": b["name"], "icon": b["icon"],
            "hurt": round(hurt), "wound": wound, "stolen": stolen, "skill": skill,
            "skill_level": round(ch.skill(skill), 1),
            "msg": f"{b['icon']} {b['name']} оказался сильнее — ты отступил"
                   + wtext
                   + (f", украдено: {', '.join(f'{k} {v}' for k, v in stolen.items())}" if stolen else "")}
