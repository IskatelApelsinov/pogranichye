"""
Оффлайн-модель холодного (заархивированного) поселения. Тел в RAM нет — работаем
с числом населения (archived_pop) и агрегатными складом/домами. Один дешёвый шаг
в сутки. Это LoD: детально живёт лишь горячее, холодное — числа.

Эмиграция в холоде «чеканит» одного путника (Character) — он уходит в мир и может
прийти в другое поселение. Рождения/смерти меняют только число archived_pop.
"""
from __future__ import annotations
from . import tuning as T
from .content import BABY_NAMES
from .model import Character
from .memory import record_anon_death


def offline_day(w, clock, rng) -> None:
    pop = w.pop()
    if pop == 0:
        return

    workers = pop
    food_need = pop * T.EAT_PER_DAY
    wood_need = pop * T.FIREWOOD_PER_DAY

    target_food = food_need * 3
    target_wood = wood_need * 3
    w.store["food"] += min(workers * 6.0, max(0.0, target_food - w.store["food"]))
    w.store["wood"] += min(workers * 4.0, max(0.0, target_wood - w.store["wood"]))

    w.store["food"] = max(0.0, w.store["food"] - food_need)
    w.store["wood"] = max(0.0, w.store["wood"] - wood_need)
    w.store["food"] *= T.FOOD_SPOIL
    w.store["raw"] *= T.RAW_SPOIL
    upkeep = 3.0 if workers > 0 else 0.0
    w.house_integrity = max(0.0, min(100.0, w.house_integrity - T.HOUSE_DECAY + upkeep))

    fed = w.store["food"] > 0

    # смерть от голода — убавляем число (конкретных тел в RAM нет)
    if not fed and w.archived_pop > 0:
        if rng.random() < T.STARVE_DEATH_CHANCE * 0.5:
            w.archived_pop -= 1
            record_anon_death(w, "Голод унёс жителя.", clock)

    # население числом: рождение / эмиграция
    pop = w.pop()
    if pop < T.SETTLEMENT_CAPACITY and fed and rng.random() < T.BIRTH_CHANCE:
        w.archived_pop += 1
    elif pop > T.SETTLEMENT_CAPACITY and rng.random() < T.EMIGRATE_BASE * (pop - T.SETTLEMENT_CAPACITY):
        # чеканим путника и выпускаем в мир
        if w.archived_pop > 0:
            w.archived_pop -= 1
            leaver = Character(name=rng.choice(BABY_NAMES), role="idler")
            w.departures.append(leaver)
            if len(w.departures) > 50:
                del w.departures[:-50]
            w.add_news(clock, f"{leaver.name} ушёл искать лучшей доли в других краях.")

    # редкое событие
    if rng.random() < T.EVENT_CHANCE * 0.5:
        if rng.random() < 0.5:
            w.store["food"] += rng.uniform(10, 25)
        else:
            w.house_integrity = max(0.0, w.house_integrity - rng.uniform(5, 12))
