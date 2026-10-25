"""Труд и экономика: NPC выбирает занятие по нужде поселения (utility AI),
производство следует за спросом. Цепочка дровосека живёт здесь."""
from __future__ import annotations
from .. import tuning as T
from .. import player as PL
from .. import items as IT
from ..content import ACT_RU
from ..engine import System
from ..memory import record_milestone


def _afk_tick(ch, s, rng, clock) -> str:
    """Офлайн-игрок под AI: трудится сам по заданному ПРИОРИТЕТУ, пользуется своими
    предметами и ТОЛЬКО прибавляет (не теряя старого). Пишет дневник занятий, чтобы
    вернувшийся игрок увидел, чем жил его персонаж в отлучке."""
    mode = getattr(ch, "priority", "balanced")
    if mode == "full_rest":
        ch.fatigue = max(0.0, ch.fatigue - 5.0)
        return "rest"
    work_chance = {"work": 0.9, "balanced": 0.55, "rest": 0.25}.get(mode, 0.55)
    if rng.random() > work_chance:
        ch.fatigue = max(0.0, ch.fatigue - 5.0)
        return "rest"
    best = max(PL.ACTIVITIES, key=lambda a: ch.skill(PL.ACTIVITIES[a]["skill"]))
    r = PL.do_activity(s, ch, best, 0.4, rng)        # пониже качество → скромнее доход
    if rng.random() < 0.25:                          # иногда немного добычи — себе в груз
        res = PL.ACTIVITIES[best]["resource"]
        if res != "_house":
            ch.cargo[res] = ch.cargo.get(res, 0) + 1
    act = r.get("activity", "leisure")
    # дневник отлучки — кратко, не чаще раза в игровой день
    label = PL.ACTIVITIES[best]["label"]
    entry = f"День {clock.day}: {label.lower()}" + (f", +{r.get('pay')}⌾" if r.get("pay") else "")
    if not ch.journal or not ch.journal[-1].startswith(f"День {clock.day}:"):
        ch.journal.append(entry)
        if len(ch.journal) > 20:
            ch.journal = ch.journal[-20:]
    return act


def decide_work(ch, s) -> str:
    needs = s.needs()
    util = {
        "chop":   needs["wood"],
        "cook":   needs["food"] if s.store["raw"] >= T.COOK_RAW else 0,
        "fish":   needs["food"] * 0.9,
        "repair": needs["repair"],
    }
    if needs["tool"] > 0 or s.store["tools"] < 2:
        util["smith"] = max(needs["tool"], 40 if s.store["tools"] < 2 else 0)
    # охотник кормит деревню дичью (raw) — ещё один путь к еде
    if ch.role == "hunter":
        util["hunt"] = needs["food"] * 0.85
    bias = {"woodcutter": "chop", "smith": "smith", "cook": "cook",
            "fisher": "fish", "hunter": "hunt", "herbalist": "forage"}.get(ch.role)
    if bias and bias in util:
        util[bias] += 8
    best = max(util, key=util.get)
    base = {"chop": needs["wood"], "cook": needs["food"], "fish": needs["food"],
            "repair": needs["repair"], "smith": needs["tool"],
            "hunt": needs["food"]}.get(best, 0)
    if base < T.WORK_THRESHOLD and not (best == "smith" and s.store["tools"] < 2):
        return _secondary_job(ch, s)      # ядро сыто — вторичное: медовуха, травы, свой дом
    return best


def _secondary_job(ch, s) -> str:
    """Когда основные нужды закрыты — продуктивный труд: медовуха, травы, и НУЖДА
    обустраивать СВОЙ дом (A-LIFE: люди вкладываются в своё жильё)."""
    pop = max(1, s.pop())
    mead_want = pop * T.MEAD_BUFFER_PER_POP
    if s.store.get("mead", 0) < mead_want:
        if s.store.get("honey", 0) >= T.BREW_HONEY and s.store["wood"] >= T.BREW_FIREWOOD:
            return "brew"
        if s.store.get("honey", 0) < pop:
            return "gather"
    if ch.role == "herbalist" and s.store.get("herb", 0) < pop:
        return "forage"
    # нужда обустроить свой дом — если он недоустроен и есть лишнее дерево
    if getattr(ch, "dwelling", 0.0) < T.DWELL_TARGET and s.store["wood"] >= T.HOMEBUILD_WOOD + 10:
        return "homebuild"
    return "leisure"


def choose_job(ch, s) -> str:
    """Что агент собирается делать. Игрок правит через command, AI — через utility.
    Один код для всех: 'каждый может быть игроком' — разница лишь в источнике решения."""
    if ch.driver == "player" and ch.command:
        return ch.command
    return decide_work(ch, s)


# заработок NPC за продуктивный труд: деревня платит за вклад в общий котёл.
# Небольшая монета в час растёт с мастерством — так у жителей копятся деньги,
# которые они тратят на снаряжение и товары (экономика оживает).
_WAGE_ACTS = {"chop": 0.5, "smith": 0.9, "repair-axe": 0.9, "cook": 0.6,
              "fish": 0.5, "repair": 0.5, "gather": 0.4, "brew": 0.7,
              "hunt": 0.8, "forage": 0.5}
