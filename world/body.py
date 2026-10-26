"""
Тело и раны — канонный гибрид «хитпоинты + части тела».

Идея (из бэклога, раздел B): у каждой части тела свой запас прочности, а удар
по ней даёт не просто «минус здоровье», а СИСТЕМНЫЙ эффект — рассечённая рука
слабее бьёт, перебитые ноги не дают убежать, удар по голове путает (мешает
колдовать и трезво решать — смычка с психикой, раздел E). Общий запас жизни
(`Character.health`) остаётся, но теперь боевой урон идёт через конкретную часть
и «переливается» в общий запас только при тяжёлой ране.

Как это делится между клиентами (принцип «ввод отдельно от расчёта»):
- ТОЛСТЫЙ клиент потом нарисует прицеливание по частям (куда бьёшь) — это лишь
  выбор `target`, расчёт тут один и тот же;
- ТОНКИЙ клиент и фон (шальная стрела, бой без игрока) НЕ целятся — часть
  выбирается броском кости по весам уязвимости (`pick_target`), исход решают кости.

Данные раны хранятся на персонаже ПЛОСКИМ словарём `Character.wounds`
(часть -> накопленный урон, 0..max). Пустой словарь = целёхонек. Так состояние
само переживает save/load (`asdict`/`Character(**c)`) без спец-кода и обратной
совместимости: у старых сохранений ключа просто нет — значит, здоров.

Смерти прямо в бою нет (канон: перма-смерть — через нужду/состояния). Тяжёлая
рана роняет в «без сил» (отступление) и может кровоточить — кровь точит общий
запас, пока не отлежишься/перевяжешь. Добивает уже голод/состояние, не удар.
"""
from __future__ import annotations

# Части тела: макс. запас прочности и вес уязвимости (как часто сюда прилетает).
# Торс крупный и открытый — ловит чаще; голова мелкая — реже, но дорого.
PARTS = {
    "head":   dict(name="голова",   max=22.0, weight=1.0),
    "torso":  dict(name="корпус",   max=48.0, weight=4.0),
    "arm_r":  dict(name="правая рука", max=26.0, weight=2.0),  # ведущая — держит оружие
    "arm_l":  dict(name="левая рука",  max=26.0, weight=1.6),
    "legs":   dict(name="ноги",     max=34.0, weight=2.4),
}
ORDER = ["head", "torso", "arm_r", "arm_l", "legs"]

# «Перелив» в общий запас жизни, когда часть добита (доля избыточного урона).
_SPILL = 0.6
# Порог тяжести: часть считается выведенной из строя при уроне >= max.
# Крит (двойной урон в часть) открывает кровотечение.


def max_hp(part: str) -> float:
    p = PARTS.get(part)
    return p["max"] if p else 1.0


def damage_of(ch, part: str) -> float:
    """Накопленный урон части (0 = цела)."""
    return float(getattr(ch, "wounds", {}).get(part, 0.0))


def part_hp(ch, part: str) -> float:
    """Остаток прочности части (0 = выведена из строя)."""
    return max(0.0, max_hp(part) - damage_of(ch, part))


def part_ratio(ch, part: str) -> float:
    """Доля прочности части 0..1 (1 — цела)."""
    return part_hp(ch, part) / max_hp(part)


def disabled(ch, part: str) -> bool:
    return damage_of(ch, part) >= max_hp(part) - 1e-6


def any_wounds(ch) -> bool:
    return any(v > 0.5 for v in getattr(ch, "wounds", {}).values())


# ── системные последствия ран (это и есть «части тела», а не просто HP) ──

def attack_mult(ch) -> float:
    """Множитель силы удара: бьющая рука ранена — бьёшь слабее; боль в корпусе мешает."""
    arm = part_ratio(ch, "arm_r")            # ведущая рука важнее всего
    arm_off = part_ratio(ch, "arm_l")        # вторая рука — немного (щит/хват)
    torso = part_ratio(ch, "torso")
    return max(0.25, 0.45 + 0.4 * arm + 0.1 * arm_off + 0.05 * torso)


def flee_mult(ch) -> float:
    """Множитель прыти (убежать/догнать/скорость в пути): держится на ногах."""
    legs = part_ratio(ch, "legs")
    return max(0.2, 0.3 + 0.7 * legs)


def stability(ch) -> float:
    """Собранность 0..1: удар по голове путает — ниже для магии и против паники (психика)."""
    head = part_ratio(ch, "head")
    return max(0.1, 0.35 + 0.65 * head)


def can_flee(ch) -> bool:
    return not disabled(ch, "legs")


def incapacitated(ch) -> bool:
    """«Без сил»: корпус добит или голова добита — бой окончен отступлением/падением."""
    return disabled(ch, "torso") or disabled(ch, "head")


def bleeding(ch) -> float:
    return float(getattr(ch, "bleeding", 0.0))


def severity(ch) -> float:
    """Совокупная тяжесть ран 0..1 (для сводок и UI)."""
    tot = sum(damage_of(ch, p) for p in PARTS)
    mx = sum(PARTS[p]["max"] for p in PARTS)
    return min(1.0, tot / mx)


# ── нанесение удара ──

