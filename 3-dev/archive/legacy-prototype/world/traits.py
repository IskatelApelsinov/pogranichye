"""Черты характера жителей — данные отдельно от логики.
Каждая черта даёт небольшие множители к занятиям/усталости/удаче/соц. Пустая черта
('') — «обычный» характер без модификаторов (так и у старых сохранений). Черта
выдаётся при рождении (сид rng), поэтому мир остаётся детерминированным.
"""
from __future__ import annotations

TRAITS = {
    "diligent":  dict(name="Трудолюбивый",  icon="💪", work=1.12, tire=1.05,
                      desc="спорая работа, но и устаёт охотнее"),
    "hardy":     dict(name="Выносливый",    icon="🏔", tire=0.78,
                      desc="усталость копится медленнее"),
    "deft":      dict(name="Ловкий",        icon="🤹", quality=0.10,
                      desc="лучше даются мини-игры (ремесло рук)"),
    "homely":    dict(name="Хозяйственный", icon="🏠", home=1.18,
                      desc="быстрее обустраивает двор и дом"),
    "sociable":  dict(name="Общительный",   icon="🗣", social=1.4, tavern=1.5,
                      desc="легко сходится с людьми, отдыхает в корчме лучше"),
    "lucky":     dict(name="Удачливый",     icon="🍀", luck=0.12,
                      desc="чаще везёт на охоте, в костях и картах"),
    "thrifty":   dict(name="Бережливый",    icon="🪙", pay=1.6, trade=0.05,
                      desc="чаще получает плату и торгуется выгоднее"),
    "stout":     dict(name="Крепкий",       icon="❤", heal=1.5, cold=0.6,
                      desc="крепче здоровьем, легче переносит холод"),
}

ORDER = list(TRAITS.keys())


def assign(rng) -> str:
    """Выдать случайную черту (при рождении). Детерминировано — от переданного rng."""
    return rng.choice(ORDER)


def info(key: str) -> dict | None:
    return TRAITS.get(key or "")


def label(key: str) -> str:
    it = TRAITS.get(key or "")
    return f"{it['icon']} {it['name']}" if it else "обычный"


def mod(ch, field: str, default: float = 1.0) -> float:
    """Множитель черты для поля (work/tire/home/luck/pay/heal/cold/quality/social/tavern).
    Нет черты или нет поля — вернёт default."""
    it = TRAITS.get(getattr(ch, "trait", "") or "")
    if not it:
        return default
    return float(it.get(field, default))
