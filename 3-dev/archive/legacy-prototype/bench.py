"""
Бенчмарк: сколько мир тянет на одном ядре в наивном режиме «все поселения горячие»
(каждое считается детально каждый час). Меряем, прежде чем оптимизировать.

    python3 bench.py --settlements 500 --days 5
"""
from __future__ import annotations
import argparse
import time
import world as W


def build_many(n: int, seed: int):
    engines = []
    for i in range(n):
        w, clock = W.make_world(), W.WorldClock()
        engines.append(W.build_engine(w, clock, seed + i))
    return engines


def run(n: int, days: int, seed: int):
    engines = build_many(n, seed)
    residents = sum(len(e.world.residents) for e in engines)
    hours = days * 24

    t0 = time.perf_counter()
    for _ in range(hours):
        for e in engines:
            e.step_hour()
    dt = time.perf_counter() - t0

    settlement_hours = n * hours
    entity_hours = residents * hours      # сколько «житель-часов» просчитано
    print(f"поселений: {n} | жителей всего: {residents} | дней: {days} (часов {hours})")
    print(f"время: {dt:.3f} с")
    print(f"поселение-часов/с: {settlement_hours/dt:,.0f}")
    print(f"житель-часов/с:    {entity_hours/dt:,.0f}")
    print(f"на 1 поселение-сутки: {dt/(n*days)*1000:.3f} мс")
    # прикидка потолка: сколько поселений можно держать «горячими» в реальном времени,
    # если мир тикает 1 игровой час в реальную секунду
    per_settlement_hour = dt / settlement_hours
    ceiling = 1.0 / per_settlement_hour   # поселений, обсчитываемых за 1 реальную секунду
    print(f"≈ потолок горячих поселений при 1 ч/с: {ceiling:,.0f} (на этом железе, 1 поток)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--settlements", type=int, default=500)
    ap.add_argument("--days", type=int, default=5)
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args()
    run(a.settlements, a.days, a.seed)