def pick_target(ch, stream) -> str:
    """Куда прилетит, если никто не целится: бросок по весам уязвимости.
    Целую часть выбрать вероятнее, чем уже разбитую (бьём по живому)."""
    pairs = []
    for p in ORDER:
        w = PARTS[p]["weight"] * (0.3 + 0.7 * part_ratio(ch, p))
        pairs.append((p, w))
    return stream.weighted(pairs) or "torso"


def apply_hit(ch, amount: float, stream, target: str | None = None,
              crit: bool = False) -> dict:
    """Нанести урон. amount — уже посчитанная «боль» (броня учтена выше).
    target=None → часть выбирается костями (фон/тонкий). crit → тяжелее + кровь.
    Возвращает диегетичную сводку для клиента."""
    if amount <= 0:
        return {"part": "", "dealt": 0.0, "disabled": False, "bleed": False, "text": ""}
    part = target if target in PARTS else pick_target(ch, stream)
    if crit:
        amount *= 1.8

    w = getattr(ch, "wounds", None)
    if w is None:
        w = {}
        ch.wounds = w
    before = w.get(part, 0.0)
    cap = max_hp(part)
    after = before + amount
    w[part] = min(cap, after)
    # перелив тяжёлого урона в общий запас жизни
    overflow = max(0.0, after - cap)
    spill = overflow * _SPILL + amount * 0.25     # даже лёгкий удар чуть точит общий запас
    ch.health = max(0.0, ch.health - spill)

    now_disabled = w[part] >= cap - 1e-6
    # кровь: крит, либо добитая часть (кроме конечностей — те немеют, а не хлещут)
    bleed = crit or (now_disabled and part in ("torso", "head"))
    if bleed:
        ch.bleeding = min(100.0, bleeding(ch) + (6.0 if crit else 4.0))

    pname = PARTS[part]["name"]
    if now_disabled and part == "legs":
        text = "перебиты ноги — не убежать"
    elif now_disabled and part in ("arm_r", "arm_l"):
        text = f"{pname} повисла — удар ослаб"
    elif now_disabled and part == "head":
        text = "оглушён ударом по голове"
    elif now_disabled and part == "torso":
        text = "тяжёлый удар в корпус — валишься"
    elif bleed:
        text = f"кровь из раны — {pname}"
    else:
        text = f"удар в {pname}"
    return {"part": part, "part_name": pname, "dealt": round(amount, 1),
            "disabled": now_disabled, "bleed": bool(bleed),
            "spill": round(spill, 1), "text": text}


# ── заживление и кровопотеря (вешается на дневной/часовой ход) ──

def heal_tick(ch, stream, hours: float = 1.0, resting: bool = False,
              treated: bool = False) -> float:
    """Раны понемногу затягиваются; кровь точит общий запас, пока не остановлена.
    resting — отлёживается (дома/сон) → быстрее. treated — перевязали → стоп крови.
    Возвращает потерю здоровья за тик (для хроники/смерти решает вызывающий)."""
    w = getattr(ch, "wounds", None)
    lost = 0.0

    bl = bleeding(ch)
    if bl > 0:
        if treated:
            ch.bleeding = 0.0
        else:
            # кровь точит жизнь; покой замедляет, но не лечит рану крови сам по себе
            drain = bl * 0.04 * hours * (0.4 if resting else 1.0)
            ch.health = max(0.0, ch.health - drain)
            lost += drain
            # медленно сворачивается сама (шанс), быстрее в покое
            stop = 0.08 * hours * (2.0 if resting else 1.0)
            if stream.chance(min(0.9, stop)):
                ch.bleeding = 0.0
            else:
                ch.bleeding = max(0.0, bl - bl * 0.05 * hours)

    if w:
        rate = (1.6 if resting else 0.7) * hours      # очков заживления в час
        # лёгкая случайность темпа, чтобы раны не заживали по таймеру
        rate *= stream.uniform(0.8, 1.2)
        for part in list(w.keys()):
            v = w[part]
            if v <= 0:
                w.pop(part, None)
                continue
            w[part] = max(0.0, v - rate)
            if w[part] <= 0.01:
                w.pop(part, None)
    return round(lost, 2)


# ── описание для клиента (диегетично, без цифр по умолчанию) ──

def describe(ch) -> list[str]:
    """Короткие строки о ранах — для панели состояния и речи мира."""
    out = []
    for p in ORDER:
        r = part_ratio(ch, p)
        if r >= 0.999:
            continue
        pname = PARTS[p]["name"]
        if r <= 0.001:
            out.append(f"{pname} — не действует")
        elif r < 0.4:
            out.append(f"{pname} — тяжёлая рана")
        elif r < 0.8:
            out.append(f"{pname} — ранение")
        else:
            out.append(f"{pname} — ушиб")
    if bleeding(ch) > 0:
        out.append("кровотечение")
    return out


def state_view(ch) -> dict:
    """Машинно-читаемое состояние тела для клиентов (толстый рисует папердолл ран)."""
    parts = {}
    for p in ORDER:
        parts[p] = {"name": PARTS[p]["name"], "ratio": round(part_ratio(ch, p), 3),
                    "disabled": disabled(ch, p)}
    return {"parts": parts, "bleeding": round(bleeding(ch), 1),
            "severity": round(severity(ch), 3),
            "incapacitated": incapacitated(ch),
            "can_flee": can_flee(ch), "notes": describe(ch)}
