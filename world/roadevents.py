"""
События в пути каравана — чтобы дорога между деревнями (и шардами) была живой, а не
пустым таймером. На длинном и «дальнем» (межшардовом) пути событий больше.

Виды: находка (повезло — монеты/припас), встречный торговец (мелкая выгода),
засада (бой — зверь или разбойник). Исход засады считает combat. Для офлайн-игрока
(под AI) засада разрешается сама со средним качеством — он отбивается.
"""
from __future__ import annotations
from . import combat


def roll(rng, hours: int, cross: bool):
    """Вернуть событие пути или None. Шанс растёт с длиной и «дальностью» пути."""
    chance = min(0.75, 0.12 + hours * 0.03 + (0.15 if cross else 0.0))
    if rng.random() > chance:
        return None
    r = rng.random()
    if r < 0.40:
        return {"kind": "ambush", "beast": combat.pick(rng, "road")}
    if r < 0.70:
        return {"kind": "find", "coins": round(rng.uniform(3, 10), 1),
                "res": rng.choice(["raw", "honey", "wood"]), "amt": rng.randint(2, 6)}
    return {"kind": "merchant", "coins": round(rng.uniform(4, 12), 1)}


def apply_passive(ev, ch, rng) -> str:
    """Применить не-боевое событие (находка/торговец) сразу. Вернуть текст-новость."""
    if ev["kind"] == "find":
        ch.coins += ev["coins"]
        ch.cargo[ev["res"]] = ch.cargo.get(ev["res"], 0) + ev["amt"]
        return f"🍀 В пути повезло: +{ev['coins']:.0f}⌾ и {ev['amt']} {ev['res']}."
    if ev["kind"] == "merchant":
        ch.coins += ev["coins"]
        return f"🧺 Встречный торговец сторговался удачно: +{ev['coins']:.0f}⌾."
    return ""
