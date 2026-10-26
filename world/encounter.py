"""
Схватка как тактический обмен раундами (слой 2 мега-итерации).

Раньше бой был одним броском «качество мини-игры → исход». Теперь это короткий
поединок по раундам: игрок выбирает ДЕЙСТВИЕ (ударить в часть тела / защититься /
уклониться / бежать), мир считает исход раунда на костях и навыке, обе стороны
копят урон. Это и есть заявленное «клик-таргет + уклонение/парир».

Принцип «ВВОД ОТДЕЛЁН ОТ РАСЧЁТА» соблюдён строго: engine принимает уже готовое
действие {kind, target, quality}. Откуда оно взялось — кнопка тонкого клиента,
клик по части тела в изометрии, позже прицел от 1-го лица — движок не знает и
знать не должен. Переход на толстый клиент/FP НЕ трогает эту математику.

Состояние боя — ПЛОСКИЙ словарь (хранится на слоте игрока у mp, сериализуется
как есть). Зверь — простой запас сил + мощь; игрок получает настоящие раны по
частям тела (world.body). Присутствует игрок → решает его выбор+навык+кости;
фон/отсутствие → всё решают кости (те же функции, quality=0).
"""
from __future__ import annotations
from . import items as _items
from . import body as _body
from . import combat as _combat

# базовый запас сил зверя выводим из его боевой мощи (power) — крупный зверь дольше держится.
# Бой идёт по раундам, поэтому запас скромнее одноударного: целим в ~4–8 раундов с оружием.
_HP_FROM_POWER = 5.0
# Ответ зверя бьёт КАЖДЫЙ раунд: держим так, чтобы затяжной бой был опасен (нельзя
# «замотать» медведя слабым ножом без потерь) — но быстрый точный бой почти цел.
_FOE_PER_ROUND = 0.8


def start(ch, beast_id: str, reason: str = "hunt") -> dict:
    """Создать состояние схватки. Возвращает сериализуемый словарь."""
    b = _combat.BEASTS.get(beast_id)
    if not b:
        return {}
    hp = round(b["power"] * _HP_FROM_POWER, 1)
    return {
        "beast": beast_id, "name": b["name"], "icon": b["icon"],
        "power": b["power"], "danger": b["danger"], "skill": b["skill"],
        "reason": reason,
        "hp": hp, "hp_max": hp,
        "round": 0, "over": False, "won": False, "fled": False,
        "stagger": 0,          # зверь оглушён на N раундов (пропускает ответ)
        "log": [],             # строки раунда для клиента
    }


def _foe_attack(ch, enc, stream, dodging: float = 0.0, guard: float = 0.0) -> dict:
    """Ответ зверя по игроку. dodging — шанс увернуться (0..1), guard — смягчение (0..1)."""
    if enc.get("stagger", 0) > 0:
        enc["stagger"] -= 1
        return {"text": f"{enc['icon']} отшатнулся — не достал", "dealt": 0.0}
    # увернулся целиком?
    if dodging > 0 and stream.chance(dodging):
        return {"text": "ты уклонился от удара", "dealt": 0.0, "dodged": True}
    armor = _items.warmth(ch) * 0.4
    raw = enc["danger"] * _FOE_PER_ROUND * stream.uniform(0.7, 1.25)
    hurt = max(0.0, raw * (1.0 - guard) - armor)
    hurt *= (1.0 - _items.trinket_stat(ch, "guard"))
    ch.fatigue = min(100.0, ch.fatigue + hurt * 0.3)
    crit = stream.chance(0.08 + 0.04 * (enc["power"] / 12.0))
    wound = _body.apply_hit(ch, hurt, stream, crit=crit) if hurt > 0.4 else {}
    return {"text": (wound.get("text") or ("удар вскользь" if hurt else "промах зверя")),
            "dealt": round(hurt, 1), "wound": wound}


