"""
Сетевой сервер со входом — мультиплеер поверх mp.World.
    python3 netserver.py   →  http://127.0.0.1:8090

Транспорт пока HTTP (опрос) — как в проекте, шаг 1 перед WebSocket. Важно, что
архитектура уже мультиплеерная: один общий мир (mp.World) тикает в фоне, клиенты
опознаются по сессии-токену (cookie), каждое намерение применяется к своему
игроку. Вход: гость / регистрация / логин (world/accounts).
"""
from __future__ import annotations
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from http.cookies import SimpleCookie
import threading, time, json, os

from world.accounts import Accounts, AccountsDB
from world.storage import JsonStorage, SqlStorage
from world import ws
import mp
from world.version import VERSION as GAME_VERSION

PORT = int(os.environ.get("GAME_PORT", "8090"))
HOST = os.environ.get("GAME_HOST", "127.0.0.1")   # в Docker ставим 0.0.0.0
SPEED = float(os.environ.get("GAME_SPEED", "0.55"))   # тиков в секунду; меньше — мир медленнее (~44с/день)
# Гость — небезопасен в проде (аккаунт без пароля легко фармить/абьюзить),
# поэтому доступен ТОЛЬКО в тестовом окружении. Прод: GAME_ENV=prod → только вход.
GAME_ENV = os.environ.get("GAME_ENV", "dev")
ALLOW_GUEST = GAME_ENV != "prod"
SECURE_COOKIE = os.environ.get("GAME_SECURE", "0") == "1"   # за TLS включить (куки только по https)
# Бэкенд хранилища: GAME_STORAGE=sqlite → SQL (прод-цель Postgres), иначе JSON-файлы.
STORAGE = os.environ.get("GAME_STORAGE", "json")

if STORAGE == "postgres":
    import time as _time
    from world.db import GameDBPostgres
    _dsn = os.environ.get("DATABASE_URL", "postgresql://localhost/game")
    # БД может быть ещё не готова при старте контейнера — подождём с ретраями,
    # а на стойкую ошибку дадим понятную подсказку вместо голого трейсбэка.
    _db = None
    _last_err = None
    for _attempt in range(1, 31):
        try:
            _db = GameDBPostgres(_dsn)
            break
        except Exception as _e:
            _last_err = _e
            print(f"[БД] попытка {_attempt}/30: не удалось подключиться к Postgres "
                  f"({type(_e).__name__}). Жду 2с…", flush=True)
            _time.sleep(2)
    if _db is None:
        print("[БД] Не удалось подключиться к Postgres за 60с. Частые причины:\n"
              "  • сменился DB_PASSWORD, а том pgdata помнит старый пароль —\n"
              "    поправь пароль ИЛИ выполни:  docker compose down -v && docker compose up -d --build\n"
              "    (down -v стирает БД мира — для теста это нормально);\n"
              "  • контейнер db ещё не поднялся — посмотри: docker compose logs db\n"
              f"Последняя ошибка: {_last_err}", flush=True)
        raise SystemExit(1)
    _accounts = AccountsDB(_db)
    _storage = SqlStorage(_db)
elif STORAGE == "sqlite":
    from world.db import GameDB
    _db = GameDB(os.environ.get("GAME_DB", "/tmp/game.db"))
    _accounts = AccountsDB(_db)
    _storage = SqlStorage(_db)
else:
    _accounts = Accounts()
    _storage = JsonStorage()

_world = mp.World(seed=1, storage=_storage)
_running = True

# анти-брутфорс входа: не больше N попыток с одного адреса за окно
_RATE_MAX, _RATE_WINDOW = 10, 60.0
_rate_hits: dict = {}
_rate_lock = threading.Lock()


def _rate_ok(ip: str) -> bool:
    now = time.time()
    with _rate_lock:
        hits = [t for t in _rate_hits.get(ip, []) if now - t < _RATE_WINDOW]
        hits.append(now)
        _rate_hits[ip] = hits
        return len(hits) <= _RATE_MAX


def _loop():
    while _running:
        _world.tick()
        time.sleep(1.0 / SPEED)


# namespace намерений: имя → (метод мира, какие поля брать из тела)
def _state(aid):
    """Состояние игрока + статус аккаунта (для режима интерфейса гость/аккаунт)."""
    st = _world.state(aid)
    st["status"] = _accounts.status(aid)
    return st


