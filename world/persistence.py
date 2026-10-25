"""Сохранение/загрузка мира. Мир переживает рестарт — часы это состояние, не аптайм."""
from __future__ import annotations
from dataclasses import asdict
import json
import os
from .model import Character, Settlement, WorldClock


def save_world(w: Settlement, clock: WorldClock, path: str) -> None:
    data = {"clock": asdict(clock),
            "settlement": {
                "name": w.name, "store": w.store, "axe_wear": w.axe_wear,
                "house_integrity": w.house_integrity, "news": w.news,
                "chronicle": w.chronicle, "graves": w.graves,
                "residents": [asdict(c) for c in w.residents]}}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def load_world(path: str):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    clock = WorldClock(**data["clock"])
    d = data["settlement"]
    w = Settlement(name=d["name"], store=d["store"], axe_wear=d["axe_wear"],
                   house_integrity=d["house_integrity"], news=d["news"],
                   chronicle=d.get("chronicle", []), graves=d.get("graves", 0))
    w.residents = [Character(**c) for c in d["residents"]]
    return w, clock


# ── регион целиком: общий мир из многих деревень (игроки НЕ сюда — см. playerstore) ──
def _settlement_to_dict(s) -> dict:
    d = asdict(s)                                   # все поля-данные деревни
    d["residents"] = [asdict(c) for c in s.residents if c.driver != "player"]
    d["departures"] = [asdict(c) for c in s.departures]
    d["pending_arrivals"] = [asdict(c) for c in s.pending_arrivals]
    d["quests"] = [asdict(q) for q in getattr(s, "quests", [])]
    d["_quest_seq"] = getattr(s, "_quest_seq", 0)
    return d


def _settlement_from_dict(d: dict):
    from .quests import Quest
    quests = d.pop("quests", [])
    seq = d.pop("_quest_seq", 0)
    residents = [Character(**c) for c in d.pop("residents", [])]
    departures = [Character(**c) for c in d.pop("departures", [])]
    pending = [Character(**c) for c in d.pop("pending_arrivals", [])]
    s = Settlement(**d)                              # остальные ключи — поля Settlement
    s.residents, s.departures, s.pending_arrivals = residents, departures, pending
    s.quests = [Quest(**q) for q in quests]
    s._quest_seq = seq
    return s


def region_to_dict(region) -> dict:
    """Чистая сериализация региона в dict (для файла или строки БД)."""
    return {
        "clock": asdict(region.clock),
        "settlements": [_settlement_to_dict(s) for s in region.settlements],
        "wanderers": [asdict(c) for c in region.wanderers],
    }


def region_from_dict(data: dict, seed: int = 1):
    """Собрать регион из dict. Движки создадутся лениво, погода стартует заново."""
    from .region import Region
    setts = [_settlement_from_dict(d) for d in data["settlements"]]
    reg = Region(setts, seed=seed)
    reg.clock.total_hours = data["clock"]["total_hours"]
    reg.wanderers = [Character(**c) for c in data.get("wanderers", [])]
    reg.set_hot(range(len(setts)))
    return reg


def save_region(region, path: str) -> None:
    """Сохранить общий мир в файл. Игроки хранятся отдельно."""
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(region_to_dict(region), f, ensure_ascii=False)
    os.replace(tmp, path)


def load_region(path: str, seed: int = 1):
    """Восстановить регион из файла."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return region_from_dict(data, seed)


def region_exists(path: str) -> bool:
    return os.path.exists(path)
