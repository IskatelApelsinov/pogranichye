"""
SQL-хранилище. Прото-реализация на SQLite (stdlib sqlite3) — настоящий SQL,
тестируется прямо здесь, без сервера БД. Прод-цель — PostgreSQL (канон): тот же
набор таблиц и запросов, отличия диалекта помечены в DDL ниже.

Что лежит в БД (по проекту set-architecture): аккаунты и сессии — строками;
игрок — строка на аккаунт (тело персонажа в JSON); мир — снимок (одна строка).
Горячая симуляция живёт в RAM; БД — durable-хранилище снимков и строк.

Переносимость на Postgres:
  * INTEGER PRIMARY KEY AUTOINCREMENT  → SERIAL/BIGSERIAL (здесь не нужен — ключи свои);
  * TEXT под JSON                       → JSONB (эффективнее, с индексами);
  * параметр-метка '?'                  → '%s' (psycopg) — одна точка замены в _q().
"""
from __future__ import annotations
import sqlite3, threading, json

DDL = """
CREATE TABLE IF NOT EXISTS accounts(
  id TEXT PRIMARY KEY, email TEXT UNIQUE, pass_hash TEXT, created REAL, status TEXT);
CREATE TABLE IF NOT EXISTS sessions(
  token TEXT PRIMARY KEY, account_id TEXT, expires REAL);
CREATE TABLE IF NOT EXISTS players(
  account_id TEXT PRIMARY KEY, loc INTEGER, char TEXT);
CREATE TABLE IF NOT EXISTS world(
  id INTEGER PRIMARY KEY CHECK(id=1), data TEXT);
"""


class GameDB:
    def __init__(self, path: str = "/tmp/game.db"):
        self.lock = threading.Lock()
        self.cx = sqlite3.connect(path, check_same_thread=False)
        self.cx.row_factory = sqlite3.Row
        with self.lock:
            self.cx.executescript(DDL)
            self.cx.commit()

    def _run(self, sql, args=(), *, fetch=None):
        with self.lock:
            cur = self.cx.execute(sql, args)
            if fetch == "one":
                row = cur.fetchone()
                self.cx.commit()
                return dict(row) if row else None
            if fetch == "all":
                rows = [dict(r) for r in cur.fetchall()]
                self.cx.commit()
                return rows
            self.cx.commit()

    # ── аккаунты / сессии ──
    def acc_create(self, aid, email, pass_hash, status, created):
        self._run("INSERT OR REPLACE INTO accounts(id,email,pass_hash,status,created) VALUES(?,?,?,?,?)",
                  (aid, email, pass_hash, status, created))

    def acc_get(self, aid):
        return self._run("SELECT * FROM accounts WHERE id=?", (aid,), fetch="one")

    def acc_by_email(self, email):
        r = self._run("SELECT id FROM accounts WHERE email=?", (email,), fetch="one")
        return r["id"] if r else None

    def sess_add(self, token, aid, expires):
        self._run("INSERT OR REPLACE INTO sessions(token,account_id,expires) VALUES(?,?,?)",
                  (token, aid, expires))

    def sess_get(self, token):
        return self._run("SELECT * FROM sessions WHERE token=?", (token,), fetch="one")

    def sess_del(self, token):
        self._run("DELETE FROM sessions WHERE token=?", (token,))

    # ── игроки ──
    def player_save(self, aid, char_json, loc):
        self._run("INSERT OR REPLACE INTO players(account_id,loc,char) VALUES(?,?,?)",
                  (aid, loc, char_json))

    def player_load(self, aid):
        return self._run("SELECT loc,char FROM players WHERE account_id=?", (aid,), fetch="one")

    # ── мир (снимок) ──
    def world_save(self, data_json):
        self._run("INSERT OR REPLACE INTO world(id,data) VALUES(1,?)", (data_json,))

    def world_load(self):
        r = self._run("SELECT data FROM world WHERE id=1", fetch="one")
        return r["data"] if r else None


