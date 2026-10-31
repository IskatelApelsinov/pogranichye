"""
События от РЕШЕНИЙ NPC (слой 4 мега-итерации).

Канон: крупное событие — не случайный бросок из воздуха, а решение живого жителя,
созревшее под давлением мира. Порог решения двигают метрики, замаскированные под
настроение: перенаселение и нужда копят раздражение → порог ниже → кто-то решается.
Язык один и тот же — кости + веса (world.dice), как и у остального мира.

Давления копятся из A-life (social/ambition/dwelling) и экономики (еда, долги,
целость домов, недавние смерти). Среди жителей ищется НОСИТЕЛЬ — тот, чьи личные
драйвы складываются с давлением и переваливают порог. Он и «принимает решение»:
исход, война, смута, призыв вожака, ватага на большак, починный сход.

Игрок НЕ диктует исход — он качает ВЕРОЯТНОСТИ: уважаемый игрок рядом гасит смуту
и разбой, поднимает шанс схода и вожака. Тот же язык весов, что у мира.

Старый EventsSystem (случайная погода-флейвор) остаётся для мелкой ряби; крупное
теперь идёт отсюда, с причиной.
"""
from __future__ import annotations
from ..engine import System
from .. import tuning as T

MIN_STAY = 4                    # не опустошаем деревню исходом ниже этого
_BASE_THRESHOLD = 0.55          # ниже суммарного давления событие не вызреет


def _npc_living(w):
    return [c for c in w.living() if c.driver != "player"]


def _player_sway(w) -> float:
    """Влияние присутствующего игрока: уважаемый гасит смуту/разбой, крепит общину.
    Возвращает 0..~1 (доля доброй славы сильнейшего игрока в деревне)."""
    best = 0.0
    for c in w.living():
        if c.driver == "player":
            best = max(best, min(1.0, getattr(c, "reputation", 0.0) / 30.0))
    return best


def _pressures(w) -> dict:
    """Снять давления деревни (0..~1+). Это и есть «настроение», под которым зреют решения."""
    liv = _npc_living(w)
    pop = max(1, w.pop())
    food = w.food_days()                                  # дней еды
    hunger = sum(c.hunger for c in liv) / len(liv) if liv else 0.0
    ambition = sum(c.ambition for c in liv) / len(liv) if liv else 0.0
    idlers = sum(1 for c in liv if (c.role == "idler" or (max(c.skills.values()) if c.skills else 0) < 5))
    debts = sum(sum(c.debts.values()) for c in liv if getattr(c, "debts", None))
    morale = w.avg_mood()
    deaths = min(1.0, w.graves / 8.0)                     # память о смертях давит
    return {
        "crowd":   max(0.0, (pop - 10) / 14.0) + max(0.0, (1.2 - food)) * 0.4,  # тесно/голодно
        "unrest":  max(0.0, (55 - morale) / 55.0) * 0.7 + min(1.0, hunger / 90.0) * 0.5
                   + min(1.0, debts / 120.0) * 0.4,
        "banditry": min(1.0, idlers / max(3, pop * 0.4)) * 0.8 + min(1.0, debts / 150.0) * 0.4,
        "rally":   deaths * 0.8 + max(0.0, (50 - morale) / 50.0) * 0.4,
        "mutual":  max(0.0, (70 - w.house_integrity) / 70.0) * 0.8,
        "exodus":  min(1.0, ambition / 90.0) * 0.7 + max(0.0, (pop - 12) / 12.0) * 0.5,
    }


def _bearer(w, stream, drive_key: str):
    """Найти носителя решения: NPC, чей личный драйв под это событие выше прочих
    (с лёгкой костью, чтобы не всегда один и тот же). Возвращает Character или None."""
    liv = _npc_living(w)
    if not liv:
        return None
    def drive(c):
        base = {
            "exodus":   c.ambition,
            "banditry": c.ambition * (1.3 if (c.role == "idler") else 0.6) + c.hunger * 0.3,
            "unrest":   c.hunger * 0.6 + sum(getattr(c, "debts", {}).values()) * 0.5
                        + (100 - getattr(c, "morale", 60)) * 0.3,
            "rally":    getattr(c, "reputation", 0.0) * 2.0 + (max(c.skills.values()) if c.skills else 0),
            "mutual":   c.social * 0.6 + getattr(c, "dwelling", 0.0) * 0.4,
        }.get(drive_key, 0.0)
        return base * stream.uniform(0.7, 1.3)
    return max(liv, key=drive)