def _dispatch(aid, path, body):
    w = _world
    if path == "/api/state":
        w.touch(aid); return _state(aid)
    if path == "/api/respawn":
        w.join(aid); return {"ok": True, "msg": "Новая жизнь началась."}
    if path == "/api/do":      return w.do(aid, body.get("activity"), body.get("quality", 0))
    if path == "/api/help":    return w.help(aid, body.get("quality", 0))
    if path == "/api/favor":   return w.favor(aid, body.get("npc", ""))
    if path == "/api/quest":   return w.quest(aid, body.get("qid", 0))
    if path == "/api/choose":  return w.choose(aid, body.get("opt", ""))
    if path == "/api/deliver": return w.deliver(aid, body.get("qid", 0))
    if path == "/api/house":   return w.house(aid, body.get("action"))
    if path == "/api/item":    return w.item(aid, body.get("action"), body.get("id", ""), body.get("slot", ""))
    if path == "/api/trade":   return w.trade(aid, body.get("action"), body.get("res", ""), body.get("amount", 10))
    if path == "/api/travel":  return w.travel(aid, body.get("to", -1))
    if path == "/api/hunt":    return w.hunt(aid)
    if path == "/api/trap":    return w.trap(aid)
    if path == "/api/strike":  return w.strike(aid, body.get("quality", 0))
    if path == "/api/drink":   return w.drink(aid)
    if path == "/api/say":     return w.say(aid, body.get("text", ""))
    if path == "/api/card":
        act = body.get("action", "start")
        if act == "hit":   return w.card_hit(aid)
        if act == "stand": return w.card_stand(aid)
        return w.card_start(aid)
    if path == "/api/durak":
        act = body.get("action", "start")
        if act == "start": return w.durak_start(aid)
        return w.durak_act(aid, act, body.get("idx", -1))
    if path == "/api/pvp":
        act = body.get("action", "join")
        if act == "join": return w.pvp_join(aid)
        return w.pvp_act(aid, act)
    if path == "/api/dpvp":
        act = body.get("action", "join")
        if act == "join": return w.durak_pvp_join(aid)
        return w.durak_pvp_act(aid, act, body.get("idx", -1))
    if path == "/api/fixtavern":return w.fix_tavern(aid, body.get("quality", 0))
    if path == "/api/dice":    return w.dice(aid, body.get("quality", 0))
    if path == "/api/priority":return w.priority(aid, body.get("mode", "balanced"))
    if path == "/api/sleep":   return w.sleep(aid)
    if path == "/api/craft":   return w.craft(aid, body.get("id", ""))
    if path == "/api/build":   return w.build(aid, body.get("id", ""))
    if path == "/api/family":  return w.start_family(aid)
    if path == "/api/save":    return w.save(aid)
    return None


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"          # нужно для апгрейда до WebSocket

    # ── утилиты ──
    def _json(self, obj, cookie=None):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        if cookie:
            self.send_header("Set-Cookie", cookie)
        self.end_headers()
        self.wfile.write(body)

    def _raw(self, body: bytes, ctype: str):
        self.send_response(200)
        self.send_header("Content-Type", ctype + "; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _html(self, text):
        body = text.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    _ASSET_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "assets")

    def _asset(self, name):
        # отдаём спрайты-заглушки; имя файла санитизируем (без путей вверх)
        safe = os.path.basename(name)
        path = os.path.join(self._ASSET_DIR, safe)
        if not safe.endswith(".svg") or not os.path.exists(path):
            self.send_error(404); return
        with open(path, "rb") as f:
            body = f.read()
        self.send_response(200)
        self.send_header("Content-Type", "image/svg+xml; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "max-age=3600")
        self.end_headers()
        self.wfile.write(body)

    def _token(self):
        c = SimpleCookie(self.headers.get("Cookie", ""))
        return c["sid"].value if "sid" in c else None

    def _set_cookie(self, token):
        sec = " Secure;" if SECURE_COOKIE else ""
        return f"sid={token}; Path=/; HttpOnly;{sec} Max-Age=2592000; SameSite=Lax"

    def _read_body(self):
        n = int(self.headers.get("Content-Length", 0))
        try:
            return json.loads(self.rfile.read(n) or b"{}")
        except Exception:
            return {}

    # ── маршруты ──
    def do_GET(self):
        if self.path == "/healthz":                 # для healthcheck контейнера
            self._json({"ok": True, "version": GAME_VERSION}); return
        if self.path.startswith("/assets/"):
            self._asset(self.path[len("/assets/"):]); return
        if self.path == "/manifest.webmanifest":
            self._raw(MANIFEST.encode(), "application/manifest+json"); return
        if self.path == "/sw.js":
            self._raw(SW_JS.encode(), "application/javascript"); return
        if self.path in ("/", "/index.html"):
            self._html(PAGE.replace("__VER__", GAME_VERSION)); return
        if self.path == "/observe":
            self._html(OBSERVE_PAGE); return
        if self.path == "/api/observe":
            self._json(_world.observe_snapshot()); return
        if self.path.split("?", 1)[0] == "/ws":
            self._serve_ws(); return
        if self.path == "/api/state":
            aid = _accounts.resolve(self._token())
            if not aid:
                self._json({"auth": False, "guest_ok": ALLOW_GUEST}); return
            _world.join(aid)                 # идемпотентно: гарантируем присутствие
            self._json(_dispatch(aid, "/api/state", {})); return
        self.send_error(404)

    # ── WebSocket: живой канал вместо опроса ──
    def _serve_ws(self):
        tok = self._token()
        if not tok and "?" in self.path:          # толстый клиент (Godot) шлёт токен в query: /ws?sid=…
            from urllib.parse import urlparse, parse_qs
            tok = (parse_qs(urlparse(self.path).query).get("sid") or [None])[0]
        aid = _accounts.resolve(tok)
        key = self.headers.get("Sec-WebSocket-Key")
        if not aid or not key:
            self.send_error(400); return
        self.send_response(101, "Switching Protocols")
        self.send_header("Upgrade", "websocket")
        self.send_header("Connection", "Upgrade")
        self.send_header("Sec-WebSocket-Accept", ws.accept_key(key))
        self.end_headers()
        sock = self.connection
        _world.join(aid)
        send_lock = threading.Lock()
        alive = [True]

        def push(obj):
            try:
                with send_lock:
                    ws.send(sock, json.dumps(obj, ensure_ascii=False))
            except OSError:
                alive[0] = False

        def reader():
            while alive[0]:
                try:
                    fr = ws.recv(sock)
                except OSError:
                    alive[0] = False; break
                if fr is None:
                    alive[0] = False; break
                op, data = fr
                if op == ws.OP_CLOSE:
                    alive[0] = False; break
                if op == ws.OP_PING:
                    with send_lock:
                        try: ws.send(sock, data, ws.OP_PONG)
                        except OSError: alive[0] = False
                    continue
                if op == ws.OP_TEXT:
                    try:
                        msg = json.loads(data.decode("utf-8"))
                    except Exception:
                        continue
                    _world.touch(aid)
                    res = _dispatch(aid, "/api/" + str(msg.get("op", "")), msg)
                    push({"type": "ack", "rid": msg.get("rid"), "res": res})
                    push({"type": "state", "state": _state(aid)})

        rt = threading.Thread(target=reader, daemon=True)
        rt.start()
        # первый снимок — полный; дальше шлём только ИЗМЕНИВШИЕСЯ части (дельты),
        # чтобы не гонять весь мир каждому раз в секунду (оптимизация под рост)
        last = _state(aid)
        push({"type": "state", "state": last})
        try:
            while alive[0]:
                time.sleep(1.0)
                _world.touch(aid)
                st = _state(aid)
                changed = {k: v for k, v in st.items() if last.get(k) != v}
                for k in last:
                    if k not in st:
                        changed[k] = None            # ключ исчез (напр. here↔transit)
                if changed:
                    push({"type": "delta", "changed": changed})
                    last = st
        except OSError:
            pass
        alive[0] = False

    def do_POST(self):
        # аутентификация
        if self.path.startswith("/auth/"):
            self._auth(); return
        # игровые намерения — по сессии
        aid = _accounts.resolve(self._token())
        if not aid:
            self._json({"auth": False}); return
        res = _dispatch(aid, self.path, self._read_body())
        if res is None:
            self.send_error(404); return
        self._json(res)

    def _auth(self):
        body = self._read_body()
        if self.path == "/auth/guest":
            if not ALLOW_GUEST:
                self._json({"ok": False, "msg": "гостевой вход отключён — войдите по аккаунту"}); return
            r = _accounts.guest()
        elif self.path == "/auth/register":
            cur = _accounts.resolve(self._token())     # апгрейд текущего гостя, если есть
            r = _accounts.register(body.get("email", ""), body.get("password", ""), account_id=cur)
        elif self.path == "/auth/login":
            ip = self.client_address[0] if self.client_address else "?"
            if not _rate_ok(ip):
                self._json({"ok": False, "msg": "слишком много попыток — подождите минуту"}); return
            r = _accounts.login(body.get("email", ""), body.get("password", ""))
        elif self.path == "/auth/logout":
            tok = self._token()
            aid = _accounts.resolve(tok)
            if aid:
                _world.leave(aid)
            _accounts.logout(tok)
            self._json({"ok": True}, cookie="sid=; Path=/; Max-Age=0"); return
        else:
            self.send_error(404); return
        if r.get("ok"):
            _world.join(r["account_id"])
            self._json(r, cookie=self._set_cookie(r["token"]))
        else:
            self._json(r)

    def log_message(self, *a): pass


MANIFEST = """{
  "name": "Приграничье", "short_name": "Приграничье",
  "start_url": "/", "display": "standalone",
  "background_color": "#1b1b21", "theme_color": "#1b1b21",
  "icons": [{"src": "/assets/icon.svg", "sizes": "any", "type": "image/svg+xml", "purpose": "any maskable"}]
}"""

# минимальный service worker — нужен лишь для «установки» PWA (игра живёт по сети)
SW_JS = "self.addEventListener('fetch', function(){});"

# ── окно-наблюдатель для тестировщика: живая таблица сущностей и домов ──
OBSERVE_PAGE = r"""<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Приграничье · наблюдатель</title>
<style>
 :root{--bg:#191920;--fg:#e8e3d8;--sub:#9a958a;--line:#36343c;--gold:#d8b968;--warn:#c98}
 *{box-sizing:border-box} body{margin:0;background:var(--bg);color:var(--fg);
  font:14px/1.4 ui-sans-serif,system-ui,Segoe UI,Roboto,sans-serif;padding:14px}
 h1{font-size:17px;margin:0 0 2px} .bar{display:flex;gap:14px;align-items:center;flex-wrap:wrap;margin-bottom:10px}
 .sub{color:var(--sub);font-size:12px} .gold{color:var(--gold)}
 input[type=search]{background:#26262e;border:1px solid var(--line);border-radius:8px;color:var(--fg);padding:7px 10px;font:inherit;min-width:180px}
 label.chk{font-size:12px;color:var(--sub);cursor:pointer}
 table{border-collapse:collapse;width:100%;font-size:13px;margin-bottom:18px}
 th,td{text-align:left;padding:4px 8px;border-bottom:1px solid var(--line);white-space:nowrap}
 th{color:var(--sub);font-weight:600;cursor:pointer;position:sticky;top:0;background:var(--bg)}
 tr:hover td{background:#22222a}
 .bad{color:var(--warn)} .me{color:var(--gold)}
 .tag{font-size:11px;padding:1px 6px;border:1px solid var(--line);border-radius:10px;color:var(--sub)}
 .meter{display:inline-block;min-width:34px}
 h2{font-size:14px;margin:4px 0 6px;color:var(--gold)}
 .dot{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:4px}
</style></head><body>
<h1>👁 Наблюдатель мира <span class="sub" id="clock"></span></h1>
<div class="bar">
 <input type="search" id="q" placeholder="фильтр по имени/роли/деревне…" oninput="render()">
 <label class="chk"><input type="checkbox" id="onlyplayers" onchange="render()"> только игроки</label>
 <label class="chk"><input type="checkbox" id="auto" checked> авто-обновление (2с)</label>
 <button onclick="pull()">обновить</button>
 <span class="sub" id="meta"></span>
</div>
<h2>🧑 Сущности (<span id="ecount">0</span>)</h2>
<div style="overflow-x:auto"><table id="ents"><thead><tr id="ehead"></tr></thead><tbody id="ebody"></tbody></table></div>
<h2>🏠 Деревни</h2>
<div style="overflow-x:auto"><table id="sets"><thead><tr id="shead"></tr></thead><tbody id="sbody"></tbody></table></div>
<script>
let DATA={entities:[],settlements:[]}, sortKey='name', sortDir=1;
const ECOLS=[['name','имя'],['role','роль'],['driver','упр.'],['trait','черта'],['where','деревня'],['sex','п'],['age','возр'],
 ['health','здоровье'],['morale','настрой'],['hunger','голод'],['fatigue','устал'],
 ['social','общ'],['ambition','амб'],['dwelling','дом'],
 ['home','жильё'],['comfort','уют'],['coins','монеты'],['debt','долг'],['rep','слава'],['weapon','оружие'],
 ['warmth','тепло'],['worn','надето'],['skills','навыки'],['last_act','занятие']];
const SCOLS=[['name','деревня'],['pop','нас.'],['online','онлайн'],['health','здоровье'],['food_days','еда,дн'],
 ['house_integrity','дом%'],['season','сезон'],['weather','погода'],['keeper','корчмарь'],['graves','могил']];
function esc(s){return String(s==null?'':s).replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function warnCell(k,v){
 if((k==='hunger'||k==='fatigue')&&v>=70)return'bad';
 if((k==='health'||k==='morale')&&typeof v==='number'&&v>=0&&v<35)return'bad';
 if(k==='food_days'&&v<1)return'bad';
 return'';}
function render(){
 const q=document.getElementById('q').value.toLowerCase().trim();
 const onlyP=document.getElementById('onlyplayers').checked;
 // головы
 document.getElementById('ehead').innerHTML=ECOLS.map(([k,l])=>`<th onclick="sortBy('${k}')">${l}</th>`).join('');
 document.getElementById('shead').innerHTML=SCOLS.map(([k,l])=>`<th>${l}</th>`).join('');
 let ents=DATA.entities.slice();
 if(onlyP)ents=ents.filter(e=>e.driver==='player'||e.driver==='afk');
 if(q)ents=ents.filter(e=>(e.name+' '+e.role+' '+e.where).toLowerCase().includes(q));
 ents.sort((a,b)=>{let x=a[sortKey],y=b[sortKey];if(typeof x==='number')return (x-y)*sortDir;return String(x).localeCompare(String(y))*sortDir;});
 document.getElementById('ecount').textContent=ents.length;
 document.getElementById('ebody').innerHTML=ents.map(e=>{
  const drv=e.driver==='player'?'<span class="dot" style="background:#6c9"></span>игрок':
   e.driver==='afk'?'<span class="dot" style="background:#cc6"></span>офлайн':
   '<span class="dot" style="background:#667"></span>AI';
  return '<tr>'+ECOLS.map(([k])=>{
   let v=e[k];
   if(k==='driver')return `<td>${drv}</td>`;
   const cls=warnCell(k,v);
   return `<td class="${cls}">${esc(v)}</td>`;}).join('')+'</tr>';}).join('');
 let sets=DATA.settlements.slice();
 if(q)sets=sets.filter(s=>s.name.toLowerCase().includes(q));
 document.getElementById('sbody').innerHTML=sets.map(s=>'<tr>'+SCOLS.map(([k])=>{
   let v=s[k];if(k==='name'&&s.archived)v+=' 💤';
   return `<td class="${warnCell(k,v)}">${esc(v)}</td>`;}).join('')+'</tr>').join('');
}
function sortBy(k){if(sortKey===k)sortDir*=-1;else{sortKey=k;sortDir=1;}render();}
async function pull(){
 try{const r=await fetch('/api/observe',{cache:'no-store'});DATA=await r.json();
  document.getElementById('clock').textContent=`· день ${DATA.day}, ${String(DATA.hour).padStart(2,'0')}:00`;
  document.getElementById('meta').textContent=`v${DATA.version||'?'} · шардов ${DATA.shards} · деревень ${DATA.settlements_n} · сущностей ${DATA.entities.length}`;
  render();}catch(e){document.getElementById('meta').textContent='нет связи…';}
}
setInterval(()=>{if(document.getElementById('auto').checked)pull();},2000);
pull();
</script></body></html>"""

PAGE = r"""<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<link rel="manifest" href="/manifest.webmanifest">
<meta name="theme-color" content="#1b1b21">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<link rel="apple-touch-icon" href="/assets/icon.svg">
<title>Приграничье — живой мир</title>
<style>
 :root{--bg:#1b1b21;--card:#26262e;--line:#34343f;--ink:#e8e6e3;--sub:#9b9a97;
       --good:#6ab04c;--warn:#f0932b;--bad:#eb4d4b;--gold:#e1b12c;--accent:#7b68ee}
 *{box-sizing:border-box} body{background:var(--bg);color:var(--ink);
   font:15px/1.5 system-ui,sans-serif;max-width:900px;margin:0 auto;padding:18px}
 h1{font-size:21px;margin:0} .sub{color:var(--sub);font-size:13px}
 .card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px 16px;margin:12px 0}
 .row{display:flex;gap:10px;flex-wrap:wrap;align-items:center}
 .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}
 button{background:#30303a;color:var(--ink);border:1px solid var(--line);border-radius:9px;padding:9px 11px;font:inherit;cursor:pointer}
 button:hover{border-color:var(--accent)} input{background:#1b1b21;color:var(--ink);border:1px solid var(--line);border-radius:8px;padding:9px;font:inherit}
 .act b{display:block} .act .sk{color:var(--sub);font-size:12px}
 .pill{background:#30303a;border:1px solid var(--line);border-radius:20px;padding:4px 11px;font-size:13px} .gold{color:var(--gold)}
 .tag{font-size:11px;padding:2px 7px;border-radius:10px;background:#333}
 .mut{color:var(--sub);font-size:12px} .cols{display:grid;grid-template-columns:1fr 1fr;gap:12px}
 .tabs{display:flex;gap:6px;flex-wrap:wrap;margin:12px 0 4px} .tabs button{padding:9px 13px}
 .tabs button.active{border-color:var(--gold);color:var(--gold)}
 .pane{display:none} .pane.active{display:block}
 .paperdoll{display:grid;grid-template-columns:repeat(3,1fr);gap:7px;margin:7px 0}
 .pdcell{background:#26262e;border:1px solid var(--line);border-radius:9px;padding:7px 8px;min-height:52px}
 .pdcell.pdon{border-color:var(--gold);background:#2c2a22}
 .pdslot{font-size:11px;color:var(--sub)} .pditem{font-size:13px;margin-top:2px}
 .pdx{font-size:11px;color:#c98} @media(max-width:560px){.paperdoll{grid-template-columns:repeat(2,1fr)}}
 .chatlog{margin-top:8px;max-height:190px;overflow-y:auto;display:flex;flex-direction:column;gap:3px}
 .chatmsg{font-size:13px;line-height:1.35} .chatmsg .who{color:var(--gold)} .chatmsg .tm{color:var(--sub);font-size:11px}
 .chatmsg.chatnpc{color:var(--sub);font-style:italic} .chatmsg.chatnpc .who{color:#b9a06a}
 input{background:#26262e;border:1px solid var(--line);border-radius:8px;color:var(--fg);padding:8px 10px;font:inherit}
 .cardrow{display:flex;gap:5px;flex-wrap:wrap;margin:3px 0 6px}
 .pcard{width:38px;height:52px;background:#f4efe3;color:#222;border-radius:6px;border:1px solid #0004;
  display:flex;align-items:center;justify-content:center;font-size:15px;font-weight:700;box-shadow:0 1px 3px #0005}
 .pcard.red{color:#b23} .pcard.back{background:#3a3450;color:#d8b968}
 .pcard{position:relative} .pcv{position:absolute;right:2px;bottom:1px;font-size:9px;color:#888;font-weight:400}
 .ctot{font-size:12px;color:var(--sub)}
 .guide{background:#222a3f;border:1px solid var(--line);border-radius:9px;padding:9px 11px;margin:8px 0;font-size:13px;color:#cfe3ff;display:none}
 body.simple .guide{display:block}
 .fillbar{position:absolute;left:0;top:0;bottom:0;width:0;background:#6ab04c;transition:width .05s}
 .scene{position:relative;height:240px;border-radius:12px;overflow:hidden;border:1px solid var(--line);margin:12px 0;transition:background 1.4s}
 .orb{position:absolute;top:10px;font-size:24px;transition:left 1.4s,top 1.4s;z-index:2}
 .wx{position:absolute;top:8px;right:12px;font-size:20px;opacity:.9;z-index:2}
 .world3d{position:absolute;left:50%;top:44px;transform:translateX(-50%)}
 #tokens{z-index:4} .spr{position:absolute;image-rendering:auto}
 .sprfallback{position:absolute;display:flex;align-items:center;justify-content:center;pointer-events:none}
 .spr.clk{cursor:pointer} .spr.clk:hover{filter:brightness(1.25) drop-shadow(0 0 6px var(--gold))}
 .verbadge{position:fixed;right:8px;bottom:6px;z-index:9999;font-size:11px;color:var(--sub);opacity:.6;pointer-events:none}
 .token{position:absolute;transition:left 1.6s ease,top 1.6s ease}
 .win{position:absolute;width:6px;height:6px;border-radius:2px;background:#ffd36b;
      box-shadow:0 0 6px #ffcf5a;opacity:0;transition:opacity 1.4s;z-index:3}
 .night{position:absolute;inset:0;background:radial-gradient(120% 90% at 50% 15%,rgba(5,8,20,0) 30%,rgba(5,8,20,.65));
        opacity:0;transition:opacity 1.4s;pointer-events:none;z-index:3}
 .nameplate{position:absolute;font-size:9px;color:#eee;text-shadow:0 1px 2px #000;white-space:nowrap;transform:translateX(-50%);z-index:5}
 .travel{position:absolute;inset:0;display:none;z-index:6}
 .travel.on{display:block}
 .vdot{position:absolute;font-size:12px;color:#e8e6e3;text-align:center;transform:translate(-50%,-50%)}
 .caravan{position:absolute;font-size:24px;transform:translate(-50%,-50%);transition:left 1s linear,top 1s linear}
 .road{position:absolute;height:2px;background:repeating-linear-gradient(90deg,#5a5a6a 0 8px,transparent 8px 14px);transform-origin:left center}
 @media(max-width:640px){.cols{grid-template-columns:1fr}}
 .over{position:fixed;inset:0;background:rgba(0,0,0,.82);display:none;align-items:center;justify-content:center;z-index:10}
 .over.on{display:flex} .stage{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:22px;width:min(420px,92vw)}
 #field{position:relative;height:240px;background:#1b1b21;border:1px solid var(--line);border-radius:10px;margin:12px 0;overflow:hidden;cursor:crosshair}
 .target{position:absolute;width:44px;height:44px;border-radius:50%;background:radial-gradient(circle at 35% 35%,#ffd36b,#e1932c);cursor:pointer}
 .seqbtn{font-size:22px;width:60px;height:60px;margin:4px}
 .bar{position:absolute;top:0;bottom:0;width:4px;background:#fff} .zone{position:absolute;top:0;bottom:0;background:rgba(106,176,76,.3)}
 #tbar{position:relative;height:46px;background:#1b1b21;border:1px solid var(--line);border-radius:8px;margin:12px 0;overflow:hidden}
 ul{margin:4px 0 0;padding-left:18px} li{color:#cfcdc9;margin:2px 0;font-size:13px}
 .toast{position:fixed;left:50%;bottom:22px;transform:translateX(-50%);background:#30303a;border:1px solid var(--accent);border-radius:10px;padding:10px 16px;opacity:0;transition:.3s;pointer-events:none;max-width:90vw;text-align:center} .toast.on{opacity:1}
</style></head><body>

<!-- экран входа -->
<div class="over on" id="gate"><div class="stage">
 <h1>Приграничье</h1>
 <p class="sub">Живой мир из нескольких деревень. Войди — и займёшь в нём место.</p>
 <div id="gatebody">
  <button id="guestbtn" style="width:100%;margin:6px 0;display:none" onclick="guest()">Играть гостем (тест)</button>
  <div class="mut" style="margin:10px 0 4px">войти / зарегистрироваться:</div>
  <input id="em" placeholder="почта" style="width:100%;margin:4px 0">
  <input id="pw" type="password" placeholder="пароль" style="width:100%;margin:4px 0">
  <div class="row"><button onclick="login()">Войти</button><button onclick="register()">Регистрация</button></div>
  <div class="sub" id="autherr" style="color:var(--bad);margin-top:6px"></div>
 </div>
</div></div>

<!-- экран смерти -->
<div class="over" id="deadover"><div class="stage" style="text-align:center">
 <h1>Ты пал</h1><p class="sub">Деревня помнит тебя — твоё имя в хронике. Мир идёт дальше.</p>
 <button onclick="newLife()">Начать новую жизнь</button>
</div></div>

<div id="verbadge" class="verbadge"></div>
<div id="game" style="display:none">
<div class="row" style="justify-content:space-between">
 <div><h1 id="town">…</h1><div class="sub" id="when">…</div></div>
 <div class="row">
  <span class="pill" id="who">—</span>
  <span class="pill gold" id="coins">⌾ 0</span>
  <span class="pill" id="home">🏚</span>
  <span class="pill" id="gear">—</span>
  <button onclick="save()">💾</button>
  <button onclick="logout()">выйти</button>
 </div>
</div>

<div class="card"><b>🗺 Деревни региона</b> <span class="sub">— путешествуй караваном, торгуй на разнице цен</span>
 <div class="row" id="map" style="margin-top:10px"></div>
 <div class="sub" id="transit" style="margin-top:8px"></div>
</div>

 <div class="scene" id="scene"><div class="orb" id="orb"></div><div class="wx" id="wx"></div>
  <div class="world3d" id="world3d"></div><div class="world3d" id="tokens"></div>
  <div class="night" id="night"></div><div class="travel" id="travel"></div></div>
<div id="local">
 <div class="card"><div class="row" style="justify-content:space-between">
  <div><b id="health">здоровье —</b> <span class="sub" id="store"></span></div>
  <div class="sub" id="body"></div></div>
  <div class="sub" id="whoelse" style="margin-top:6px"></div>
 </div>

 <div class="tabs" id="tabs">
  <button data-pane="village" class="active" onclick="tab('village')">🏡 Деревня</button>
  <button data-pane="tavern" onclick="tab('tavern')">🍯 Таверна</button>
  <button data-pane="goods" onclick="tab('goods')">🎒 Хозяйство</button>
  <button data-pane="log" onclick="tab('log')">📜 Летопись</button>
 </div>

 <div class="pane active" data-pane="village">
  <div class="guide">Выбери занятие — откроется мини-игра. На ПК основное действие — <b>Пробел</b>. Ниже — просьбы жителей (квесты).</div>
  <div class="card"><b>Чем займёшься?</b><div class="grid" id="acts" style="margin-top:10px"></div>
   <div class="row" style="margin-top:10px"><button onclick="hunt()">🏹 Выйти на зверя (бой)</button>
    <button onclick="trap()">🪤 Поставить силок</button>
    <button onclick="sleep_()">🛏 В кровать (ночью)</button>
    <span class="sub">зверя — только с оружием; мелочь — силком</span></div></div>
  <div class="card"><b>📜 Просьбы деревни</b> <span class="sub">— живые нужды; выполнишь — будет должен</span>
   <div id="quests" style="margin-top:10px"></div><div id="choice" style="margin-top:10px"></div><div id="deliveries" style="margin-top:10px"></div></div>
 </div>

 <div class="pane" data-pane="tavern">
  <div class="guide">В корчме отдыхают и пьют медовуху с друзьями. Починил корчму — корчмарь нальёт за счёт заведения.</div>
  <div class="card"><b>🍯 Таверна</b> <span id="tav" class="sub"></span>
   <div class="row" style="margin-top:8px"><button id="drinkbtn" onclick="drink()">🍺 Выпить медовухи</button>
    <button onclick="fixTavern()">🔨 Подлатать корчму</button>
    <button onclick="dice()">🎲 Кости (ставка 5⌾)</button>
    <button onclick="cardStart()">🃏 Очко (ставка 10⌾)</button>
    <button onclick="durakStart()">🂿 Дурак (ставка 10⌾)</button>
    <button onclick="pvpJoin()">🎴 PvP Очко (с игроком)</button>
    <button onclick="dpvpJoin()">🂿 PvP Дурак (с игроком)</button>
    <span class="sub" id="withme"></span></div>
   <div class="sub" id="keeper" style="margin-top:6px"></div></div>
  <div class="card" id="cardgame" style="display:none"><b>🃏 Очко</b> <span class="sub">набери ближе к 21, но не больше</span>
   <div id="cardtable" style="margin-top:8px"></div>
   <div class="row" id="cardbtns" style="margin-top:8px"></div></div>
  <div class="card" id="durakgame" style="display:none"><b>🂿 Дурак</b> <span class="sub">подкидной, против корчмаря</span>
   <div id="duraktop" class="sub" style="margin-top:6px"></div>
   <div id="duraktable" style="margin-top:6px;min-height:58px"></div>
   <div class="sub" style="margin-top:4px">твои карты (клик — сыграть):</div>
   <div id="durakhand" class="cardrow"></div>
   <div id="durakmsg" style="margin-top:4px"></div>
   <div class="row" id="durakbtns" style="margin-top:6px"></div></div>
  <div class="card" id="pvpgame" style="display:none"><b>🎴 PvP Очко</b> <span class="sub">кто ближе к 21, тот берёт банк</span>
   <div id="pvptop" class="sub" style="margin-top:6px"></div>
   <div id="pvpopp" style="margin-top:4px"></div>
   <div class="sub" style="margin-top:4px">твои карты:</div>
   <div id="pvphand" class="cardrow"></div>
   <div id="pvpmsg" style="margin-top:4px"></div>
   <div class="row" id="pvpbtns" style="margin-top:6px"></div></div>
  <div class="card" id="dpvpgame" style="display:none"><b>🂿 PvP Дурак</b> <span class="sub">подкидной с другим игроком</span>
   <div id="dpvptop" class="sub" style="margin-top:6px"></div>
   <div id="dpvptable" style="margin-top:6px;min-height:58px"></div>
   <div class="sub" style="margin-top:4px">твои карты (клик — сыграть):</div>
   <div id="dpvphand" class="cardrow"></div>
   <div id="dpvpmsg" style="margin-top:4px"></div>
   <div class="row" id="dpvpbtns" style="margin-top:6px"></div></div>
  <div class="card"><b>💬 Разговор у очага</b> <span class="sub">слышат те, кто сейчас здесь</span>
   <div id="chatlog" class="chatlog"></div>
   <div class="row" style="margin-top:8px">
    <input id="chatin" maxlength="160" placeholder="сказать слово…" style="flex:1;min-width:140px"
     onkeydown="if(event.key==='Enter')sayChat()">
    <button onclick="sayChat()">Сказать</button></div></div>
 </div>

 <div class="pane" data-pane="goods">
  <div class="guide">Торгуй на разнице цен между деревнями, крафти из добычи, обустраивай двор и жильё.</div>
  <div class="cols">
   <div class="card"><b>🧺 Караван-торг</b> <span class="sub">рынок ×<span id="mk">1</span></span>
    <div id="trade" style="margin-top:8px"></div><div class="sub" id="cargo" style="margin-top:8px"></div></div>
   <div class="card"><b>🛒 Лавка</b> <span class="sub">товар от мастеров и караванов</span>
    <div id="caravan" style="margin-top:4px"></div><div id="shop" style="margin-top:8px"></div></div>
  </div>
  <div class="cols">
   <div class="card"><b>🎒 При себе / 🏠 Склад</b><div id="inv" style="margin-top:8px"></div></div>
   <div class="card"><b>Жильё и услуги</b><div class="row" id="house" style="margin-top:8px"></div>
    <div class="row" style="margin-top:8px"><button onclick="startHelp()">🤝 Помочь соседу</button><span class="sub" id="favors"></span></div></div>
  </div>
  <div class="cols" id="ownercards">
   <div class="card"><b>🛠 Мастерская</b> <span class="sub">крафт из добычи (груз)</span>
    <div id="craft" style="margin-top:8px"></div></div>
   <div class="card"><b>🏡 Двор</b> <span class="sub" id="plotsub">постройки на участке</span>
    <div id="plot" style="margin-top:8px"></div></div>
  </div>
  <div class="card" id="ownerhint" style="display:none"><span class="sub">🔒 Мастерская и развитие двора откроются, когда купишь свой дом (не аренда).</span></div>
  <div class="card"><b>🧭 Персонаж</b> <span class="sub" id="origin"></span>
   <div class="row" style="margin-top:8px"><span class="sub">семья:</span>
    <span class="sub" id="heir"></span><button id="familybtn" onclick="family()">👶 Завести наследника</button></div>
   <div class="row" style="margin-top:8px"><span class="sub">в отлучке:</span>
    <button onclick="prio('work')">больше работать</button>
    <button onclick="prio('balanced')">баланс</button>
    <button onclick="prio('rest')">больше отдыхать</button>
    <button onclick="prio('full_rest')">только отдых</button>
    <span class="sub" id="prio"></span></div>
   <div id="journal" style="margin-top:8px"></div></div>
 </div>

 <div class="pane" data-pane="log">
  <div class="guide">Твоя лента жизни, слухи дня и долгая память деревни.</div>
  <div class="card"><b>🧭 Лента жизни</b> <span class="sub">что ты делал(а)</span><ul id="feed"></ul></div>
  <div class="cols">
   <div class="card"><b>Что говорят</b><ul id="news"></ul></div>
   <div class="card"><b>Память деревни</b> <span class="sub" id="graves"></span><ul id="chron"></ul></div>
  </div>
 </div>
</div>
</div>

<div class="over" id="mini"><div class="stage" style="text-align:center">
 <b id="mtitle"></b><div class="sub" id="mhint"></div><div id="mbody"></div>
 <div class="row" style="justify-content:center"><button onclick="closeMini()">Закрыть</button></div>
</div></div>
<div class="toast" id="toast"></div>

<script>
let S=null,pending=null;
async function api(p,b){const r=await fetch(p,{method:b?'POST':'GET',
 headers:{'Content-Type':'application/json'},body:b?JSON.stringify(b):undefined});return r.json();}
function toast(m){const t=document.getElementById('toast');t.textContent=m;
 t.classList.add('on');clearTimeout(window._tt);window._tt=setTimeout(()=>t.classList.remove('on'),2400);}

// ── WebSocket: живой канал (пуш состояния + намерения с rid) ──
let ws=null, _rid=0, _pend={};
function connectWS(){
 ws=new WebSocket((location.protocol==='https:'?'wss':'ws')+'://'+location.host+'/ws');
 ws.onmessage=e=>{const m=JSON.parse(e.data);
   if(m.type==='state')applyState(m.state);
   else if(m.type==='delta'){for(const k in m.changed){if(m.changed[k]===null)delete S[k];else S[k]=m.changed[k];}applyState(S);}
   else if(m.type==='ack'){const r=_pend[m.rid];if(r){delete _pend[m.rid];r(m.res);}}};
 ws.onclose=()=>{ws=null;if(document.getElementById('game').style.display!=='none')setTimeout(connectWS,1500);};
}
function wsCall(op,extra){return new Promise(res=>{
 if(!ws||ws.readyState!==1){res({ok:false,msg:'нет связи'});return;}
 const id=++_rid;_pend[id]=res;ws.send(JSON.stringify(Object.assign({op,rid:id},extra||{})));
 setTimeout(()=>{if(_pend[id]){delete _pend[id];res({ok:false,msg:'таймаут'});}},5000);});}
const ROLE_EMOJI={woodcutter:'🪓',smith:'🔨',cook:'🍲',fisher:'🎣',idler:'🧑',
 tavernkeeper:'🍺',villager:'🧑',newcomer:'🧳',heir:'👶',traveler:'🧳',
 hunter:'🏹',herbalist:'🌿'};
const ROLE_SPRITE={woodcutter:1,smith:1,cook:1,fisher:1,hunter:1,herbalist:1,tavernkeeper:1};
function roleSprite(role){return ROLE_SPRITE[role]?('/assets/'+role+'.svg'):'/assets/villager.svg';}
function skyGradient(hour,season,weather){
 let g;
 if(hour<5||hour>=22)g=['#0b1020','#141a2e'];           // ночь
 else if(hour<8)g=['#f6a96b','#3a3a66'];                 // рассвет
 else if(hour<18)g=['#7ec0ee','#cfeafe'];                // день
 else if(hour<21)g=['#ff9e6b','#4a3a66'];                // закат
 else g=['#1a2240','#0b1020'];                           // сумерки
 if(weather==='буря')g=['#4a4e63','#2b2f40'];
 else if(weather==='ливень'||weather==='засуха')g=['#8a94a6','#5a6478'];
 if(season==='зима')g=[g[0],'#b9c6dd'];
 return `linear-gradient(${g[0]},${g[1]})`;
}
// ── изометрический вид (2.5D сверху): статика + живые токены ──
const TW=64, TH=32, COLS=5, ROWS=4;
function isoX(c,r){return (c-r)*TW/2;}
function isoY(c,r){return (c+r)*TH/2;}
function addSpr(parent,src,x,y,w,h,z,cls,emoji){
 const im=document.createElement('img');im.className='spr'+(cls?' '+cls:'');im.src=src;
 im.style.left=x+'px';im.style.top=y+'px';im.style.width=w+'px';if(h)im.style.height=h+'px';
 im.style.zIndex=String(z);
 // защита от «битых» спрайтов: если картинка не загрузилась — рисуем эмодзи-заглушку на том же месте
 im.dataset.emoji=emoji||'';
 im.onerror=function(){
   if(im._fallen)return;im._fallen=true;im.style.display='none';
   const em=im.dataset.emoji;if(!em)return;
   const s=document.createElement('div');s.className='spr sprfallback'+(cls?' '+cls:'');
   s.textContent=em;s.style.left=x+'px';s.style.top=y+'px';
   s.style.width=w+'px';s.style.height=(h||w)+'px';s.style.fontSize=Math.round((h||w)*0.7)+'px';
   s.style.zIndex=String(z);s.style.lineHeight=(h||w)+'px';s.style.textAlign='center';
   parent.appendChild(s);im._fb=s;
 };
 parent.appendChild(im);return im;
}
const HOUSE_SPOTS=[[0,0],[2,0],[4,0],[0,2],[3,3]];
const TAVERN_SPOT=[2,2];
const WALK=[[1,1],[3,1],[1,3],[2,3],[4,2],[0,3],[4,3],[2,1],[1,2],[3,2]];
function hashi(s,m){let h=0;for(let i=0;i<s.length;i++)h=(h*31+s.charCodeAt(i))>>>0;return h%m;}
let _staticSig="", _tokens={}, _winGlows=[];

function buildStatic(H,winter){
 const w3=document.getElementById('world3d');w3.innerHTML='';_winGlows=[];
 const tile=winter?'/assets/tile_snow.svg':'/assets/tile_grass.svg';
 const tileEm=winter?'⬜':'🟩';
 for(let r=0;r<ROWS;r++)for(let c=0;c<COLS;c++)
   addSpr(w3,tile,isoX(c,r)-TW/2,isoY(c,r),TW,TH,(c+r),'',tileEm);
 const nb=Math.min(HOUSE_SPOTS.length,Math.max(2,Math.round((H.alive||8)/4)));
 const mark=(c,r)=>{const g=document.createElement('div');g.className='win';
   g.style.left=(isoX(c,r)-4)+'px';g.style.top=(isoY(c,r)-14)+'px';g.style.zIndex=60;
   w3.appendChild(g);_winGlows.push(g);};
 for(let i=0;i<nb;i++){const [c,r]=HOUSE_SPOTS[i];
   const hi=addSpr(w3,'/assets/house.svg',isoX(c,r)-32,isoY(c,r)-40,64,72,50+(c+r),'clk','🏠');
   hi.title='двор и хозяйство';hi.onclick=()=>tab('goods');mark(c,r);}
 if(H.tavern){const [c,r]=TAVERN_SPOT;
   const ti=addSpr(w3,'/assets/tavern.svg',isoX(c,r)-32,isoY(c,r)-40,64,72,54,'clk','🍯');
   ti.title='в таверну';ti.onclick=()=>tab('tavern');mark(c,r);}
 // декор по краям: деревья/ёлки/камни/колодец (зимой — заснеженные). Не на тропах.
 const treeSp=winter?'/assets/pine_snow.svg':'/assets/tree.svg';
 const treeEm=winter?'🌲':'🌳';
 const PROPS=[[-1,0,treeSp,44,56,treeEm],[5,1,treeSp,44,56,treeEm],[-1,3,'/assets/rock.svg',40,34,'🪨'],
   [5,4,winter?'/assets/pine_snow.svg':'/assets/pine.svg',40,58,'🌲'],[0,4,'/assets/well.svg',48,54,'⛲'],
   [-1,2,winter?'/assets/tree_snow.svg':'/assets/bush_berry.svg',36,30,'🌿']];
 PROPS.forEach(([c,r,sp,pw,ph,em])=>addSpr(w3,sp,isoX(c,r)-pw/2,isoY(c,r)-ph+TH,pw,ph,40+(c+r),'',em));
}

function targetTile(name,hour,inTavern,isMe){
 const sleep=(hour>=23||hour<7), evening=(hour>=18&&hour<23);
 if(sleep) return HOUSE_SPOTS[hashi(name,HOUSE_SPOTS.length)];   // ночь → к дому/кровати
 if(evening && (inTavern||isMe)) return TAVERN_SPOT;             // вечер → в таверну
 return WALK[hashi(name+'w',WALK.length)];                       // день → по делам
}

function drawScene(H,hour){
 document.getElementById('travel').classList.remove('on');
 endTravelView();
 const sc=document.getElementById('scene');
 sc.style.background=skyGradient(hour,H.season,H.weather);
 const isNight=(hour>=22||hour<6);
 const orb=document.getElementById('orb');orb.textContent=isNight?'🌙':'☀';
 const frac=Math.max(0,Math.min(1,(hour-5)/16));
 orb.style.left=(6+frac*84)+'%';orb.style.top=(28-Math.sin(frac*Math.PI)*20)+'px';
 const wic={'буря':'⛈','ливень':'🌧','засуха':'🌫','вёдро':'🌤','ясно':''}[H.weather]||'';
 document.getElementById('wx').textContent=(H.cold?'❄':'')+wic;
 // плавное затемнение ночи + окна
 let dark=0; if(hour>=21)dark=(hour-21)/3+0.45; else if(hour<7)dark=(7-hour)/4+0.3;
 dark=Math.max(0,Math.min(0.82,dark));
 document.getElementById('night').style.opacity=dark.toFixed(2);
 const winter=(!!H.cold||H.season==='зима');
 const sig=`${H.name}|${winter}|${Math.round((H.alive||8)/4)}|${H.tavern?1:0}`;
 if(sig!==_staticSig){_staticSig=sig;buildStatic(H,winter);}
 _winGlows.forEach(g=>g.style.opacity=(dark>0.2?0.9:0).toString());
 // живые токены — устойчивы по имени, плавно едут к цели (CSS-переход)
 const tk=document.getElementById('tokens');
 const tav=new Set(H.tavern||[]);
 const want={};
 (H.roster||[]).slice(0,7).forEach(r=>want[r.name]={sp:roleSprite(r.role),e:ROLE_EMOJI[r.role]||'🧑'});
 (H.players_here||[]).forEach(n=>want[n]={sp:'/assets/villager.svg',e:'🧍'});
 want[S.me.name]={sp:'/assets/player.svg',e:'⭐',me:true};
 for(const name in _tokens){if(!(name in want)){_tokens[name].img.remove();_tokens[name].np.remove();delete _tokens[name];}}
 const sleeping=(hour>=23||hour<7);
 for(const name in want){
   const p=want[name];let t=_tokens[name];
   if(!t){const img=addSpr(tk,p.sp,0,0,28,40,100,'token');
     const np=document.createElement('div');np.className='nameplate';np.style.transition='left 1.6s,top 1.6s';tk.appendChild(np);
     t=_tokens[name]={img,np};}
   t.img.src=p.sp;
   const [c,r]=targetTile(name,hour,tav.has(name),!!p.me);
   const jx=(hashi(name,7)-3), jy=(hashi(name+'y',5)-2);
   const x=isoX(c,r)+jx, y=isoY(c,r)+jy;
   t.img.style.left=(x-14)+'px';t.img.style.top=(y-30)+'px';t.img.style.zIndex=String(100+c+r);
   t.np.style.left=x+'px';t.np.style.top=(y+2)+'px';
   t.np.textContent=(sleeping?'💤 ':p.e+' ')+name;
 }
}

// ── анимированное путешествие (самоанимация через rAF, деревня скрыта) ──
let _travelSig='', _travelRAF=0, _travelProps=[], _travelCar=null, _travelBar=null, _travelLbl=null;
let _travelProg=0, _travelCur=0, _travelT=0;
function hideVillage(hide){
 ['world3d','tokens','night','orb','wx'].forEach(id=>{const e=document.getElementById(id);if(e)e.style.display=hide?'none':'';});
}
function drawTravel(S){
 const sc=document.getElementById('scene');const tr=document.getElementById('travel');
 hideVillage(true);                                  // прячем деревню на время пути
 sc.style.background='linear-gradient(#20406a 0%,#3a5a86 55%,#8a6a4a 56%,#6a4e32 100%)'; // небо + земля
 tr.classList.add('on');
 const sig=`${S.transit.to}`;
 if(sig!==_travelSig){
  _travelSig=sig;tr.innerHTML='';_travelProps=[];
  const W=tr.clientWidth||860, H=tr.clientHeight||300, horizon=Math.round(H*0.56);
  // солнце
  const sun=document.createElement('div');sun.textContent='☀';sun.style.cssText=`position:absolute;left:78%;top:18px;font-size:30px`;tr.appendChild(sun);
  // дорога
  const road=document.createElement('div');road.style.cssText=`position:absolute;left:0;right:0;top:${horizon}px;height:${H-horizon}px;background:repeating-linear-gradient(90deg,#5a4026 0 40px,#64482c 40px 80px)`;tr.appendChild(road);
  // придорожные деревья/камни — их прокручиваем влево (ощущение движения)
  const EM=['🌲','🌳','🪨','🌲','🌾','🌳','🏔','🌲'];
  for(let i=0;i<10;i++){const p=document.createElement('div');p.textContent=EM[i%EM.length];
    p.style.cssText=`position:absolute;font-size:${22+ (i%3)*8}px;top:${horizon-24-(i%3)*6}px;left:${i*120}px`;
    tr.appendChild(p);_travelProps.push({el:p,base:i*120});}
  // караван (покачивается на месте по центру)
  _travelCar=document.createElement('div');_travelCar.textContent='🐫';
  _travelCar.style.cssText=`position:absolute;left:44%;top:${horizon-30}px;font-size:40px`;tr.appendChild(_travelCar);
  // метка и полоса прогресса
  _travelLbl=document.createElement('div');_travelLbl.style.cssText='position:absolute;left:0;right:0;top:14px;text-align:center;font-weight:600;color:#ffe6a0';tr.appendChild(_travelLbl);
  const barbg=document.createElement('div');barbg.style.cssText=`position:absolute;left:10%;right:10%;bottom:16px;height:10px;background:#0006;border-radius:6px;overflow:hidden`;
  _travelBar=document.createElement('div');_travelBar.style.cssText='height:100%;width:0;background:linear-gradient(90deg,#d8b968,#f0d890)';barbg.appendChild(_travelBar);tr.appendChild(barbg);
  _travelCur=0;
  if(!_travelRAF)_travelRAF=requestAnimationFrame(travelTick);
 }
 _travelProg=Math.max(0.03,Math.min(0.99,1-(S.transit.left||0)/9));
 _travelLbl.innerHTML=`🐫 В пути в <b>${S.transit.to}</b> — осталось ~${S.transit.left} ч`;
}
function travelTick(ts){
 _travelRAF=requestAnimationFrame(travelTick);
 const dt=16; _travelT+=dt;
 const tr=document.getElementById('travel');const W=(tr&&tr.clientWidth)||860;
 // прокрутка пейзажа
 const speed=0.06;
 for(const p of _travelProps){let x=(p.base - (_travelT*speed))% (W+140);if(x<-120)x+=W+140;p.el.style.left=x+'px';}
 // покачивание каравана
 if(_travelCar)_travelCar.style.transform=`translateY(${Math.sin(_travelT*0.006)*4}px)`;
 // плавное подтягивание полосы прогресса к цели
 _travelCur+=(_travelProg-_travelCur)*0.04;
 if(_travelBar)_travelBar.style.width=(_travelCur*100).toFixed(1)+'%';
}
function endTravelView(){_travelSig='';if(_travelRAF){cancelAnimationFrame(_travelRAF);_travelRAF=0;}_travelProps=[];_travelCar=null;_travelBar=null;_travelLbl=null;hideVillage(false);}
let _inCombat=false;
function applyState(s){S=s;if(S.dead)document.getElementById('deadover').classList.add('on');
 document.body.classList.toggle('simple', !!(s.status && s.status!=='guest')); // аккаунт — проще, с гайдами
 if(S.encounter&&!_inCombat)openCombat(S.encounter);
 render();}
function tab(name){document.querySelectorAll('.pane').forEach(p=>p.classList.toggle('active',p.dataset.pane===name));
 document.querySelectorAll('.tabs button').forEach(b=>b.classList.toggle('active',b.dataset.pane===name));}
// пробел = основное действие активной мини-игры (ПК)
window._spaceAction=null;
window.addEventListener('keydown',e=>{if(e.code==='Space'&&window._spaceAction){e.preventDefault();if(!e.repeat)window._spaceAction(true);}});
window.addEventListener('keyup',e=>{if(e.code==='Space'&&window._spaceAction){e.preventDefault();window._spaceAction(false);}});

// ── вход (по HTTP, нужен cookie для рукопожатия WS) ──
async function guest(){afterAuth(await api('/auth/guest',{}));}
async function login(){afterAuth(await api('/auth/login',{email:em.value,password:pw.value}));}
async function register(){afterAuth(await api('/auth/register',{email:em.value,password:pw.value}));}
async function logout(){if(ws)ws.close();await api('/auth/logout',{});location.reload();}
function afterAuth(r){if(!r.ok){document.getElementById('autherr').textContent=r.msg||'не вышло';return;}
 document.getElementById('gate').classList.remove('on');document.getElementById('game').style.display='';connectWS();}
async function newLife(){await wsCall('respawn');document.getElementById('deadover').classList.remove('on');}

async function boot(){const s=await api('/api/state');
 if(s.auth===false){document.getElementById('gate').classList.add('on');document.getElementById('game').style.display='none';
   document.getElementById('guestbtn').style.display=s.guest_ok?'block':'none';return;}
 document.getElementById('gate').classList.remove('on');document.getElementById('game').style.display='';connectWS();}

function render(){
 document.getElementById('who').textContent=`${S.me.full_name||S.me.name} ${S.me.sex==='ж'?'♀':'♂'} ${S.me.age}л · ★${S.me.reputation} (ты)`+(S.me.trait_label?` · ${S.me.trait_label}`:'');
 if(S.me.trait_desc)document.getElementById('who').title=S.me.trait_desc;
 document.getElementById('coins').textContent='⌾ '+S.me.coins;
 const hm={none:'🏚 без крова',rented:'🏠 комната',owned:'🏡 свой дом'}[S.me.home];
 document.getElementById('home').textContent=hm+(S.me.home!=='none'?` ·уют ${S.me.comfort}`:'');
 const gw=(S.me.weapon?S.me.weapon.label:'без оружия')+' · '+(S.me.outfit?S.me.outfit.label:'без одежды');
 document.getElementById('gear').textContent='⚔ '+gw;
 const rg=S.region;const lod=rg.hot!==undefined?` · живут детально ${rg.hot}/${rg.count} (тел в RAM ${rg.bodies}), шардов ${rg.shards}`:'';
 document.getElementById('when').textContent=`Регион «${rg.name}», ${rg.count} деревни · День ${S.day}, ${String(S.hour).padStart(2,'0')}:00`+lod;
 const map=document.getElementById('map');map.innerHTML='';
 for(const v of S.settlements){
   const d=document.createElement('div');d.className='pill';
   const mk=v.market>1.1?'🔺дорого':v.market<0.9?'🔻дёшево':'≈';
   if(v.know===0){d.style.opacity='.45';d.innerHTML='🌫 <span class="mut">неизвестный край</span>';}
   else if(v.current){d.style.borderColor='var(--gold)';d.innerHTML=`📍<b>${v.name}</b> <span class="mut">·жит ${v.pop} ·${mk}</span>`;}
   else if(S.transit){d.innerHTML=`${v.name} <span class="mut">${mk}</span>`;}
   else if(v.know===1){d.style.opacity='.75';
     d.innerHTML=`🌁 ${v.name} <span class="mut">·по слухам ·${v.hours}ч</span> <button onclick="travel(${v.idx})">→ разведать</button>`;}
   else{const hc=v.health>60?'var(--good)':v.health>25?'var(--warn)':'var(--bad)';
     d.innerHTML=`<span style="color:${hc}">●</span> ${v.name} <span class="mut">·жит ${v.pop} ·${mk} ·${v.hours}ч</span> <button onclick="travel(${v.idx})">→ в путь</button>`;}
   map.appendChild(d);}
 const loc=document.getElementById('local');
 if(S.transit){document.getElementById('transit').textContent=`🐫 В пути в ${S.transit.to}: осталось ~${S.transit.left} ч.`;
   loc.style.display='none';document.getElementById('town').textContent='🐫 В пути…';drawTravel(S);return;}
 document.getElementById('transit').textContent='';loc.style.display='';
 const H=S.here;document.getElementById('town').textContent=H.name;
 const hc=H.health>60?'var(--good)':H.health>25?'var(--warn)':'var(--bad)';
 const sic={'весна':'🌱','лето':'☀','осень':'🍂','зима':'❄'}[H.season]||'';
 const wic={'буря':'⛈','ливень':'🌧','засуха':'🌵','вёдро':'☀','ясно':''}[H.weather]||'';
 const wtag=(H.weather&&H.weather!=='ясно')?` <span class="tag">${wic} ${H.weather}</span>`:'';
 const coldb=(H.cold?' <span class="tag" style="background:#2d4a63">❄ холода</span>':'')+` <span class="tag">${sic} ${H.season}</span>`+wtag;
 document.getElementById('health').innerHTML=`<span style="color:${hc}">здоровье ${H.health}</span>`+coldb;
 document.getElementById('store').textContent='· еды на '+H.food_days+' дн · склад: '+Object.entries(H.store).map(([k,v])=>k+' '+v).join(' · ');
 document.getElementById('body').innerHTML=`❤ здоровье ${S.me.health} · 🙂 настрой ${S.me.morale} · 🍗 голод ${S.me.hunger} · 💤 усталость ${S.me.fatigue}`;
 document.getElementById('whoelse').textContent=H.players_here&&H.players_here.length?('👥 рядом другие игроки: '+H.players_here.join(', ')):'';
 document.getElementById('mk').textContent=H.market;
 drawScene(H,S.hour);
 const ac=document.getElementById('acts');ac.innerHTML='';
 for(const [k,a] of Object.entries(S.activities)){const sk=S.me.skills[a.skill]?(' · '+a.skill+' '+S.me.skills[a.skill]):(' · '+a.skill);
   const b=document.createElement('button');b.className='act';b.innerHTML=`<b>${a.label}</b><span class="sk">${a.minigame}${sk}</span>`;
   b.onclick=()=>startAct(k);ac.appendChild(b);}
 const qc=document.getElementById('quests');
 if(!S.quests.length)qc.innerHTML='<span class="sub">пока деревне всего хватает</span>';
 else{qc.innerHTML='';for(const q of S.quests){const lab=S.activities[q.activity]?S.activities[q.activity].label:q.activity;
   const d=document.createElement('div');d.className='row';d.style.margin='6px 0';
   if(q.accepted)d.innerHTML=`<span class="pill">📜 ${q.giver}: ${q.title}</span><span class="sub">${lab} — ${q.have}/${q.need} · ${q.reward}⌾ +услуга</span>`;
   else{const tail=q.chain?' <span class="tag" style="background:#3a3350">🔗 цепочка</span>':'';const verb=q.kind==='deliver'?'Взять поклажу':'Взяться';
     d.innerHTML=`<span>📜 <b>${q.giver}</b>: ${q.title} <span class="sub">(${q.kind==='deliver'?'доставка':lab}, ${q.reward}⌾)</span>${tail}</span><button onclick="accept(${q.qid})">${verb}</button>`;}
   qc.appendChild(d);}}
 const chc=document.getElementById('choice');
 if(S.choice){chc.innerHTML=`<div class="pill" style="border-color:var(--gold)">🔀 ${S.choice.giver} доверяет тебе нажитое — как поступишь?</div>`+
   `<div class="row" style="margin-top:6px"><button onclick="choose('deliver')">🐫 Отвезти в ${S.choice.dest_name} (монеты)</button>`+
   `<button onclick="choose('charity')">🤲 Раздать нуждающимся (слава)</button></div>`;}
 else chc.innerHTML='';
 const dv=document.getElementById('deliveries');
 if(!S.me.deliveries.length)dv.innerHTML='';
 else{dv.innerHTML='<div class="mut">🐫 несу поклажу:</div>';for(const d of S.me.deliveries){const row=document.createElement('div');row.className='row';row.style.margin='4px 0';
   row.innerHTML=`<span class="pill">📦 ${d.parcel} → ${d.dest_name} <span class="mut">(от ${d.giver}, ${d.reward}⌾)</span></span>`+(d.can?`<button onclick="deliver(${d.qid})">сдать здесь</button>`:`<span class="sub">довези до ${d.dest_name}</span>`);dv.appendChild(row);}}
 const tr=document.getElementById('trade');tr.innerHTML='';
 for(const [res,p] of Object.entries(S.trade)){const row=document.createElement('div');row.className='row';row.style.margin='4px 0';
   row.innerHTML=`<span style="width:70px">${res}</span><span class="mut">куп ${p.buy} / прод ${p.sell} · есть ${p.store}</span><button onclick="trade('buy','${res}')">купить 10</button><button onclick="trade('sell','${res}')">продать 10</button>`;tr.appendChild(row);}
 const cg=Object.entries(S.me.cargo);document.getElementById('cargo').innerHTML=cg.length?'🧺 в грузе: '+cg.map(([k,v])=>`${k} ${v}`).join(' · '):'<span class="sub">груз пуст</span>';
 const sh=document.getElementById('shop');sh.innerHTML='';
 if(!S.shop||!S.shop.length){sh.innerHTML='<span class="sub">лавка пуста — ждите мастеров или караван</span>';}
 for(const it of (S.shop||[])){const b=document.createElement('button');b.innerHTML=`${it.icon} ${it.name} <span class="mut">${it.price}⌾ ·×${it.count}</span>`;b.onclick=()=>item('buy',it.id);sh.appendChild(b);}
 // караван в деревне
 const cv=(H.caravans||[]);const cvd=document.getElementById('caravan');
 if(cvd)cvd.innerHTML=cv.length?('🐫 '+cv.map(c=>`<b>${c.name}</b> <span class="sub">(${c.goods})</span>`).join(' · ')):'';
 const inv=document.getElementById('inv');
 const SLOTS=[['weapon','⚔','в руках'],['head','🎩','голова'],['body','👕','тело'],['outfit','🧥','верх'],['feet','🥾','ноги'],['trinket','🧿','оберег']];
 const worn=S.me.worn||{};
 inv.innerHTML=`<div class="mut">надето · тепло ${S.me.warmth}:</div>`;
 const pd=document.createElement('div');pd.className='paperdoll';
 SLOTS.forEach(([slot,ic,lbl])=>{const w=worn[slot];const cell=document.createElement('div');cell.className='pdcell'+(w?' pdon':'');
   cell.innerHTML=`<div class="pdslot">${ic} ${lbl}</div>`+(w?`<div class="pditem">${w.label}</div><a href="#" class="pdx" onclick="item('unequip','','${slot}');return false">снять ✕</a>`:'<div class="sub">—</div>');
   pd.appendChild(cell);});
 inv.appendChild(pd);
 inv.innerHTML+='<div class="mut" style="margin-top:6px">🎒 при себе:</div>';
 const bag=document.createElement('div');bag.className='row';if(!S.me.inventory.length)bag.innerHTML='<span class="sub">пусто</span>';
 S.me.inventory.forEach(it=>{const d=document.createElement('span');d.className='pill';const slot=it.slot||'';
   const eq=(slot==='weapon'||slot==='clothing'||slot==='trinket')?`<a href="#" onclick="item('equip','${it.id}');return false">надеть</a> · `:'';
   const us=(slot==='consumable')?`<a href="#" onclick="item('use','${it.id}');return false">выпить</a> · `:'';
   d.innerHTML=`${it.label} <span class="mut">${eq}${us}<a href="#" onclick="item('store','${it.id}');return false">склад</a> · <a href="#" onclick="item('sell','${it.id}');return false">продать</a></span>`;bag.appendChild(d);});
 inv.appendChild(bag);inv.innerHTML+='<div class="mut" style="margin-top:6px">🏠 склад:</div>';
 const st=document.createElement('div');st.className='row';if(!S.me.storage.length)st.innerHTML='<span class="sub">пусто</span>';
 S.me.storage.forEach(it=>{const b=document.createElement('button');b.textContent=it.label+' ↩';b.onclick=()=>item('unstore',it.id);st.appendChild(b);});inv.appendChild(st);
 const hs=document.getElementById('house');hs.innerHTML='';
 if(S.me.home!=='owned'){hs.innerHTML+=`<button onclick="house('rent')">Снять (${S.house.rent}/д)</button><button onclick="house('buy')">Купить дом (${S.house.price})</button>`;}
 if(S.me.home==='owned')hs.innerHTML+=`<button onclick="house('furnish')">Обустроить (${S.house.furnish})</button>`;
 // мастерская и двор — только у владельца дома (аренда/без дома не в счёт)
 const owned=(S.me.home==='owned');
 document.getElementById('ownercards').style.display=owned?'':'none';
 document.getElementById('ownerhint').style.display=owned?'none':'';
 const fv=Object.entries(S.me.favors).filter(([n,c])=>c>0);
 document.getElementById('favors').innerHTML=fv.length?'Должны: '+fv.map(([n,c])=>`<button onclick="callFavor('${n}')">${n}×${c}↩</button>`).join(' '):'<span class="sub">никто не должен</span>';
 document.getElementById('tav').textContent=H.tavern.length?'за медовухой: '+H.tavern.join(', '):'тиха';
 document.getElementById('withme').textContent=H.players_here&&H.players_here.length?('с тобой тут: '+H.players_here.join(', ')):'';
 document.getElementById('drinkbtn').textContent=H.free_tab?'🍺 Выпить (за счёт заведения)':`🍺 Выпить медовухи (${H.drink_price}⌾)`;
 renderChat(H.chat||[]);
 // PvP-столы: держим панели актуальными (вход соперника, его ходы)
 if(H.pvp&&H.pvp.seated){renderPvp(H.pvp);}
 if(H.dpvp&&H.dpvp.seated){renderDpvp(H.dpvp);}
 document.getElementById('keeper').textContent=H.keeper?('корчмарь: '+H.keeper+(H.free_tab?' — должен тебе (стол закрыт)':'')):'';
 const pr={work:'больше работать',balanced:'баланс',rest:'больше отдыхать',full_rest:'только отдых'}[S.me.priority]||S.me.priority;
 document.getElementById('origin').textContent=(S.me.origin||'');
 document.getElementById('prio').textContent='сейчас: '+pr;
 document.getElementById('heir').textContent=S.me.heir?('наследник: '+S.me.heir+' ✓  '):'рода пока нет  ';
 document.getElementById('familybtn').style.display=S.me.heir?'none':'';
 // крафт
 const cr=document.getElementById('craft');cr.innerHTML='';
 for(const r of (S.recipes||[])){const b=document.createElement('button');b.disabled=!r.can;
   const cost=Object.entries(r.cost).map(([k,v])=>k+' '+v).join(', ');
   b.innerHTML=`${r.icon} ${r.name} <span class="mut">(${cost})</span>`;b.onclick=()=>craft(r.id);cr.appendChild(b);}
 // двор
 const pl=document.getElementById('plot');pl.innerHTML='';
 const built=(S.me.plot||[]).map(p=>p.id);
 if(S.me.home!=='owned')document.getElementById('plotsub').textContent='нужен свой дом';
 else document.getElementById('plotsub').textContent='постройки на участке';
 if(S.me.plot&&S.me.plot.length)pl.innerHTML+='<div class="mut">стоит: '+S.me.plot.map(p=>p.icon+p.name).join(' · ')+'</div>';
 for(const p of (S.plots||[])){if(built.includes(p.id))continue;const b=document.createElement('button');
   b.disabled=S.me.home!=='owned';const cost=Object.entries(p.cost).map(([k,v])=>k+' '+v).join(', ');
   b.innerHTML=`${p.icon} ${p.name} <span class="mut">(${cost})</span>`;b.onclick=()=>build(p.id);pl.appendChild(b);}
 const jn=document.getElementById('journal');
 jn.innerHTML=(S.me.journal&&S.me.journal.length)?('<div class="mut">📖 пока тебя не было:</div><ul>'+S.me.journal.map(e=>`<li>${e}</li>`).join('')+'</ul>'):'';
 const fd=document.getElementById('feed');
 if(fd)fd.innerHTML=(S.me.feed&&S.me.feed.length)?S.me.feed.map(e=>`<li><span class="mut">д.${e.day} ${String(e.hour).padStart(2,'0')}:00</span> ${escHtml(e.text)}</li>`).join(''):'<li class="sub">пока тихо — начни действовать</li>';
 document.getElementById('news').innerHTML=H.news.map(n=>`<li>${n}</li>`).join('')||'<li>тихо…</li>';
 document.getElementById('graves').textContent='· могил '+H.graves;
 document.getElementById('chron').innerHTML=H.chronicle.map(n=>`<li>${n}</li>`).join('')||'<li>—</li>';
}

let DIFF=0.3;
const ACT_GAMES={firewood:['mash','timing'],garden:['sequence','catch'],berries:['ripe','catch'],
 fishing:['hold','balance'],hunting:['targets','aim'],build:['sequence','timing'],
 herbs:['ripe','catch'],mine:['timing','balance']};
function startAct(k){const a=Object.assign({},S.activities[k]);const pool=ACT_GAMES[k];
 if(pool)a.minigame=pool[Math.floor(Math.random()*pool.length)];
 pending={kind:'do',activity:k};openMini(a);}
function startHelp(){pending={kind:'help'};openMini({label:'Помощь со стройкой',minigame:'sequence',difficulty:0.3});}
function openMini(a){document.getElementById('mini').classList.add('on');DIFF=a.difficulty||0.3;
 document.getElementById('mtitle').textContent=a.label;const mg=a.minigame;
 const lvl=DIFF<0.3?'просто':DIFF<0.55?'средне':DIFF<0.75?'сложно':'мастерски';
 const hints={timing:'Останови бегунок в зелёной зоне (Пробел). 3 попытки.',sequence:'Повтори последовательность.',
  targets:'Лови цели.',ripe:'Кликай спелые 🍎, не 🟢.',mash:'Жми ПРОБЕЛ часто — наполни полосу!',
  hold:'Держи ПРОБЕЛ и отпусти в зелёной зоне.',catch:'Лови 🍎, не хватай 🪨 — кликай падающее.',
  aim:'Бей по цели, когда она в кольце (Пробел/клик).',balance:'Держи точку в центре: Пробел толкает, отпустишь — сносит.'};
 document.getElementById('mhint').textContent=(hints[mg]||'')+` · сложность: ${lvl}`;
 const body=document.getElementById('mbody');body.innerHTML='';window._spaceAction=null;
 if(mg==='timing')miniTiming(body);else if(mg==='sequence')miniSequence(body);
 else if(mg==='ripe')miniRipe(body);else if(mg==='mash')miniMash(body);
 else if(mg==='hold')miniHold(body);else if(mg==='catch')miniCatch(body);
 else if(mg==='aim')miniAim(body);else if(mg==='balance')miniBalance(body);
 else miniTargets(body);}
function closeMini(){document.getElementById('mini').classList.remove('on');window._spaceAction=null;if(window._mt)cancelAnimationFrame(window._mt);}

// ── охота/бой ──
async function hunt(){const r=await wsCall('hunt');if(!r.ok){toast(r.msg);return;}}
async function trap(){toast((await wsCall('trap')).msg);}
async function sleep_(){toast((await wsCall('sleep')).msg);}
function openCombat(enc){
 _inCombat=true;
 document.getElementById('mini').classList.add('on');
 document.getElementById('mtitle').textContent=(enc.where==='road'?'Засада! ':'Бой: ')+enc.icon+' '+enc.name;
 const tough=Math.min(0.7,0.12+enc.power*0.04);
 document.getElementById('mhint').textContent='Бей по цели — набери нужные удары. Можно и мышью, и пробелом по появившейся цели.';
 const body=document.getElementById('mbody');body.innerHTML='<div id="field"></div><div class="sub" id="cnt">ударов 0</div>';
 const f=body.querySelector('#field'),cnt=body.querySelector('#cnt');let hits=0,sp=0,done=false;
 const goal=5+Math.round(tough*5),life=Math.max(750,1300-tough*500),gap=Math.max(320,560-tough*220);
 f.style.fontSize='40px';
 function finishCombat(){if(done)return;done=true;f.querySelectorAll('.target').forEach(t=>t.remove());
   window._spaceAction=null;document.getElementById('mini').classList.remove('on');_inCombat=false;
   const q=Math.min(1,hits/goal);wsCall('strike',{quality:q}).then(r=>toast(r.msg||''));}
 function doHit(t){if(done)return;hits++;cnt.textContent='ударов '+hits+' / '+goal;if(t)t.remove();
   if(hits>=goal)finishCombat();}
 function spawn(){if(done)return;sp++;const t=document.createElement('div');t.className='target';
  t.textContent=enc.icon;t.style.display='flex';t.style.alignItems='center';t.style.justifyContent='center';
  t.style.width='60px';t.style.height='60px';
  t.style.left=(Math.random()*76)+'%';t.style.top=(Math.random()*68)+'%';
  t.onclick=()=>doHit(t);
  f.appendChild(t);setTimeout(()=>{if(t.parentNode)t.remove();},life);if(sp<goal+10)setTimeout(spawn,gap+Math.random()*160);}
 // пробел бьёт по самой «свежей» цели на поле
 window._spaceAction=(down)=>{if(down){const t=f.querySelector('.target');if(t)doHit(t);}};
 spawn();
 setTimeout(finishCombat,9000);
}
async function finish(q){closeMini();let r;
 if(pending.kind==='do')r=await wsCall('do',{activity:pending.activity,quality:q});
 else if(pending.kind==='fixtavern')r=await wsCall('fixtavern',{quality:q});
 else if(pending.kind==='dice')r=await wsCall('dice',{quality:q});
 else r=await wsCall('help',{quality:q});
 toast((r.msg||'')+(r.pay?` (+${r.pay}⌾)`:'')+(r.skill_level?` · ${r.skill} ${r.skill_level}`:''));
 if(r.quest_done)setTimeout(()=>toast(`✅ Квест для ${r.quest_done.giver}: +${r.quest_done.reward}⌾, теперь должен услугу`),2500);
 if(r.chain_next)setTimeout(()=>toast(`🔗 ${r.chain_next.giver}: ${r.chain_next.title} (${r.chain_next.reward}⌾) — на доске`),5000);}
function miniTiming(body){body.innerHTML='<div id="tbar"><div class="zone"></div><div class="bar"></div></div><button id="hit">Бей! (3)</button>';
 const bar=body.querySelector('.bar'),zone=body.querySelector('.zone');const zw=Math.max(8,30-DIFF*24),zx=10+Math.random()*(90-zw);
 zone.style.left=zx+'%';zone.style.width=zw+'%';const spd=1.2+DIFF*2.0;let pos=0,dir=1,left=3,sc=[];
 function step(){pos+=dir*spd;if(pos>100){pos=100;dir=-1}if(pos<0){pos=0;dir=1}bar.style.left=pos+'%';window._mt=requestAnimationFrame(step);}step();
 const hit=body.querySelector('#hit');
 hit.onclick=e=>{const c=zx+zw/2,dist=Math.abs(pos-c),half=zw/2;sc.push(Math.max(0,1-dist/(half+18)));left--;hit.textContent=`Бей! (${left})`;
  if(left<=0){cancelAnimationFrame(window._mt);window._spaceAction=null;finish(sc.reduce((a,b)=>a+b,0)/sc.length);}};
 window._spaceAction=(down)=>{if(down)hit.click();};}
function miniMash(body){body.innerHTML='<div id="tbar"><div class="fillbar"></div></div><button id="mash">рубить! (Пробел)</button>';
 const fb=body.querySelector('.fillbar');let taps=0;const goal=14+Math.round(DIFF*20);
 function tap(){taps++;fb.style.width=Math.min(100,taps/goal*100)+'%';}
 body.querySelector('#mash').onclick=tap;window._spaceAction=(down)=>{if(down)tap();};
 setTimeout(()=>{window._spaceAction=null;finish(Math.min(1,taps/goal));},5000);}
function miniHold(body){body.innerHTML='<div id="tbar"><div class="zone"></div><div class="fillbar"></div></div><div class="sub">держи Пробел/мышь, отпусти в зелёной</div>';
 const fb=body.querySelector('.fillbar'),zone=body.querySelector('.zone');
 const zlo=50+Math.random()*25,zw=Math.max(8,18-DIFF*10);zone.style.left=zlo+'%';zone.style.width=zw+'%';
 let v=0,held=false,done=false;const spd=1.1+DIFF*1.2;
 function grow(){if(held&&!done){v=Math.min(100,v+spd);fb.style.width=v+'%';}if(!done)window._mt=requestAnimationFrame(grow);}grow();
 function release(){if(done)return;done=true;cancelAnimationFrame(window._mt);window._spaceAction=null;
  const c=zlo+zw/2;const q=Math.max(0,1-Math.abs(v-c)/35);finish(q);}
 window._spaceAction=(down)=>{if(down)held=true;else release();};
 body.onpointerdown=()=>{held=true;};body.onpointerup=()=>release();}
function miniSequence(body){const icons=['🌲','🍎','🐟','🔨','🌾','💧'];let seq=[],inp=[];const n=3+Math.round(DIFF*5);
 for(let i=0;i<n;i++)seq.push(Math.floor(Math.random()*icons.length));
 body.innerHTML='<div id="show" style="font-size:30px;letter-spacing:6px;min-height:40px"></div><div id="pad"></div>';
 const sh=body.querySelector('#show'),pad=body.querySelector('#pad');let i=0;
 const t=setInterval(()=>{sh.textContent=icons[seq[i]];i++;if(i>=seq.length){clearInterval(t);
  setTimeout(()=>{sh.textContent='повтори →';icons.forEach((ic,idx)=>{const b=document.createElement('button');b.className='seqbtn';b.textContent=ic;
   b.onclick=()=>{inp.push(idx);if(inp[inp.length-1]!==seq[inp.length-1]){finish((inp.length-1)/seq.length);return;}if(inp.length===seq.length)finish(1);};pad.appendChild(b);});},600);}},650);}
function miniRipe(body){body.innerHTML='<div id="field"></div><div class="sub" id="cnt">спелых 0</div>';
 const f=body.querySelector('#field'),cnt=body.querySelector('#cnt');let good=0,bad=0,sp=0,done=false;
 const goal=6+Math.round(DIFF*7),life=Math.max(500,1000-DIFF*520);f.style.fontSize='26px';
 function spawn(){if(done)return;sp++;const ripe=Math.random()>0.38;const t=document.createElement('div');
  t.className='target';t.style.background='none';t.textContent=ripe?'🍎':'🟢';
  t.style.display='flex';t.style.alignItems='center';t.style.justifyContent='center';
  t.style.left=(Math.random()*82)+'%';t.style.top=(Math.random()*74)+'%';
  t.onclick=()=>{if(ripe){good++;}else{bad++;}cnt.textContent='спелых '+good+(bad?(' · мимо '+bad):'');t.remove();};
  f.appendChild(t);setTimeout(()=>t.remove(),life);if(sp<goal+8)setTimeout(spawn,340+Math.random()*220);}
 spawn();
 setTimeout(()=>{done=true;f.querySelectorAll('.target').forEach(t=>t.remove());
   finish(Math.max(0,Math.min(1,(good-bad*0.7)/goal)));},6000);}
function miniTargets(body){body.innerHTML='<div id="field"></div><div class="sub" id="cnt">поймано 0</div>';
 const f=body.querySelector('#field'),cnt=body.querySelector('#cnt');let c=0,sp=0,done=false;
 const tot=8+Math.round(DIFF*8),life=Math.max(450,950-DIFF*520),gap=Math.max(240,460-DIFF*240);
 function spawn(){if(done)return;sp++;const t=document.createElement('div');t.className='target';t.style.left=(Math.random()*82)+'%';t.style.top=(Math.random()*76)+'%';
  t.onclick=()=>{c++;cnt.textContent='поймано '+c;t.remove();};f.appendChild(t);setTimeout(()=>t.remove(),life);if(sp<tot)setTimeout(spawn,gap+Math.random()*200);}spawn();
 setTimeout(()=>{done=true;f.querySelectorAll('.target').forEach(t=>t.remove());finish(Math.min(1,c/tot));},6000);}
// ловля: падают 🍎(хорошо) и 🪨(плохо) — клик по падающему
function miniCatch(body){body.innerHTML='<div id="field" style="position:relative;height:200px"></div><div class="sub" id="cnt">собрано 0</div>';
 const f=body.querySelector('#field'),cnt=body.querySelector('#cnt');let good=0,bad=0,done=false,sp=0;
 const goal=6+Math.round(DIFF*6),fall=Math.max(1200,2200-DIFF*1000);
 function drop(){if(done)return;sp++;const ok=Math.random()>0.32;const t=document.createElement('div');
  t.textContent=ok?'🍎':'🪨';t.style.cssText=`position:absolute;font-size:26px;left:${5+Math.random()*85}%;top:-28px;transition:top ${fall}ms linear;cursor:pointer`;
  f.appendChild(t);requestAnimationFrame(()=>t.style.top='180px');
  t.onclick=()=>{if(ok)good++;else bad++;cnt.textContent='собрано '+good+(bad?(' · мимо '+bad):'');t.remove();};
  setTimeout(()=>t.remove(),fall+60);if(sp<goal+8)setTimeout(drop,Math.max(360,760-DIFF*300));}
 drop();setTimeout(()=>{done=true;finish(Math.max(0,Math.min(1,(good-bad*0.8)/goal)));},6500);}
// прицел: цель бегает, бей когда она в кольце (клик/Пробел)
function miniAim(body){body.innerHTML='<div id="field" style="position:relative;height:200px"><div id="ring" style="position:absolute;width:60px;height:60px;border:3px solid var(--gold);border-radius:50%;left:50%;top:70px;transform:translateX(-50%)"></div></div><div class="sub" id="cnt">попаданий 0</div>';
 const f=body.querySelector('#field'),ring=f.querySelector('#ring'),cnt=body.querySelector('#cnt');
 const rx=f.clientWidth/2||180, ry=100;let hits=0,done=false,t=0;
 const tgt=document.createElement('div');tgt.textContent='🎯';tgt.style.cssText='position:absolute;font-size:30px;cursor:pointer';f.appendChild(tgt);
 const goal=5+Math.round(DIFF*5),spd=1.6+DIFF*1.8;
 function loop(){if(done)return;t+=spd;const x=rx+Math.sin(t*0.05)*120, y=ry+Math.cos(t*0.07)*60;
  tgt.style.left=(x-15)+'px';tgt.style.top=(y-15)+'px';window._mt=requestAnimationFrame(loop);}loop();
 function tryHit(){if(done)return;const tr=tgt.getBoundingClientRect(),rr=ring.getBoundingClientRect();
  const dx=(tr.left+tr.width/2)-(rr.left+rr.width/2),dy=(tr.top+tr.height/2)-(rr.top+rr.height/2);
  if(Math.hypot(dx,dy)<34){hits++;cnt.textContent='попаданий '+hits+' / '+goal;if(hits>=goal){end();}}}
 tgt.onclick=tryHit;f.onclick=tryHit;window._spaceAction=(d)=>{if(d)tryHit();};
 function end(){if(done)return;done=true;cancelAnimationFrame(window._mt);window._spaceAction=null;finish(Math.min(1,hits/goal));}
 setTimeout(end,7000);}
// баланс: точку сносит влево, Пробел/мышь толкает вправо — держи у центра
function miniBalance(body){body.innerHTML='<div id="bar2" style="position:relative;height:34px;background:#26262e;border-radius:8px;margin-top:10px"><div id="zone2" style="position:absolute;left:42%;width:16%;top:0;bottom:0;background:#3a5a3a;border-radius:8px"></div><div id="dot2" style="position:absolute;width:16px;height:34px;background:var(--gold);border-radius:6px;left:50%"></div></div><div class="sub" id="cnt">держи в зелёной</div>';
 const dot=body.querySelector('#dot2');let pos=50,vel=0,push=false,done=false,inzone=0,ticks=0;
 function loop(){if(done)return;ticks++;vel+=(-0.18-DIFF*0.12);if(push)vel+=0.5;vel*=0.9;pos+=vel;
  if(pos<2){pos=2;vel=0;}if(pos>94){pos=94;vel=0;}dot.style.left=pos+'%';
  if(pos>=42&&pos<=58)inzone++;window._mt=requestAnimationFrame(loop);}loop();
 window._spaceAction=(d)=>{push=d;};body.onpointerdown=()=>push=true;body.onpointerup=()=>push=false;
 setTimeout(()=>{done=true;cancelAnimationFrame(window._mt);window._spaceAction=null;finish(Math.max(0,Math.min(1,inzone/Math.max(1,ticks))));},6500);}

async function accept(q){toast((await wsCall('quest',{qid:q})).msg);}
async function choose(opt){const r=await wsCall('choose',{opt});toast(r.msg);if(r.chain_next)setTimeout(()=>toast(`🔗 ${r.chain_next.title} (${r.chain_next.reward}⌾) — на доске`),2200);}
async function callFavor(n){toast((await wsCall('favor',{npc:n})).msg);}
async function house(a){toast((await wsCall('house',{action:a})).msg);}
async function item(a,id,slot){toast((await wsCall('item',{action:a,id,slot})).msg);}
async function trade(a,res){toast((await wsCall('trade',{action:a,res,amount:10})).msg);}
async function travel(to){toast((await wsCall('travel',{to})).msg);}
async function deliver(qid){toast((await wsCall('deliver',{qid})).msg);}
async function save(){toast((await wsCall('save',{})).msg);}
async function drink(){toast((await wsCall('drink',{})).msg);}
function escHtml(s){return (s||'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));}
let _chatSig='';
function renderChat(msgs){
 const box=document.getElementById('chatlog');if(!box)return;
 const sig=msgs.map(m=>m.who+m.text+m.day+m.hour).join('|');
 if(sig===_chatSig)return;_chatSig=sig;
 if(!msgs.length){box.innerHTML='<span class="sub">тихо — скажи первым</span>';return;}
 box.innerHTML=msgs.map(m=>{const np=m.npc?' chatnpc':'';
   return `<div class="chatmsg${np}"><span class="tm">${String(m.hour).padStart(2,'0')}:00</span> <span class="who">${escHtml(m.who)}</span> ${m.npc?'':':'} ${escHtml(m.text)}</div>`;}).join('');
 box.scrollTop=box.scrollHeight;
}
async function sayChat(){
 const el=document.getElementById('chatin');const t=el.value.trim();if(!t)return;
 el.value='';const r=await wsCall('say',{text:t});if(!r.ok)toast(r.msg);
}
function cardEl(c){const r=/[♥♦]/.test(c.c)?' red':'';const v=(c.v!=null)?`<span class="pcv">${c.v}</span>`:'';return `<div class="pcard${r}">${c.c}${v}</div>`;}
function renderCard(g){
 const box=document.getElementById('cardgame');const tbl=document.getElementById('cardtable');
 const btns=document.getElementById('cardbtns');box.style.display='block';
 let dealer=g.dealer.map(cardEl).join('');
 for(let i=0;i<(g.hidden||0);i++)dealer+='<div class="pcard back">?</div>';
 tbl.innerHTML=`<div class="ctot">Банкир${g.dtotal!=null?' · '+g.dtotal:''}:</div><div class="cardrow">${dealer}</div>`+
  `<div class="ctot">Ты · ${g.ptotal}:</div><div class="cardrow">${g.player.map(cardEl).join('')}</div>`+
  `<div style="margin-top:4px">${g.msg||''}</div>`;
 btns.innerHTML = g.done
  ? `<button onclick="cardStart()">🔄 Ещё партию (10⌾)</button><button onclick="cardClose()">Встать из-за стола</button>`
  : `<button onclick="cardHit()">🃏 Ещё карту</button><button onclick="cardStand()">✋ Хватит</button>`;
}
function cardClose(){document.getElementById('cardgame').style.display='none';}
async function cardStart(){const r=await wsCall('card',{action:'start'});if(!r.ok){toast(r.msg);return;}renderCard(r);}
async function cardHit(){const r=await wsCall('card',{action:'hit'});if(!r.ok){toast(r.msg);return;}renderCard(r);}
async function cardStand(){const r=await wsCall('card',{action:'stand'});if(!r.ok){toast(r.msg);return;}renderCard(r);}
function dcard(c,onclick){const r=/[♥♦]/.test(c.s||c.label)?' red':'';const h=onclick?` onclick="${onclick}"`:'';const st=onclick?'cursor:pointer':'';
 return `<div class="pcard${r}" style="${st}"${h}>${c.label}</div>`;}
function renderDurak(g){
 const box=document.getElementById('durakgame');box.style.display='block';
 document.getElementById('duraktop').textContent=`козырь ${g.trump} ${g.trump_card?('('+g.trump_card+')'):''} · в колоде ${g.deck} · у корчмаря ${g.bot_cards} карт · ты ${g.role==='attack'?'заходишь':'отбиваешься'}`;
 const tbl=document.getElementById('duraktable');
 if(!g.table.length){tbl.innerHTML='<span class="sub">стол пуст</span>';}
 else{tbl.innerHTML=g.table.map(p=>`<span style="display:inline-block;position:relative;margin:0 10px 6px 0">${dcard(p.a)}${p.d?('<span style="position:absolute;left:14px;top:10px">'+dcard(p.d)+'</span>'):''}</span>`).join('');}
 const hand=document.getElementById('durakhand');
 const canPlay=g.your_turn;
 hand.innerHTML=g.hand.map((c,i)=>dcard(c, canPlay?`durakPlay(${i})`:'')).join('')||'<span class="sub">карт нет</span>';
 document.getElementById('durakmsg').textContent=g.msg||'';
 const btns=document.getElementById('durakbtns');
 if(g.over){btns.innerHTML=`<button onclick="durakStart()">🔄 Ещё партию (10⌾)</button><button onclick="document.getElementById('durakgame').style.display='none'">Встать</button>`;}
 else if(g.role==='defend'&&g.your_turn){btns.innerHTML=`<button onclick="durakAct('take')">🖐 Беру</button>`;}
 else if(g.role==='attack'&&g.your_turn){btns.innerHTML=`<button onclick="durakAct('done')">✓ Бито</button>`;}
 else{btns.innerHTML='<span class="sub">ход корчмаря…</span>';}
}
let _durakGame=null;
async function durakStart(){const r=await wsCall('durak',{action:'start'});if(!r.ok){toast(r.msg);return;}_durakGame=r;renderDurak(r);}
async function durakAct(a){const r=await wsCall('durak',{action:a});if(!r.ok){toast(r.msg);return;}_durakGame=r;renderDurak(r);}
async function durakPlay(i){const g=_durakGame;if(!g||!g.your_turn)return;const a=(g.role==='attack')?'attack':'defend';const r=await wsCall('durak',{action:a,idx:i});if(!r.ok){toast(r.msg);return;}_durakGame=r;renderDurak(r);}
// ── PvP Очко ──
let _pvpOn=false;
function renderPvp(g){
 const box=document.getElementById('pvpgame');box.style.display='block';_pvpOn=!g.done;
 document.getElementById('pvptop').textContent=g.waiting?('за столом: '+(g.players||[]).join(', ')+' · ждём второго…'):('банк '+g.pot+'⌾ · за столом: '+(g.players||[]).join(' vs '));
 const op=document.getElementById('pvpopp');
 if(g.opponent){op.innerHTML=`соперник <b>${escHtml(g.opponent.name)}</b>: карт ${g.opponent.cards}`+(g.opponent.total!=null?(' · '+g.opponent.total):' · ?')+(g.opponent.stood?' (готов)':'');}
 else op.innerHTML='<span class="sub">соперника пока нет</span>';
 document.getElementById('pvphand').innerHTML=(g.hand||[]).map(cardEl).join('')||'<span class="sub">—</span>';
 document.getElementById('pvphand').insertAdjacentHTML('beforeend', g.total?` <span class="ctot">= ${g.total}</span>`:'');
 document.getElementById('pvpmsg').textContent=g.msg||'';
 const b=document.getElementById('pvpbtns');
 if(g.done){b.innerHTML=`<button onclick="pvpJoin()">🔄 Ещё партию</button><button onclick="pvpAct('leave')">Встать</button>`;}
 else if(g.waiting){b.innerHTML=`<button onclick="pvpAct('leave')">Отменить</button>`;}
 else if(g.stood){b.innerHTML='<span class="sub">ждём соперника…</span>';}
 else{b.innerHTML=`<button onclick="pvpAct('hit')">🎴 Ещё карту</button><button onclick="pvpAct('stand')">✋ Хватит</button>`;}
}
async function pvpJoin(){const r=await wsCall('pvp',{action:'join'});if(!r.ok){toast(r.msg);return;}renderPvp(r);}
async function pvpAct(a){const r=await wsCall('pvp',{action:a});if(!r.ok){toast(r.msg);return;}if(a==='leave'&&!r.seated){document.getElementById('pvpgame').style.display='none';return;}renderPvp(r);}
// ── PvP Дурак ──
let _dpvp=null;
function renderDpvp(g){
 const box=document.getElementById('dpvpgame');box.style.display='block';_dpvp=g;
 if(g.waiting){document.getElementById('dpvptop').textContent='за столом: '+(g.players||[]).join(', ')+' · ждём второго…';
  document.getElementById('dpvptable').innerHTML='';document.getElementById('dpvphand').innerHTML='';
  document.getElementById('dpvpmsg').textContent=g.msg||'';
  document.getElementById('dpvpbtns').innerHTML=`<button onclick="dpvpAct('leave')">Отменить</button>`;return;}
 document.getElementById('dpvptop').textContent=`козырь ${g.trump} ${g.trump_card?('('+g.trump_card+')'):''} · колода ${g.deck} · у ${escHtml(g.opp_name||'соперника')} ${g.opp_cards} карт · банк ${g.pot}⌾ · ты ${g.role==='attack'?'заходишь':'отбиваешься'}`;
 const tbl=document.getElementById('dpvptable');
 tbl.innerHTML=(!g.table||!g.table.length)?'<span class="sub">стол пуст</span>':g.table.map(p=>`<span style="display:inline-block;position:relative;margin:0 10px 6px 0">${cardEl(p.a)}${p.d?('<span style="position:absolute;left:14px;top:10px">'+cardEl(p.d)+'</span>'):''}</span>`).join('');
 const canPlay=g.your_turn&&!g.done;
 document.getElementById('dpvphand').innerHTML=(g.hand||[]).map((c,i)=>{const r=/[♥♦]/.test(c.label)?' red':'';const st=canPlay?'cursor:pointer':'';return `<div class="pcard${r}" style="${st}" onclick="${canPlay?('dpvpPlay('+i+')'):''}">${c.label}</div>`;}).join('')||'<span class="sub">карт нет</span>';
 document.getElementById('dpvpmsg').textContent=g.msg||'';
 const b=document.getElementById('dpvpbtns');
 if(g.done){b.innerHTML=`<button onclick="dpvpJoin()">🔄 Ещё партию</button><button onclick="dpvpAct('leave')">Встать</button>`;}
 else if(g.role==='defend'&&g.your_turn){b.innerHTML=`<button onclick="dpvpAct('take')">🖐 Беру</button>`;}
 else if(g.role==='attack'&&g.your_turn){b.innerHTML=`<button onclick="dpvpAct('done')">✓ Бито</button>`;}
 else{b.innerHTML='<span class="sub">ход соперника…</span>';}
}
async function dpvpJoin(){const r=await wsCall('dpvp',{action:'join'});if(!r.ok){toast(r.msg);return;}renderDpvp(r);}
async function dpvpAct(a){const r=await wsCall('dpvp',{action:a});if(!r.ok){toast(r.msg);return;}if(a==='leave'&&!r.seated){document.getElementById('dpvpgame').style.display='none';return;}renderDpvp(r);}
async function dpvpPlay(i){const g=_dpvp;if(!g||!g.your_turn)return;const a=(g.role==='attack')?'attack':'defend';const r=await wsCall('dpvp',{action:a,idx:i});if(!r.ok){toast(r.msg);return;}renderDpvp(r);}
async function fixTavern(){pending={kind:'fixtavern'};openMini({label:'Починка корчмы',minigame:'sequence',difficulty:0.4});}
async function dice(){pending={kind:'dice'};openMini({label:'Кости',minigame:'timing',difficulty:0.5});}
async function prio(m){toast((await wsCall('priority',{mode:m})).msg);}
async function craft(id){toast((await wsCall('craft',{id})).msg);}
async function build(id){toast((await wsCall('build',{id})).msg);}
async function family(){toast((await wsCall('family',{})).msg);}

if('serviceWorker' in navigator){try{navigator.serviceWorker.register('/sw.js');}catch(e){}}
document.getElementById('verbadge').textContent='v__VER__';
boot();
</script></body></html>"""


if __name__ == "__main__":
    threading.Thread(target=_loop, daemon=True).start()
    print(f"Сетевой мир: http://{HOST}:{PORT}  env={GAME_ENV} storage={STORAGE}  (Ctrl+C — стоп)")
    try:
        ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
    except KeyboardInterrupt:
        _running = False
        _world.save()
        print("\nмир остановлен, сохранено.")
