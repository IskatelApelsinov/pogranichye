"""
Хранилище игроков — ОТДЕЛЬНО от мира. Это принципиально: мир один и общий,
а игроков много, и у каждого своя запись (прогресс, где он сейчас). В прототипе —
JSON-файл на игрока; в бою это станет таблицей players в PostgreSQL (та же форма
данных, другой драйвер). Игрок — обычный Character с driver='player', плюс id
аккаунта и индекс деревни, где он находится.

Так загрузка мира и загрузка игрока независимы: мир тикает сам по себе, игрок
«входит» в него по своему id и встаёт в свою деревню.
"""
from __future__ import annotations
from dataclasses import asdict
import json
import os
from .model import Character

STORE_DIR = "/tmp/game_players"


def _path(pid: str) -> str:
    safe = "".join(c if c.isalnum() else "_" for c in pid)
    return os.path.join(STORE_DIR, f"{safe}.json")


def save(pid: str, char: Character, loc: int) -> None:
    os.makedirs(STORE_DIR, exist_ok=True)
    data = {"pid": pid, "loc": loc, "char": asdict(char)}
    tmp = _path(pid) + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
    os.replace(tmp, _path(pid))        # атомарная запись — не поймаем полусейв


def load(pid: str):
    """Вернёт (Character, loc) или None, если игрок ещё не рождён."""
    try:
        with open(_path(pid), encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError):
        return None
    return Character(**data["char"]), int(data["loc"])


def exists(pid: str) -> bool:
    return os.path.exists(_path(pid))
