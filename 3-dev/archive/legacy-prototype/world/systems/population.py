"""Население как механика со стоком: рождения ограничены ёмкостью поселения,
излишек людей уходит искать лучшей доли (эмиграция). Так мир не пухнет вечно —
это «движение вперёд» на уровне демографии. Раз в сутки.

Задел на Stage 2: уходящие потом будут не исчезать, а приходить в другие
поселения (миграция по нужде)."""
from __future__ import annotations
from .. import tuning as T
from .. import names
from .. import traits as _traits
from ..model import Character
from ..engine import System

DAYS_PER_YEAR = 365.0


class PopulationSystem(System):
    name = "population"

    def on_day(self, w, clock, rng):
        # старение: мир из людей, а не вечных юнитов — возраст течёт
        for c in w.living():
            c.age += 1.0 / DAYS_PER_YEAR

        liv = w.living()
        pop = len(liv)
        cap = T.SETTLEMENT_CAPACITY

        # рождение — только если есть место и деревня сыта
        if pop < cap and w.food_days() > 2.0 and rng.random() < T.BIRTH_CHANCE and liv:
            # родители: мать и отец из взрослых жителей, если есть
            adults = [c for c in liv if c.age >= 16]
            women = [c for c in adults if c.sex == "ж"]
            men = [c for c in adults if c.sex == "м"]
            mother = rng.choice(women) if women else rng.choice(liv)
            father = rng.choice(men) if men else None
            sex = names.random_sex(rng)
            existing = {c.name for c in w.residents}
            # фамилия от отца (род по отцу), иначе от матери, иначе своя
            fam = (father.surname if father and father.surname else
                   (mother.surname if mother.surname else names.random_surname(sex, rng)))
            baby = Character(name=names.random_name(sex, rng, exclude=existing),
                             role="idler", sex=sex, age=0.0,
                             father=father.name if father else "—",
                             mother=mother.name, surname=fam,
                             trait=_traits.assign(rng))
            w.residents.append(baby)
            w.add_news(clock, f"У {mother.name} в доме радость — "
                              f"{baby.v('родился сын', 'родилась дочь')} {baby.name}.")
            return

        # эмиграция — при перенаселении кто-то уходит (чаще из незанятых)
        if pop > cap:
            if rng.random() < T.EMIGRATE_BASE * (pop - cap):
                cand = [c for c in liv if c.role == "idler"] or liv
                leaver = rng.choice(cand)
                w.residents.remove(leaver)
                w.departures.append(leaver)     # мир направит его в другое поселение
                if len(w.departures) > 50:
                    del w.departures[:-50]
                w.add_news(clock, f"{leaver.name} {leaver.v('ушёл', 'ушла')} "
                                  f"искать лучшей доли в других краях.")
