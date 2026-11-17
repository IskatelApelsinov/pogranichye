"""
Архивация холодных поселений. Горячее поселение держит каждого жителя полным
агентом в RAM. Когда оно остывает — жители ВЫГРУЖАЮТСЯ из RAM на диск, в памяти
остаётся лишь число (archived_pop) + агрегатные склад/дома/новости (они дёшевы).
При разогреве жители ВОССТАНАВЛИВАЮТСЯ в полных агентов.

Так в RAM живут телами лишь горячие области; холодный мир (хоть вся планета) —
это числа и файлы. Тождество сохраняется: выживших возвращаем тех же; за время
холода население могло измениться (рождения/смерти/миграция) — согласуем число.
"""
from __future__ import annotations
from dataclasses import asdict
import json
import os
from .model import Character
from .content import BABY_NAMES

ARCHIVE_DIR = "/tmp/game_archive"


def _path(name: str) -> str:
    safe = "".join(c if c.isalnum() else "_" for c in name)
    return os.path.join(ARCHIVE_DIR, f"{safe}.json")


def archive_settlement(s) -> None:
    """Горячее → холодное: выгрузить жителей на диск, освободить RAM."""
    if s.archived:
        return
    os.makedirs(ARCHIVE_DIR, exist_ok=True)
    living = s.living()
    with open(_path(s.name), "w", encoding="utf-8") as f:
        json.dump([asdict(c) for c in living], f, ensure_ascii=False)
    s.archived_pop = len(living)
    s.residents = []                 # тела ушли из RAM
    s.pending_arrivals = []
    s.archived = True


def restore_settlement(s) -> None:
    """Холодное → горячее: вернуть жителей в RAM полными агентами, согласовав число."""
    if not s.archived:
        return
    try:
        with open(_path(s.name), encoding="utf-8") as f:
            raw = json.load(f)
    except (OSError, json.JSONDecodeError):
        raw = []
    people = [Character(**c) for c in raw]

    target = s.archived_pop                      # сколько должно быть по архиву
    # согласование: кто-то мог умереть/уйти (убавить) или родиться (добавить) в холоде
    if len(people) > target:
        people = people[:target]                 # выжили первые target (тождество сохранено)
    while len(people) < target:
        people.append(Character(name=_newborn(people), role="idler"))
    people.extend(s.pending_arrivals)            # пришедшие за время холода

    s.residents = people
    s.pending_arrivals = []
    s.archived = False
    s.archived_pop = 0


def _newborn(existing) -> str:
    base = BABY_NAMES[len(existing) % len(BABY_NAMES)]
    return base