class NpcDecisionSystem(System):
    name = "decisions"

    def on_day(self, w, clock, rng):
        liv = _npc_living(w)
        if len(liv) < 2:
            return
        # опасность дорог сама понемногу спадает (ватаги расходятся, стража крепнет)
        if getattr(w, "road_danger", 0.0) > 0:
            w.road_danger = max(0.0, w.road_danger - 0.15)
        pr = _pressures(w)
        sway = _player_sway(w)
        # игрок гасит смуту/разбой, крепит общину и вожака (качает веса, не исход)
        pr["unrest"]   *= (1.0 - 0.6 * sway)
        pr["banditry"] *= (1.0 - 0.6 * sway)
        pr["rally"]    *= (1.0 + 0.5 * sway)
        pr["mutual"]   *= (1.0 + 0.5 * sway)

        top = max(pr.values())
        if top < 0.20:
            return                                        # в спокойной деревне ничего не зреет
        # вызреет ли вообще решение сегодня — порог тем ниже, чем выше давление
        fire_p = min(0.6, max(0.0, (top - 0.0)) * 0.5)
        if not rng.chance(fire_p) if hasattr(rng, "chance") else (rng.random() > fire_p):
            return
        # какое именно — взвешенный выбор по давлениям (кости + веса)
        kind = rng.weighted([(k, v) for k, v in pr.items()]) if hasattr(rng, "weighted") \
            else max(pr, key=pr.get)
        getattr(self, f"_do_{kind}", self._noop)(w, clock, rng, pr)

    # ── исходы решений ──
    def _noop(self, w, clock, rng, pr):
        pass

    def _do_exodus(self, w, clock, rng, pr):
        bearer = _bearer(w, rng, "exodus")
        if not bearer or len(w.living()) <= MIN_STAY:
            return
        # носитель поднимает ватагу искать лучшей доли — уходит с ним 1–3 единомышленника
        band = [bearer]
        for c in _npc_living(w):
            if c is bearer:
                continue
            if c.ambition >= 55 and (rng.random() < 0.4 if not hasattr(rng, "chance") else rng.chance(0.4)):
                band.append(c)
            if len(band) >= 3:
                break
        moved = 0
        for c in band:
            if len(w.living()) <= MIN_STAY:
                break
            w.residents.remove(c); c.ambition = 0.0
            w.departures.append(c); moved += 1
        del w.departures[:-50]
        who = bearer.full_name()
        if moved > 1:
            w.add_chronicle(clock, f"{who} увёл(а) ватагу ({moved} душ) искать иной доли.")
            w.add_news(clock, f"{bearer.name} подбил(а) соседей — ушли artель искать лучшей земли.")
        else:
            w.add_news(clock, f"{bearer.name} {bearer.v('ушёл','ушла')} искать лучшей доли.")

    def _do_unrest(self, w, clock, rng, pr):
        bearer = _bearer(w, rng, "unrest")
        if not bearer:
            return
        # смута: раздор бьёт по настрою; иногда силой вытрясают долг
        for c in _npc_living(w):
            c.morale = max(0.0, getattr(c, "morale", 60.0) - rng.uniform(4, 10))
        # должник — не сам носитель, и с монетами, чтобы было что вытрясти
        debtors = [c for c in _npc_living(w) if c is not bearer
                   and getattr(c, "debts", None) and c.coins > 0.5]
        if debtors and (rng.chance(0.5) if hasattr(rng, "chance") else rng.random() < 0.5):
            d = max(debtors, key=lambda c: sum(c.debts.values()))
            who_to = max(d.debts, key=d.debts.get)
            paid = min(d.coins, d.debts[who_to])
            d.coins -= paid; d.debts[who_to] -= paid
            if d.debts[who_to] <= 0.1:
                d.debts.pop(who_to, None)
            w.add_news(clock, f"У очага вышла ссора: {bearer.name} попрекнул(а) {d.name} долгом — "
                              f"пришлось отдать {paid:.0f}⌾.")
        else:
            w.add_news(clock, f"В деревне ропот: {bearer.name} в сердцах бранит нужду и порядки.")

    def _do_banditry(self, w, clock, rng, pr):
        bearer = _bearer(w, rng, "banditry")
        if not bearer or len(w.living()) <= MIN_STAY:
            return
        # отчаявшийся/незанятый уходит на большак — дороги станут опаснее
        w.residents.remove(bearer); bearer.ambition = 0.0
        w.departures.append(bearer); del w.departures[:-50]
        w.road_danger = min(3.0, getattr(w, "road_danger", 0.0) + 1.0)
        w.add_chronicle(clock, f"{bearer.full_name()} подался(ась) в лихие люди — на большаке неспокойно.")
        w.add_news(clock, f"{bearer.name} сгинул(а) из деревни — говорят, пристал(а) к разбойникам.")

    def _do_rally(self, w, clock, rng, pr):
        bearer = _bearer(w, rng, "rally")
        if not bearer:
            return
        # после бед община сплачивается вокруг уважаемого — страх отступает, порядок крепнет
        for c in _npc_living(w):
            c.morale = min(100.0, getattr(c, "morale", 60.0) + rng.uniform(4, 9))
        w.road_danger = max(0.0, getattr(w, "road_danger", 0.0) - 1.0)
        bearer.reputation = getattr(bearer, "reputation", 0.0) + 1.0
        w.add_chronicle(clock, f"{bearer.full_name()} поднял(а) людей держаться вместе — деревня приободрилась.")
        w.add_news(clock, f"{bearer.name} сплотил(а) соседей: решили стеречь околицу по очереди.")

    def _do_mutual(self, w, clock, rng, pr):
        bearer = _bearer(w, rng, "mutual")
        if not bearer:
            return
        # починный сход: всем миром латают дома
        w.house_integrity = min(100.0, w.house_integrity + rng.uniform(12, 26))
        for c in _npc_living(w):
            c.dwelling = min(100.0, getattr(c, "dwelling", 0.0) + 6.0)
        w.add_news(clock, f"{bearer.name} {bearer.v('кликнул','кликнула')} помочь — всем миром "
                          f"подлатали дома.")

    def _do_crowd(self, w, clock, rng, pr):
        # теснота сама по себе выливается в исход (переиспользуем)
        self._do_exodus(w, clock, rng, pr)