def act(ch, enc: dict, action: dict, stream) -> dict:
    """Один раунд. action = {kind: strike|guard|dodge|flee, target?: part, quality?: 0..1}.
    Мутирует ch (раны/навык/усталость) и enc. Возвращает сводку раунда."""
    if not enc or enc.get("over"):
        return {"ok": False, "msg": "боя нет"}
    kind = action.get("kind", "strike")
    quality = max(0.0, min(1.0, float(action.get("quality", 0.0))))
    enc["round"] += 1
    rl = []

    if kind == "flee":
        chance = 0.35 + 0.5 * _body.flee_mult(ch)
        if not _body.can_flee(ch):
            rl.append("ноги не держат — не убежать!")
            foe = _foe_attack(ch, enc, stream, guard=0.1)
            rl.append(foe["text"])
        elif stream.chance(chance):
            enc["over"] = True; enc["fled"] = True
            rl.append("ты вышел из боя")
            enc["log"] = rl
            return _round_result(ch, enc, kind, rl)
        else:
            rl.append("уйти не вышло — зверь настиг")
            foe = _foe_attack(ch, enc, stream)
            rl.append(foe["text"])
        _check_player_down(ch, enc, rl)
        enc["log"] = rl
        return _round_result(ch, enc, kind, rl)

    if kind == "guard":
        # глухая защита: сильно режет ответ, шанс контрудара, растит навык чуть
        ch.train(enc["skill"], 0.3)
        guard = 0.65
        riposte = 0.25 + 0.45 * quality
        foe = _foe_attack(ch, enc, stream, guard=guard)
        rl.append("ты закрылся — " + foe["text"])
        if stream.chance(riposte):
            dmg = _combat.attack_power(ch, 0.5) * stream.uniform(0.6, 1.0)
            enc["hp"] = max(0.0, enc["hp"] - dmg)
            rl.append(f"и ответил на отскоке (−{dmg:.0f})")
        _check_foe_down(ch, enc, rl)
        _check_player_down(ch, enc, rl)
        enc["log"] = rl
        return _round_result(ch, enc, kind, rl)

    if kind == "dodge":
        # уклон: не бьёшь, но высокий шанс увернуться; держится на ногах и собранности
        ch.train(enc["skill"], 0.2)
        dodge = min(0.9, 0.3 + 0.4 * _body.flee_mult(ch) + 0.3 * quality)
        foe = _foe_attack(ch, enc, stream, dodging=dodge)
        rl.append(foe["text"] if foe.get("dodged") else ("не увернулся — " + foe["text"]))
        _check_player_down(ch, enc, rl)
        enc["log"] = rl
        return _round_result(ch, enc, kind, rl)

    # strike — удар; target пока влияет на зверя лишь как выбор (у зверя нет частей),
    # но несёт смысл для будущего PvP/людей и для текста. Качество от мини-игры/навыка.
    ch.train(enc["skill"], 0.5 + quality)
    atk = _combat.attack_power(ch, quality) * stream.uniform(0.8, 1.15)
    enc["hp"] = max(0.0, enc["hp"] - atk)
    tgt = action.get("target")
    where = f" в {_body.PARTS[tgt]['acc']}" if tgt in _body.PARTS else ""
    rl.append(f"ты бьёшь{where} (−{atk:.0f})")
    # сильный удар может оглушить зверя (пропустит ответ)
    if stream.chance(min(0.4, 0.12 + atk / (enc['power'] * 10.0))):
        enc["stagger"] = 1
        rl.append(f"{enc['icon']} оглушён")
    if not _check_foe_down(ch, enc, rl):
        foe = _foe_attack(ch, enc, stream)
        rl.append(foe["text"])
        _check_player_down(ch, enc, rl)
    enc["log"] = rl
    return _round_result(ch, enc, kind, rl)


def _check_foe_down(ch, enc, rl) -> bool:
    if enc["hp"] <= 0 and not enc.get("over"):
        enc["over"] = True; enc["won"] = True
        rl.append(f"{enc['icon']} {enc['name']} повержен!")
        return True
    return enc.get("over", False)


def _check_player_down(ch, enc, rl) -> None:
    if _body.incapacitated(ch) and not enc.get("over"):
        enc["over"] = True; enc["won"] = False
        rl.append("ты теряешь силы и отступаешь")


def _round_result(ch, enc, kind, rl) -> dict:
    out = {"ok": True, "kind": kind, "round": enc["round"],
           "foe_hp": round(enc["hp"], 1), "foe_hp_max": enc["hp_max"],
           "over": enc["over"], "won": enc.get("won", False),
           "fled": enc.get("fled", False), "lines": rl,
           "body": _body.state_view(ch), "msg": "; ".join(rl)}
    if enc["over"]:
        out["reward"] = _settle(ch, enc) if enc.get("won") else {}
    return out


def _settle(ch, enc) -> dict:
    """Награда за победу — добыча/монеты/навык, как в прежнем combat (но без нового урона)."""
    b = _combat.BEASTS.get(enc["beast"], {})
    gained = {}
    for res, amt in b.get("loot", {}).items():
        take = round(amt * 0.8)
        if take:
            ch.cargo[res] = ch.cargo.get(res, 0) + take
            gained[res] = take
    pay = round(b.get("coins", 0) * 0.9, 1)
    ch.coins += pay
    return {"loot": gained, "coins_gain": pay, "coins": round(ch.coins, 1),
            "skill": enc["skill"], "skill_level": round(ch.skill(enc["skill"]), 1)}


def view(ch, enc: dict) -> dict:
    """Состояние боя для клиента (кнопки/иконки рисует клиент)."""
    if not enc:
        return {}
    return {"name": enc["name"], "icon": enc["icon"],
            "hp": round(enc["hp"], 1), "hp_max": enc["hp_max"],
            "hp_ratio": round(enc["hp"] / enc["hp_max"], 3) if enc["hp_max"] else 0.0,
            "round": enc["round"], "over": enc["over"], "won": enc.get("won", False),
            "fled": enc.get("fled", False),
            "can_flee": _body.can_flee(ch),
            "body": _body.state_view(ch),
            "log": enc.get("log", [])}
