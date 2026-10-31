"""
Этап 1 — запуск мира в терминале. Тонкая оболочка над пакетом world/.
Вся логика — в модулях world/. Этот файл только собирает движок и печатает.

    python3 sim.py                         # 30 дней, подробно
    python3 sim.py --days 60 --quiet
    python3 sim.py --seed 7
    python3 sim.py --save mir.json
    python3 sim.py --load mir.json --days 10
    python3 sim.py --no-weather            # собрать мир без модуля погоды (демо модульности)
"""
from __future__ import annotations
import argparse
import world as W
from world.systems import (NeedsSystem, WorkSystem, TavernSystem, ConsumptionSystem,
                           EventsSystem, PopulationSystem, WeatherSystem, QuestSystem,
                           SeasonSystem, AlifeSystem)


def fmt_store(store: dict) -> str:
    return " ".join(f"{k}:{v:.0f}" for k, v in store.items())


def run(days, seed, quiet, load, save, no_weather):
    if load:
        w, clock = W.load_world(load)
        print(f"=== загружен мир «{w.name}», день {clock.day} ===")
    else:
        w, clock = W.make_world(), W.WorldClock()
        print(f"=== {w.name}: старт, жителей {len(w.residents)}, seed {seed} ===")

    systems = [NeedsSystem, WorkSystem, TavernSystem, ConsumptionSystem,
               EventsSystem, PopulationSystem, QuestSystem, SeasonSystem, AlifeSystem]
    if not no_weather:
        systems.append(WeatherSystem)
    eng = W.build_engine(w, clock, seed, systems=systems)

    print(f"склад: {fmt_store(w.store)} | здоровье {w.health()} | "
          f"модули: {', '.join(s.name for s in eng.systems)}\n")

    start_day = clock.day
    last_news = len(w.news)
    while clock.day < start_day + days:
        eng.step_hour()
        if clock.hour == 0:
            alive = len(w.living())
            if not quiet or clock.day % 5 == 0 or alive < len(w.residents):
                print(f"День {clock.day-1:>3} | здоровье {w.health():>5} | "
                      f"живых {alive:>2} | еды на {w.food_days():4.1f} дн | "
                      f"склад {fmt_store(w.store)} | топор {w.axe_wear:.0f}")
            for line in w.news[last_news:]:
                print(f"        ▸ {line}")
            last_news = len(w.news)
            if alive == 0:
                print("\n☠  Поселение вымерло. Мир пошёл дальше без него.")
                break

    print(f"\n✓ прожито дней: {clock.day - start_day}. Здоровье {w.health()}. "
          f"Живых {len(w.living())}/{len(w.residents)}.")
    if w.living():
        best = max(w.residents, key=lambda c: sum(c.skills.values()))
        if best.skills:
            sk = ", ".join(f"{k} {v:.0f}" for k, v in best.skills.items())
            print(f"  Мастер деревни: {best.name} ({best.role}) — {sk}")
    if save:
        W.save_world(w, clock, save)
        print(f"  Мир сохранён в {save}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=30)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--quiet", action="store_true")
    ap.add_argument("--load", type=str, default="")
    ap.add_argument("--save", type=str, default="")
    ap.add_argument("--no-weather", action="store_true", help="собрать без модуля погоды")
    a = ap.parse_args()
    try:
        run(a.days, a.seed, a.quiet, a.load, a.save, a.no_weather)
    except BrokenPipeError:
        pass        # вывод оборвали (например, | head) — это нормально
