"""
Бенчмарк масштабирования: регион из многих поселений.
Сравниваем «всё горячее» (детально) против LoD (горячи лишь несколько, остальные
считаются дёшево offline). Показывает рычаг масштабирования в цифрах.

    python3 bench2.py --settlements 2000 --hot 10 --days 30
"""
from __future__ import annotations
import argparse
import time
import world as W
from world.region import Region


def make_region(n, seed):
    return Region([_village(f"сел-{i}") for i in range(n)], seed=seed)


def _village(name):
    s = W.make_world()
    s.name = name
    return s


def run(n, hot, days, seed):
    hours = days * 24

    # — режим A: всё горячее (детально) —
    rA = make_region(n, seed)
    rA.set_hot(range(n))
    t0 = time.perf_counter()
    for _ in range(hours):
        rA.step_hour()
    tA = time.perf_counter() - t0

    # — режим B: LoD (горячи лишь hot штук) —
    rB = make_region(n, seed)
    rB.set_hot(range(hot))
    t0 = time.perf_counter()
    for _ in range(hours):
        rB.step_hour()
    tB = time.perf_counter() - t0

    print(f"поселений: {n} | дней: {days} (часов {hours})")
    print(f"A. всё горячее: {tA:.3f} с | {n*days/tA:,.0f} поселение-суток/с | "
          f"итог {rA.totals()}")
    print(f"B. LoD (горячих {hot}): {tB:.3f} с | {n*days/tB:,.0f} поселение-суток/с | "
          f"итог {rB.totals()}")
    print(f"ускорение LoD: ×{tA/tB:.1f}")
    print(f"вывод: детально живёт лишь горячая часть мира, холодная — почти даром; "
          f"так один регион масштабируется до тысяч поселений на одном ядре.")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--settlements", type=int, default=2000)
    ap.add_argument("--hot", type=int, default=10)
    ap.add_argument("--days", type=int, default=30)
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args()
    run(a.settlements, a.hot, a.days, a.seed)
