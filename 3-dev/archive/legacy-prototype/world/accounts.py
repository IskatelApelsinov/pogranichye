"""
Аккаунты и сессии — слой входа. Прото-таблицы accounts/sessions в одном JSON
(в бою → PostgreSQL за тем же интерфейсом). Три уровня входа из проекта:
  guest()     — аноним: сразу в игру, выдаём аккаунт без почты + токен-пропуск;
  register()  — закрепить аккаунт: почта + пароль (хранится только pbkdf2-хэш);
  login()     — почта + пароль → новая сессия.

Пароль НИКОГДА не хранится — только необратимый отпечаток (pbkdf2_hmac, stdlib;
в бою желательно argon2id). Сессия — токен со сроком, отзываемый (в отличие от
самоподписанного JWT). Один аккаунт — один персонаж (решение Kate): связь
аккаунт↔персонаж держит уже существующий playerstore по account_id.
"""
from __future__ import annotations
import json, os, time, hmac, hashlib, secrets

STORE = "/tmp/game_accounts.json"
SESSION_TTL = 30 * 24 * 3600        # 30 дней
_PBKDF_ROUNDS = 200_000


def _hash_pw(pw: str) -> str:
    salt = os.urandom(16)
    dk = hashlib.pbkdf2_hmac("sha256", pw.encode(), salt, _PBKDF_ROUNDS)
    return f"{salt.hex()}${dk.hex()}"


def _verify_pw(pw: str, stored: str) -> bool:
    try:
        salt_hex, dk_hex = stored.split("$")
        dk = hashlib.pbkdf2_hmac("sha256", pw.encode(), bytes.fromhex(salt_hex), _PBKDF_ROUNDS)
        return hmac.compare_digest(dk.hex(), dk_hex)
    except Exception:
        return False


class Accounts:
    def __init__(self, path: str = STORE):
        self.path = path
        self.data = {"accounts": {}, "by_email": {}, "sessions": {}}
        self._load()

    def _load(self):
        try:
            with open(self.path, encoding="utf-8") as f:
                self.data = json.load(f)
        except (OSError, json.JSONDecodeError):
            pass

    def _save(self):
        tmp = self.path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(self.data, f, ensure_ascii=False)
        os.replace(tmp, self.path)

    # ── создание/вход ──
    def guest(self) -> dict:
        """Аноним: новый аккаунт без почты + сессия. Играть можно сразу."""
        aid = "a_" + secrets.token_hex(8)
        self.data["accounts"][aid] = {"email": None, "pass_hash": None,
                                      "created": time.time(), "status": "guest"}
        tok = self._new_session(aid)
        self._save()
        return {"ok": True, "account_id": aid, "token": tok, "status": "guest"}

    def register(self, email: str, password: str, account_id: str | None = None) -> dict:
        """Закрепить аккаунт за почтой. Если передан account_id гостя — апгрейдим его
        (прогресс сохраняется); иначе создаём новый."""
        email = (email or "").strip().lower()
        if "@" not in email or len(password) < 4:
            return {"ok": False, "msg": "нужна почта и пароль от 4 символов"}
        if email in self.data["by_email"]:
            return {"ok": False, "msg": "почта уже занята"}
        if account_id and account_id in self.data["accounts"]:
            aid = account_id                       # апгрейд гостя → прогресс остаётся
        else:
            aid = "a_" + secrets.token_hex(8)
            self.data["accounts"][aid] = {"created": time.time()}
        acc = self.data["accounts"][aid]
        acc.update(email=email, pass_hash=_hash_pw(password), status="registered")
        self.data["by_email"][email] = aid
        tok = self._new_session(aid)
        self._save()
        return {"ok": True, "account_id": aid, "token": tok, "status": "registered"}

    def login(self, email: str, password: str) -> dict:
        email = (email or "").strip().lower()
        aid = self.data["by_email"].get(email)
        acc = self.data["accounts"].get(aid) if aid else None
        if not acc or not _verify_pw(password, acc.get("pass_hash") or ""):
            return {"ok": False, "msg": "неверная почта или пароль"}
        tok = self._new_session(aid)
        self._save()
        return {"ok": True, "account_id": aid, "token": tok, "status": acc.get("status")}

    def logout(self, token: str) -> None:
        if token in self.data["sessions"]:
            del self.data["sessions"][token]
            self._save()

    # ── сессии ──
    def _new_session(self, aid: str) -> str:
        tok = secrets.token_urlsafe(24)
        self.data["sessions"][tok] = {"account_id": aid, "expires": time.time() + SESSION_TTL}
        return tok

    def resolve(self, token: str) -> str | None:
        """Токен → account_id (или None, если нет/просрочен)."""
        s = self.data["sessions"].get(token or "")
        if not s:
            return None
        if s["expires"] < time.time():
            del self.data["sessions"][token]
            self._save()
            return None
        return s["account_id"]

    def status(self, aid: str) -> str:
        acc = self.data["accounts"].get(aid)
        return acc.get("status", "guest") if acc else "none"


class AccountsDB:
    """То же, что Accounts, но на SQL (GameDB). Тот же интерфейс — сервер не видит
    разницы. Логика хэша/сессий общая (функции модуля)."""
    def __init__(self, db):
        self.db = db

    def _new_session(self, aid: str) -> str:
        tok = secrets.token_urlsafe(24)
        self.db.sess_add(tok, aid, time.time() + SESSION_TTL)
        return tok

    def guest(self) -> dict:
        aid = "a_" + secrets.token_hex(8)
        self.db.acc_create(aid, None, None, "guest", time.time())
        return {"ok": True, "account_id": aid, "token": self._new_session(aid), "status": "guest"}

    def register(self, email: str, password: str, account_id: str | None = None) -> dict:
        email = (email or "").strip().lower()
        if "@" not in email or len(password) < 4:
            return {"ok": False, "msg": "нужна почта и пароль от 4 символов"}
        if self.db.acc_by_email(email):
            return {"ok": False, "msg": "почта уже занята"}
        if account_id and self.db.acc_get(account_id):
            aid = account_id
            acc = self.db.acc_get(aid)
            created = acc.get("created") or time.time()
        else:
            aid = "a_" + secrets.token_hex(8)
            created = time.time()
        self.db.acc_create(aid, email, _hash_pw(password), "registered", created)
        return {"ok": True, "account_id": aid, "token": self._new_session(aid), "status": "registered"}

    def login(self, email: str, password: str) -> dict:
        email = (email or "").strip().lower()
        aid = self.db.acc_by_email(email)
        acc = self.db.acc_get(aid) if aid else None
        if not acc or not _verify_pw(password, acc.get("pass_hash") or ""):
            return {"ok": False, "msg": "неверная почта или пароль"}
        return {"ok": True, "account_id": aid, "token": self._new_session(aid), "status": acc.get("status")}

    def logout(self, token: str) -> None:
        self.db.sess_del(token)

    def resolve(self, token: str) -> str | None:
        s = self.db.sess_get(token or "")
        if not s:
            return None
        if s["expires"] < time.time():
            self.db.sess_del(token)
            return None
        return s["account_id"]

    def status(self, aid: str) -> str:
        acc = self.db.acc_get(aid)
        return acc.get("status", "guest") if acc else "none"
