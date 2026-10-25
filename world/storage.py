"""
Абстракция хранилища игрока и мира — один интерфейс, два бэкенда:
  JsonStorage — файлы (прототип, как было);
  SqlStorage  — SQL (GameDB: SQLite сейчас, PostgreSQL в проде).

Мир хранится РОДОВЫМ dict-блобом (мир может состоять из нескольких шардов —
сериализацию собирает сам mp.World, хранилище лишь кладёт/достаёт блоб). Игрок —
отдельной записью. Поменять JSON на БД = поменять одну строку выбора бэкенда.
"""
from __future__ import annotations
from dataclasses import asdict
import json, os
from .model import Character
from . import playerstore

WORLD_FILE = "/tmp/game_world.json"


class JsonStorage:
    def __init__(self, world_file: str = WORLD_FILE):
        self.world_file = world_file

    def player_save(self, aid, char, loc):
        playerstore.save(aid, char, loc)

    def player_load(self, aid):
        return playerstore.load(aid)

    def world_exists(self):
        return os.path.exists(self.world_file)

    def world_load(self):
        try:
            with open(self.world_file, encoding="utf-8") as f:
                return json.load(f)
        except (OSError, json.JSONDecodeError):
            return None

    def world_save(self, data: dict):
        tmp = self.world_file + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False)
        os.replace(tmp, self.world_file)


class SqlStorage:
    def __init__(self, db):
        self.db = db

    def player_save(self, aid, char, loc):
        self.db.player_save(aid, json.dumps(asdict(char), ensure_ascii=False), loc)

    def player_load(self, aid):
        r = self.db.player_load(aid)
        if not r:
            return None
        return Character(**json.loads(r["char"])), int(r["loc"])

    def world_exists(self):
        return self.db.world_load() is not None

    def world_load(self):
        data = self.db.world_load()
        return json.loads(data) if data else None

    def world_save(self, data: dict):
        self.db.world_save(json.dumps(data, ensure_ascii=False))