_SKILL_FOR = {"chop": "chopping", "smith": "smithing", "repair-axe": "smithing",
              "cook": "cooking", "fish": "fishing", "repair": "building",
              "gather": "gathering", "brew": "brewing", "hunt": "hunting",
              "forage": "herbalism"}


def _pay_npc(ch, s, act, rng) -> None:
    base = _WAGE_ACTS.get(act)
    if not base:
        return
    skill = ch.skill(_SKILL_FOR.get(act, ""))
    wage = base * (1.0 + skill / 80.0) * getattr(s, "market", 1.0)
    # платят не каждый час — часть труда «на общее благо», как и у игрока
    if rng.random() < 0.6:
        ch.coins += round(wage, 1)


def do_work(ch, s) -> str:
    job = choose_job(ch, s)
    if job == "leisure":
        return "leisure"
    if job == "chop":
        rate = T.CHOP_WOOD * (0.5 if s.axe_wear >= T.AXE_BROKEN_AT else 1.0)
        rate *= 1.0 + ch.skill("chopping") / 100.0
        s.store["wood"] += rate
        s.axe_wear += T.CHOP_AXE_WEAR
        ch.train("chopping", 1.0)
        return "chop"
    if job == "smith":
        if s.axe_wear >= T.AXE_BROKEN_AT and s.store["wood"] >= T.SMITH_TOOL_WOOD:
            s.store["wood"] -= T.SMITH_TOOL_WOOD
            s.axe_wear = 0.0
            ch.train("smithing", 1.0)
            return "repair-axe"
        if s.store["wood"] >= T.SMITH_TOOL_WOOD:
            s.store["wood"] -= T.SMITH_TOOL_WOOD
            s.store["tools"] += 1.0
            ch.train("smithing", 1.0)
            return "smith"
        return "leisure"
    if job == "cook":
        if s.store["raw"] >= T.COOK_RAW and s.store["wood"] >= T.COOK_FIREWOOD:
            s.store["raw"] -= T.COOK_RAW
            s.store["wood"] -= T.COOK_FIREWOOD
            s.store["food"] += T.COOK_FOOD * (1.0 + ch.skill("cooking") / 100.0)
            ch.train("cooking", 1.0)
            return "cook"
        return "leisure"
    if job == "fish":
        s.store["raw"] += T.FISH_RAW * (1.0 + ch.skill("fishing") / 100.0)
        ch.train("fishing", 1.0)
        return "fish"
    if job == "repair":
        if s.store["wood"] >= 2:
            s.store["wood"] -= 2
            s.house_integrity = min(100.0, s.house_integrity + 5)
            return "repair"
        return "leisure"
    if job == "gather":
        s.store["honey"] = s.store.get("honey", 0) + T.GATHER_HONEY * (1.0 + ch.skill("gathering") / 100.0)
        ch.train("gathering", 1.0)
        return "gather"
    if job == "brew":
        if s.store.get("honey", 0) >= T.BREW_HONEY and s.store["wood"] >= T.BREW_FIREWOOD:
            s.store["honey"] -= T.BREW_HONEY
            s.store["wood"] -= T.BREW_FIREWOOD
            s.store["mead"] = s.store.get("mead", 0) + T.BREW_MEAD * (1.0 + ch.skill("brewing") / 100.0)
            ch.train("brewing", 1.0)
            return "brew"
        return "leisure"
    if job == "hunt":
        s.store["raw"] += T.HUNT_RAW * (1.0 + ch.skill("hunting") / 100.0)
        ch.train("hunting", 1.0)
        return "hunt"
    if job == "forage":
        s.store["herb"] = s.store.get("herb", 0) + T.FORAGE_HERB * (1.0 + ch.skill("herbalism") / 100.0)
        ch.train("herbalism", 1.0)
        return "forage"
    if job == "homebuild":
        if s.store["wood"] >= T.HOMEBUILD_WOOD:
            s.store["wood"] -= T.HOMEBUILD_WOOD
            ch.dwelling = min(100.0, ch.dwelling + T.HOMEBUILD_GAIN * (1.0 + ch.skill("building") / 100.0))
            ch.train("building", 1.0)
            return "homebuild"
        return "leisure"
    return "leisure"


class WorkSystem(System):
    name = "work"

    def on_hour(self, w, clock, rng):
        hour = clock.hour
        if hour in T.SLEEP_HOURS:
            return                       # спящими занимается NeedsSystem
        for ch in w.living():
            # офлайн-игрок под AI: сам трудится своими предметами, только прибавляя
            if getattr(ch, "afk", False):
                if hour in T.WORK_HOURS and ch.fatigue < 90:
                    act = _afk_tick(ch, w, rng, clock)
                    ch.last_act = ACT_RU.get(act, act)
                    record_milestone(w, ch, clock)
                else:
                    ch.last_act = "отдыхает (ты в отлучке)"
                continue
            # живой игрок без приказа ничего не делает сам — ждёт решения
            if ch.driver == "player" and not ch.command:
                ch.last_act = "ждёт твоего решения"
                continue
            if hour in T.WORK_HOURS and ch.fatigue < 90:
                act = do_work(ch, w)
                ch.last_act = ACT_RU.get(act, act)
                _pay_npc(ch, w, act, rng)           # НПС тоже зарабатывают за труд
                record_milestone(w, ch, clock)      # дорос до мастера — мир запомнит
            else:
                ch.last_act = "отдыхает"
