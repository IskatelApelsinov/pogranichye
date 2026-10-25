"""
Память мира. Стержень игры: персонажи преходящи, мир помнит. Жизнь агента
складывается в арку — прославился мастером, пал в беде — и остаётся в ХРОНИКЕ
поселения даже после смерти тела и после архивации. Это зерно будущего фольклора:
из таких записей потом и родятся легенды, которые носят барды.
"""
from __future__ import annotations

MASTER_SKILL = 60.0

_DEED_RU = {
    "fishing":  "лучший рыбак деревни",
    "cooking":  "искусный повар",
    "chopping": "знатный дровосек",
    "smithing": "кузнец-мастер",
}


def _top_skill(ch):
    if not ch.skills:
        return None, 0.0
    k, v = max(ch.skills.items(), key=lambda kv: kv[1])
    return k, v


def record_milestone(w, ch, clock) -> None:
    """Агент дорос до мастера — мир это запоминает (один раз на жизнь)."""
    if ch.renowned:
        return
    k, v = _top_skill(ch)
    if k and v >= MASTER_SKILL:
        ch.renowned = True
        deed = _DEED_RU.get(k, f"мастер ({k})")
        w.add_chronicle(clock, f"{ch.name} прослыл как {deed}.")


def record_death(w, ch, cause: str, clock) -> None:
    """Смерть оставляет след: могилу и, если жизнь была примечательной, легенду."""
    w.graves += 1
    w.add_news(clock, f"{ch.name}: {cause}.")
    k, v = _top_skill(ch)
    if v >= MASTER_SKILL:
        deed = _DEED_RU.get(k, f"мастер ({k})")
        w.add_chronicle(clock, f"Пал {ch.name}, {deed} — {cause}. Деревня помнит его.")
    elif ch.renowned or v >= MASTER_SKILL * 0.6:
        w.add_chronicle(clock, f"Не стало {ch.name} — {cause}.")
    # рядовую смерть помнит лишь счётчик могил — не каждый попадает в легенды


def record_anon_death(w, cause: str, clock) -> None:
    """Смерть в холодном (архивном) поселении — тел в RAM нет, но память есть."""
    w.graves += 1
    w.add_chronicle(clock, f"{cause} В поселении стало на одну историю меньше.")