# ── PostgreSQL-бэкенд: тот же интерфейс, что GameDB, но на psycopg2 ──
DDL_PG = """
CREATE TABLE IF NOT EXISTS accounts(
  id TEXT PRIMARY KEY, email TEXT UNIQUE, pass_hash TEXT, created DOUBLE PRECISION, status TEXT);
CREATE TABLE IF NOT EXISTS sessions(
  token TEXT PRIMARY KEY, account_id TEXT, expires DOUBLE PRECISION);
CREATE TABLE IF NOT EXISTS players(
  account_id TEXT PRIMARY KEY, loc INTEGER, char JSONB);
CREATE TABLE IF NOT EXISTS world(
  id INTEGER PRIMARY KEY CHECK(id=1), data JSONB);
"""


class GameDBPostgres:
    """Прод-хранилище на PostgreSQL. Интерфейс совпадает с GameDB (SQLite), поэтому
    storage.SqlStorage и accounts.AccountsDB работают без изменений. Отличия: метка
    параметра %s, апсерт через ON CONFLICT, char/data как JSONB."""
    def __init__(self, dsn: str):
        import psycopg2
        import psycopg2.extras
        self._pe = psycopg2.extras
        self.lock = threading.Lock()
        self.cx = psycopg2.connect(dsn)
        self.cx.autocommit = True
        with self.lock, self.cx.cursor() as cur:
            cur.execute(DDL_PG)

    def _one(self, sql, args=()):
        with self.lock, self.cx.cursor(cursor_factory=self._pe.RealDictCursor) as cur:
            cur.execute(sql, args)
            row = cur.fetchone()
            return dict(row) if row else None

    def _exec(self, sql, args=()):
        with self.lock, self.cx.cursor() as cur:
            cur.execute(sql, args)

    def acc_create(self, aid, email, pass_hash, status, created):
        self._exec("""INSERT INTO accounts(id,email,pass_hash,status,created) VALUES(%s,%s,%s,%s,%s)
                      ON CONFLICT (id) DO UPDATE SET email=EXCLUDED.email,
                      pass_hash=EXCLUDED.pass_hash,status=EXCLUDED.status""",
                   (aid, email, pass_hash, status, created))

    def acc_get(self, aid):
        return self._one("SELECT * FROM accounts WHERE id=%s", (aid,))

    def acc_by_email(self, email):
        r = self._one("SELECT id FROM accounts WHERE email=%s", (email,))
        return r["id"] if r else None

    def sess_add(self, token, aid, expires):
        self._exec("""INSERT INTO sessions(token,account_id,expires) VALUES(%s,%s,%s)
                      ON CONFLICT (token) DO UPDATE SET expires=EXCLUDED.expires""",
                   (token, aid, expires))

    def sess_get(self, token):
        return self._one("SELECT * FROM sessions WHERE token=%s", (token,))

    def sess_del(self, token):
        self._exec("DELETE FROM sessions WHERE token=%s", (token,))

    def player_save(self, aid, char_json, loc):
        self._exec("""INSERT INTO players(account_id,loc,char) VALUES(%s,%s,%s)
                      ON CONFLICT (account_id) DO UPDATE SET loc=EXCLUDED.loc,char=EXCLUDED.char""",
                   (aid, loc, char_json))

    def player_load(self, aid):
        r = self._one("SELECT loc,char FROM players WHERE account_id=%s", (aid,))
        if r and not isinstance(r["char"], str):
            r["char"] = json.dumps(r["char"])     # JSONB → строка (storage ждёт json.loads)
        return r

    def world_save(self, data_json):
        self._exec("""INSERT INTO world(id,data) VALUES(1,%s)
                      ON CONFLICT (id) DO UPDATE SET data=EXCLUDED.data""", (data_json,))

    def world_load(self):
        r = self._one("SELECT data FROM world WHERE id=1")
        if not r:
            return None
        return r["data"] if isinstance(r["data"], str) else json.dumps(r["data"])
