"""
Тонкий клиент — играбельное окно в живой мир из НЕСКОЛЬКИХ деревень.
    python3 thin.py   →  http://127.0.0.1:8090

Регион (world/region.py) тикает в фоне под одними часами — все деревни горячие,
так проверяется корректность тиков на многих поселениях разом. Ты вселён в жителя
одной деревни и можешь путешествовать караваном между ними (как в Fallout):
закупился дёшево в одной — продал дороже в другой (у деревень разный рынок).
Активности-мини-игры, квесты, услуги, жильё, предметы — в текущей деревне.

Сервер авторитетен: клиент шлёт лишь намерение и качество мини-игры, мир решает.
"""
from __future__ import annotations
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import threading, time, json, random

import world as W
from world import player as P
from world import quests as Q
from world import items as I
from world import names, playerstore
from world import persistence as PS
from world.model import Character
from world.region import make_region

PORT = 8090
SPEED = 1.0
N_VILLAGES = 3
WORLD_FILE = "/tmp/game_world.json"        # общий мир (без игроков)
PID = "local"                              # прототип: один локальный игрок = id аккаунта
AUTOSAVE_EVERY = 60                        # тиков между автосейвами

_lock = threading.Lock()

# мир: загрузить, если есть сохранение, иначе собрать заново
if PS.region_exists(WORLD_FILE):
    _region = PS.load_region(WORLD_FILE, seed=1)
else:
    _region = make_region(N_VILLAGES, seed=1)
_clock = _region.clock
_rng = random.Random(777)
_running = True
_transit = None

# игрок: войти по своему id (хранится отдельно от мира)
_pdata = playerstore.load(PID)
if _pdata:
    _me, _loc = _pdata
    if _me not in _region.settlements[_loc].residents:
        _region.settlements[_loc].residents.append(_me)   # вернуть игрока в его деревню
else:
    _g = random.Random(1)
    _sex = names.random_sex(_g)
    _f, _m = names.random_parents(_g)
    _me = Character(name="Ты", role="newcomer", driver="player", sex=_sex,
                    age=round(_g.uniform(18, 30), 1), father=_f, mother=_m, coins=60.0)
    _loc = 0
    _region.settlements[0].residents.append(_me)           # новый игрок приходит в мир
_prev_day = _clock.day


def _persist():
    """Сохранить мир и игрока (игрок — отдельной записью). Под _lock."""
    PS.save_region(_region, WORLD_FILE)
    loc = _loc if _loc is not None else (_transit["from"] if _transit else 0)
    playerstore.save(PID, _me, loc)


def cur():
    return None if _loc is None else _region.settlements[_loc]


def _activity_difficulty(w, activity: str) -> float:
    """Сложность мини-игры растёт с мастерством и с объёмом взятого квеста.
    Мастеру — настоящий вызов (и большая награда), новичку — посильно."""
    a = P.ACTIVITIES.get(activity, {})
    skill = _me.skill(a.get("skill", "")) if a else 0.0
    need = 0
    for q in Q.open_quests(w):
        if q.kind == "activity" and q.accepted and q.activity == activity:
            need = max(need, q.need)
    return round(max(0.1, min(0.9, 0.15 + skill / 120.0 + max(0, need - 2) * 0.06)), 2)


def _loop():
    global _prev_day, _loc, _transit
    ticks = 0
    while _running:
        with _lock:
            _region.step_hour()
            if _clock.day != _prev_day:
                w_here = cur()
                P.player_day(_me, cold=bool(w_here and getattr(w_here, "cold", False)))
                _prev_day = _clock.day
            # прибытие каравана
            if _transit and _clock.total_hours >= _transit["arrive"]:
                dest = _region.settlements[_transit["to"]]
                dest.residents.append(_me)
                dest.add_news(_clock, f"С караваном {_me.v('прибыл', 'прибыла')} {_me.name}.")
                _loc = _transit["to"]
                _transit = None
                _persist()                       # прибытие — хороший момент сохраниться
            ticks += 1
            if ticks % AUTOSAVE_EVERY == 0:
                _persist()
        time.sleep(1.0 / SPEED)


def _labels(ids):
    return [{"id": x, "label": I.label(x)} for x in ids]


def state() -> dict:
    with _lock:
        clock = _clock
        region_view = []
        for i, s in enumerate(_region.settlements):
            region_view.append({
                "idx": i, "name": s.name, "market": s.market,
                "pop": s.pop(), "health": s.health(),
                "hours": None if _loc is None else _region.dist_hours(_loc, i),
                "current": i == _loc,
            })
        me = {
            "name": _me.name, "sex": _me.sex, "age": round(_me.age, 1),
            "father": _me.father, "mother": _me.mother,
            "coins": round(_me.coins, 1), "home": _me.home,
            "comfort": round(_me.home_comfort), "fatigue": round(_me.fatigue),
            "hunger": round(_me.hunger),
            "skills": {k: round(v, 1) for k, v in _me.skills.items()},
            "favors": dict(_me.favors),
            "inventory": _labels(_me.inventory),
            "storage": _labels(_me.storage),
            "cargo": {k: round(v) for k, v in _me.cargo.items() if v > 0},
            "weapon": {"id": _me.weapon, "label": I.label(_me.weapon)} if _me.weapon else None,
            "outfit": {"id": _me.outfit, "label": I.label(_me.outfit)} if _me.outfit else None,
            "warmth": I.warmth(_me), "weapon_dmg": I.weapon_dmg(_me),
            "deliveries": [dict(d, can=(d["dest"] == _loc)) for d in _me.deliveries],
        }
        base = {
            "region": {"name": "Приграничье", "count": len(_region.settlements)},
            "day": clock.day, "hour": clock.hour,
            "me": me, "settlements": region_view,
            "shop": I.catalog_list(),
        }
        if _transit:
            dest = _region.settlements[_transit["to"]]
            base["transit"] = {"to": dest.name,
                               "left": max(0, _transit["arrive"] - clock.total_hours)}
            return base

        w = cur()
        base.update({
            "here": {
                "name": w.name, "market": w.market,
                "health": w.health(), "food_days": round(w.food_days(), 1),
                "store": {k: round(v) for k, v in w.store.items()},
                "alive": len(w.living()),
                "tavern": list(w.tavern_tonight),
                "cold": bool(getattr(w, "cold", False)),
                "news": list(w.news[-6:])[::-1],
                "chronicle": list(w.chronicle[-6:])[::-1],
                "graves": w.graves,
            },
            "activities": {k: {"label": v["label"], "minigame": v["minigame"],
                               "skill": v["skill"],
                               "difficulty": _activity_difficulty(w, k)}
                           for k, v in P.ACTIVITIES.items()},
            "quests": [q.as_dict() for q in Q.open_quests(w)
                       if not (q.kind == "deliver" and q.accepted)],
            "trade": {res: {"buy": P.buy_price(w, res), "sell": P.sell_price(w, res),
                            "store": round(w.store.get(res, 0))}
                      for res in P.TRADE_BASE},
            "house": {"rent": P.RENT_PER_DAY, "price": P.HOUSE_PRICE,
                      "furnish": P.FURNISH_STEP_COST},
        })
        return base


def _need_here():
    return cur() is not None


def act_do(body):
    with _lock:
        if not _need_here():
            return {"ok": False, "msg": "ты в пути с караваном"}
        w = cur()
        r = P.do_activity(w, _me, body.get("activity", ""),
                          float(body.get("quality", 0)), _rng)
        if r.get("ok"):
            info = Q.credit_activity(w, _me, r["activity"], _clock)
            if info:
                r["quest_done"] = info
                r["coins"] = round(_me.coins, 1)
                # звено цепочки завершено → заказчик просит доставку в другую деревню
                if info.get("chain"):
                    others = [i for i in range(len(_region.settlements)) if i != _loc]
                    if others:
                        dj = _rng.choice(others)
                        dq = Q.make_delivery(w, info["giver"], info["activity"],
                                             dj, _region.settlements[dj].name, _rng)
                        w.add_news(_clock, f"{info['giver']} просит отвезти {dq.parcel} "
                                           f"в {dq.dest_name}.")
                        r["chain_next"] = dq.as_dict()
        return r


def act_help(body):
    with _lock:
        if not _need_here():
            return {"ok": False, "msg": "ты в пути"}
        w = cur()
        others = [c for c in w.living() if c is not _me]
        npc = body.get("npc") or (_rng.choice(others).name if others else "сосед")
        return P.help_npc(w, _me, npc, float(body.get("quality", 0)), _rng)


def act_favor(body):
    with _lock:
        if not _need_here():
            return {"ok": False, "msg": "ты в пути"}
        return P.call_favor(cur(), _me, body.get("npc", ""), _rng)


def act_quest(body):
    with _lock:
        if not _need_here():
            return {"ok": False, "msg": "ты в пути"}
        w = cur()
        res = Q.accept(w, int(body.get("qid", 0)))
        q = res.get("quest")
        if res.get("ok") and q and q["kind"] == "deliver":
            # берём поклажу в дорогу — запоминаем доставку на игроке
            _me.deliveries.append({"src": _loc, "qid": q["qid"], "dest": q["dest"],
                                   "dest_name": q["dest_name"], "giver": q["giver"],
                                   "parcel": q["parcel"], "title": q["title"],
                                   "reward": q["reward"]})
        return res


def act_deliver(body):
    with _lock:
        if not _need_here():
            return {"ok": False, "msg": "ты в пути"}
        qid = int(body.get("qid", 0))
        d = next((x for x in _me.deliveries if x["qid"] == qid), None)
        if not d:
            return {"ok": False, "msg": "нет такой поклажи"}
        if _loc != d["dest"]:
            return {"ok": False, "msg": f"сдать можно в {d['dest_name']}"}
        src = _region.settlements[d["src"]]
        res = Q.complete_delivery(src, cur(), _me, qid, _clock)
        if res.get("ok"):
            _me.deliveries.remove(d)
        return res


def act_house(body):
    with _lock:
        a = body.get("action")
        if a == "rent":    return P.rent_room(_me)
        if a == "buy":     return P.buy_house(_me)
        if a == "furnish": return P.furnish(_me)
        return {"ok": False, "msg": "неизвестное действие"}


def act_item(body):
    with _lock:
        a, iid = body.get("action"), body.get("id", "")
        if a == "buy":     return I.buy(_me, iid)
        if a == "sell":    return I.sell(_me, iid)
        if a == "store":   return I.to_storage(_me, iid)
        if a == "unstore": return I.from_storage(_me, iid)
        if a == "equip":   return I.equip(_me, iid)
        if a == "unequip": return I.unequip(_me, body.get("slot", ""))
        return {"ok": False, "msg": "?"}


def act_trade(body):
    with _lock:
        if not _need_here():
            return {"ok": False, "msg": "ты в пути"}
        w, a = cur(), body.get("action")
        res, amt = body.get("res", ""), float(body.get("amount", 10))
        if a == "buy":  return P.buy_goods(w, _me, res, amt)
        if a == "sell": return P.sell_goods(w, _me, res, amt)
        return {"ok": False, "msg": "?"}


def act_travel(body):
    global _loc, _transit
    with _lock:
        if _transit:
            return {"ok": False, "msg": "уже в пути"}
        to = int(body.get("to", -1))
        if to < 0 or to >= len(_region.settlements) or to == _loc:
            return {"ok": False, "msg": "некуда"}
        hours = _region.dist_hours(_loc, to)
        src = _region.settlements[_loc]
        if _me in src.residents:
            src.residents.remove(_me)        # уходим из деревни на время пути
        _transit = {"to": to, "from": _loc, "arrive": _clock.total_hours + hours}
        _loc = None
        return {"ok": True, "msg": f"Вышел с караваном в {_region.settlements[to].name} "
                                   f"(~{hours} ч пути)."}


def act_save(body):
    with _lock:
        _persist()
    return {"ok": True, "msg": "Сохранено."}


ROUTES = {"/api/do": act_do, "/api/help": act_help, "/api/favor": act_favor,
          "/api/quest": act_quest, "/api/house": act_house, "/api/item": act_item,
          "/api/trade": act_trade, "/api/travel": act_travel, "/api/deliver": act_deliver,
          "/api/save": act_save}


class Handler(BaseHTTPRequestHandler):
    def _send(self, obj, ctype="application/json"):
        body = (obj if isinstance(obj, bytes)
                else json.dumps(obj, ensure_ascii=False).encode("utf-8"))
        self.send_response(200)
        self.send_header("Content-Type", f"{ctype}; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            self._send(PAGE.encode("utf-8"), "text/html")
        elif self.path == "/api/state":
            self._send(state())
        else:
            self.send_error(404)

    def do_POST(self):
        fn = ROUTES.get(self.path)
        if not fn:
            self.send_error(404); return
        n = int(self.headers.get("Content-Length", 0))
        try:
            body = json.loads(self.rfile.read(n) or b"{}")
        except Exception:
            body = {}
        self._send(fn(body))

    def log_message(self, *a): pass


PAGE = r"""<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Приграничье — живой мир</title>
<style>
 :root{--bg:#1b1b21;--card:#26262e;--line:#34343f;--ink:#e8e6e3;--sub:#9b9a97;
       --good:#6ab04c;--warn:#f0932b;--bad:#eb4d4b;--gold:#e1b12c;--accent:#7b68ee}
 *{box-sizing:border-box} body{background:var(--bg);color:var(--ink);
   font:15px/1.5 system-ui,sans-serif;max-width:900px;margin:0 auto;padding:18px}
 h1{font-size:21px;margin:0} .sub{color:var(--sub);font-size:13px}
 .card{background:var(--card);border:1px solid var(--line);border-radius:12px;
       padding:14px 16px;margin:12px 0}
 .row{display:flex;gap:10px;flex-wrap:wrap;align-items:center}
 .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}
 button{background:#30303a;color:var(--ink);border:1px solid var(--line);
         border-radius:9px;padding:9px 11px;font:inherit;cursor:pointer}
 button:hover{border-color:var(--accent)} button:disabled{opacity:.4;cursor:default}
 .act b{display:block} .act .sk{color:var(--sub);font-size:12px}
 .pill{background:#30303a;border:1px solid var(--line);border-radius:20px;
       padding:4px 11px;font-size:13px} .gold{color:var(--gold)}
 .tag{font-size:11px;padding:2px 7px;border-radius:10px;background:#333}
 .mini{position:fixed;inset:0;background:rgba(0,0,0,.7);display:none;
        align-items:center;justify-content:center;z-index:9}
 .mini.on{display:flex} .stage{background:var(--card);border:1px solid var(--line);
        border-radius:14px;padding:18px;width:min(520px,92vw);text-align:center}
 #field{position:relative;height:240px;background:#1b1b21;border:1px solid var(--line);
        border-radius:10px;margin:12px 0;overflow:hidden;cursor:crosshair}
 .target{position:absolute;width:44px;height:44px;border-radius:50%;
        background:radial-gradient(circle at 35% 35%,#ffd36b,#e1932c);cursor:pointer}
 .seqbtn{font-size:22px;width:60px;height:60px;margin:4px}
 .bar{position:absolute;top:0;bottom:0;width:4px;background:#fff}
 .zone{position:absolute;top:0;bottom:0;background:rgba(106,176,76,.3)}
 #tbar{position:relative;height:46px;background:#1b1b21;border:1px solid var(--line);
       border-radius:8px;margin:12px 0;overflow:hidden}
 table{width:100%;border-collapse:collapse} td,th{padding:3px 6px;text-align:left}
 ul{margin:4px 0 0;padding-left:18px} li{color:#cfcdc9;margin:2px 0;font-size:13px}
 .toast{position:fixed;left:50%;bottom:22px;transform:translateX(-50%);background:#30303a;
   border:1px solid var(--accent);border-radius:10px;padding:10px 16px;opacity:0;
   transition:.3s;pointer-events:none;max-width:90vw;text-align:center} .toast.on{opacity:1}
 .cols{display:grid;grid-template-columns:1fr 1fr;gap:12px}
 @media(max-width:640px){.cols{grid-template-columns:1fr}}
 .mut{color:var(--sub);font-size:12px}
</style></head><body>
<div class="row" style="justify-content:space-between">
 <div><h1 id="town">…</h1><div class="sub" id="when">…</div></div>
 <div class="row">
  <span class="pill gold" id="coins">⌾ 0</span>
  <span class="pill" id="home">🏚</span>
  <span class="pill" id="who">—</span>
  <span class="pill" id="gear">—</span>
  <button onclick="save()">💾 Сохранить</button>
 </div>
</div>

<div class="card"><b>🗺 Деревни региона</b> <span class="sub">— путешествуй караваном, торгуй на разнице цен</span>
 <div class="row" id="map" style="margin-top:10px"></div>
 <div class="sub" id="transit" style="margin-top:8px"></div>
</div>

<div id="local">
 <div class="card"><div class="row" style="justify-content:space-between">
  <div><b id="health">здоровье —</b> <span class="sub" id="store"></span></div>
  <div class="sub" id="body"></div>
 </div></div>

 <div class="card"><b>Чем займёшься?</b><div class="grid" id="acts" style="margin-top:10px"></div></div>

 <div class="card"><b>📜 Просьбы деревни</b> <span class="sub">— живые нужды; выполнишь — будет должен</span>
  <div id="quests" style="margin-top:10px"></div>
  <div id="deliveries" style="margin-top:10px"></div></div>

 <div class="cols">
  <div class="card"><b>🧺 Караван-торг</b> <span class="sub">рынок ×<span id="mk">1</span></span>
   <div id="trade" style="margin-top:8px"></div>
   <div class="sub" id="cargo" style="margin-top:8px"></div></div>
  <div class="card"><b>🛒 Лавка</b> <span class="sub">оружие и одежда (пока косметика)</span>
   <div id="shop" style="margin-top:8px"></div></div>
 </div>

 <div class="cols">
  <div class="card"><b>🎒 При себе / 🏠 Склад</b><div id="inv" style="margin-top:8px"></div></div>
  <div class="card"><b>Жильё и услуги</b>
   <div class="row" id="house" style="margin-top:8px"></div>
   <div class="row" style="margin-top:8px"><button onclick="startHelp()">🤝 Помочь соседу</button>
    <span class="sub" id="favors"></span></div></div>
 </div>

 <div class="card"><b>🍯 Таверна</b> <span id="tav" class="sub"></span></div>

 <div class="cols">
  <div class="card"><b>Что говорят</b><ul id="news"></ul></div>
  <div class="card"><b>Память деревни</b> <span class="sub" id="graves"></span><ul id="chron"></ul></div>
 </div>
</div>

<div class="mini" id="mini"><div class="stage">
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
async function refresh(){S=await api('/api/state');render();}

function render(){
 document.getElementById('coins').textContent='⌾ '+S.me.coins;
 const hm={none:'🏚 без крова',rented:'🏠 комната',owned:'🏡 свой дом'}[S.me.home];
 document.getElementById('home').textContent=hm+(S.me.home!=='none'?` ·уют ${S.me.comfort}`:'');
 document.getElementById('who').textContent=`${S.me.sex==='ж'?'♀':'♂'} ${S.me.age}л`;
 const gw=(S.me.weapon?S.me.weapon.label:'без оружия')+' · '+(S.me.outfit?S.me.outfit.label:'без одежды');
 document.getElementById('gear').textContent='⚔ '+gw;
 document.getElementById('when').textContent=
   `Регион «${S.region.name}», ${S.region.count} деревни · День ${S.day}, ${String(S.hour).padStart(2,'0')}:00`;

 // карта деревень
 const map=document.getElementById('map');map.innerHTML='';
 for(const v of S.settlements){
   const hc=v.health>60?'var(--good)':v.health>25?'var(--warn)':'var(--bad)';
   const d=document.createElement('div');d.className='pill';
   const mk=v.market>1.1?'🔺дорого':v.market<0.9?'🔻дёшево':'≈';
   if(v.current){d.style.borderColor='var(--gold)';
     d.innerHTML=`📍<b>${v.name}</b> <span class="mut">·жит ${v.pop} ·${mk}</span>`;}
   else if(S.transit){d.innerHTML=`${v.name} <span class="mut">${mk}</span>`;}
   else{d.innerHTML=`<span style="color:${hc}">●</span> ${v.name} <span class="mut">·жит ${v.pop} ·${mk} ·${v.hours}ч</span> `+
     `<button onclick="travel(${v.idx})">→ в путь</button>`;}
   map.appendChild(d);
 }
 const loc=document.getElementById('local');
 if(S.transit){
   document.getElementById('transit').textContent=`🐫 В пути в ${S.transit.to}: осталось ~${S.transit.left} ч. Караван идёт, мир живёт.`;
   loc.style.display='none';
   document.getElementById('town').textContent='🐫 В пути…';
   return;
 }
 document.getElementById('transit').textContent='';
 loc.style.display='';
 const H=S.here;
 document.getElementById('town').textContent=H.name;
 const hc=H.health>60?'var(--good)':H.health>25?'var(--warn)':'var(--bad)';
 const coldb=H.cold?' <span class="tag" style="background:#2d4a63">❄ холода</span>':'';
 document.getElementById('health').innerHTML=`<span style="color:${hc}">здоровье ${H.health}</span>`+coldb;
 document.getElementById('store').textContent='· еды на '+H.food_days+' дн · склад: '+
   Object.entries(H.store).map(([k,v])=>k+' '+v).join(' · ');
 document.getElementById('body').textContent=`голод ${S.me.hunger} · усталость ${S.me.fatigue}`;
 document.getElementById('mk').textContent=H.market;

 // активности
 const ac=document.getElementById('acts');ac.innerHTML='';
 for(const [k,a] of Object.entries(S.activities)){
   const sk=S.me.skills[a.skill]?(' · '+a.skill+' '+S.me.skills[a.skill]):(' · '+a.skill);
   const b=document.createElement('button');b.className='act';
   b.innerHTML=`<b>${a.label}</b><span class="sk">${a.minigame}${sk}</span>`;
   b.onclick=()=>startAct(k);ac.appendChild(b);
 }
 // квесты
 const qc=document.getElementById('quests');
 if(!S.quests.length)qc.innerHTML='<span class="sub">пока деревне всего хватает</span>';
 else{qc.innerHTML='';for(const q of S.quests){
   const lab=S.activities[q.activity]?S.activities[q.activity].label:q.activity;
   const d=document.createElement('div');d.className='row';d.style.margin='6px 0';
   if(q.accepted)d.innerHTML=`<span class="pill">📜 ${q.giver}: ${q.title}</span>`+
     `<span class="sub">${lab} — ${q.have}/${q.need} · ${q.reward}⌾ +услуга</span>`;
   else{const tail=q.chain?' <span class="tag" style="background:#3a3350">🔗 цепочка</span>':'';
     const verb=q.kind==='deliver'?'Взять поклажу':'Взяться';
     d.innerHTML=`<span>📜 <b>${q.giver}</b>: ${q.title} <span class="sub">(${q.kind==='deliver'?'доставка':lab}, ${q.reward}⌾)</span>${tail}</span>`+
       `<button onclick="accept(${q.qid})">${verb}</button>`;}
   qc.appendChild(d);}}
 // мои доставки (поклажи в пути между деревнями)
 const dv=document.getElementById('deliveries');
 if(!S.me.deliveries.length)dv.innerHTML='';
 else{dv.innerHTML='<div class="mut">🐫 несу поклажу:</div>';
   for(const d of S.me.deliveries){const row=document.createElement('div');row.className='row';row.style.margin='4px 0';
     row.innerHTML=`<span class="pill">📦 ${d.parcel} → ${d.dest_name} <span class="mut">(от ${d.giver}, ${d.reward}⌾)</span></span>`+
       (d.can?`<button onclick="deliver(${d.qid})">сдать здесь</button>`:`<span class="sub">довези до ${d.dest_name}</span>`);
     dv.appendChild(row);}}
 // торг караваном
 const tr=document.getElementById('trade');tr.innerHTML='';
 for(const [res,p] of Object.entries(S.trade)){
   const row=document.createElement('div');row.className='row';row.style.margin='4px 0';
   row.innerHTML=`<span style="width:70px">${res}</span>`+
     `<span class="mut">куп ${p.buy} / прод ${p.sell} · есть ${p.store}</span>`+
     `<button onclick="trade('buy','${res}')">купить 10</button>`+
     `<button onclick="trade('sell','${res}')">продать 10</button>`;
   tr.appendChild(row);
 }
 const cg=Object.entries(S.me.cargo);
 document.getElementById('cargo').innerHTML = cg.length
   ? '🧺 в грузе: '+cg.map(([k,v])=>`${k} ${v}`).join(' · ') : '<span class="sub">груз пуст</span>';
 // лавка
 const sh=document.getElementById('shop');sh.innerHTML='';
 for(const it of S.shop){const b=document.createElement('button');
   b.innerHTML=`${it.icon} ${it.name} <span class="mut">${it.price}⌾</span>`;
   b.onclick=()=>item('buy',it.id);sh.appendChild(b);}
 // экипировка
 const inv=document.getElementById('inv');inv.innerHTML='';
 inv.innerHTML+='<div class="mut">надето:</div>';
 if(S.me.weapon)inv.innerHTML+=`<button onclick="item('unequip','','weapon')">${S.me.weapon.label} ⚔ <span class="mut">+${Math.round(S.me.weapon_dmg*5)}% охота ✕</span></button>`;
 if(S.me.outfit)inv.innerHTML+=`<button onclick="item('unequip','','clothing')">${S.me.outfit.label} 🧥 <span class="mut">тепло ${S.me.warmth} ✕</span></button>`;
 if(!S.me.weapon&&!S.me.outfit)inv.innerHTML+='<span class="sub">ничего</span>';
 // при себе
 inv.innerHTML+='<div class="mut" style="margin-top:6px">🎒 при себе:</div>';
 const bag=document.createElement('div');bag.className='row';
 if(!S.me.inventory.length)bag.innerHTML='<span class="sub">пусто</span>';
 S.me.inventory.forEach(it=>{
   const d=document.createElement('span');d.className='pill';
   const slot=(S.shop.find(x=>x.id===it.id)||{}).slot;
   const eq=(slot==='weapon'||slot==='clothing')?`<a href="#" onclick="item('equip','${it.id}');return false">надеть</a> · `:'';
   d.innerHTML=`${it.label} <span class="mut">${eq}<a href="#" onclick="item('store','${it.id}');return false">склад</a> · `+
     `<a href="#" onclick="item('sell','${it.id}');return false">продать</a></span>`;
   bag.appendChild(d);});
 inv.appendChild(bag);
 // склад
 inv.innerHTML+='<div class="mut" style="margin-top:6px">🏠 склад:</div>';
 const st=document.createElement('div');st.className='row';
 if(!S.me.storage.length)st.innerHTML='<span class="sub">пусто</span>';
 S.me.storage.forEach(it=>{const b=document.createElement('button');b.textContent=it.label+' ↩';
   b.title='забрать';b.onclick=()=>item('unstore',it.id);st.appendChild(b);});
 inv.appendChild(st);
 // жильё
 const hs=document.getElementById('house');hs.innerHTML='';
 if(S.me.home!=='owned'){hs.innerHTML+=`<button onclick="house('rent')">Снять (${S.house.rent}/д)</button>`;
   hs.innerHTML+=`<button onclick="house('buy')">Купить дом (${S.house.price})</button>`;}
 if(S.me.home!=='none')hs.innerHTML+=`<button onclick="house('furnish')">Обустроить (${S.house.furnish})</button>`;
 // услуги
 const fv=Object.entries(S.me.favors).filter(([n,c])=>c>0);
 document.getElementById('favors').innerHTML=fv.length
   ? 'Должны: '+fv.map(([n,c])=>`<button onclick="callFavor('${n}')">${n}×${c}↩</button>`).join(' ')
   : '<span class="sub">никто не должен</span>';
 // таверна/лента
 document.getElementById('tav').textContent=H.tavern.length?'за медовухой: '+H.tavern.join(', '):'тиха';
 document.getElementById('news').innerHTML=H.news.map(n=>`<li>${n}</li>`).join('')||'<li>тихо…</li>';
 document.getElementById('graves').textContent='· могил '+H.graves;
 document.getElementById('chron').innerHTML=H.chronicle.map(n=>`<li>${n}</li>`).join('')||'<li>—</li>';
}

// ── мини-игры ──
let DIFF=0.3;
function startAct(k){pending={kind:'do',activity:k};openMini(S.activities[k]);}
function startHelp(){pending={kind:'help'};openMini({label:'Помощь со стройкой',minigame:'sequence',difficulty:0.3});}
function openMini(a){document.getElementById('mini').classList.add('on');
 DIFF=a.difficulty||0.3;
 document.getElementById('mtitle').textContent=a.label;const mg=a.minigame;
 const lvl=DIFF<0.3?'просто':DIFF<0.55?'средне':DIFF<0.75?'сложно':'мастерски';
 const hints={timing:'Останови бегунок в зелёной зоне. 3 попытки.',
  sequence:'Повтори показанную последовательность.',targets:'Лови цели — успей кликнуть.'};
 document.getElementById('mhint').textContent=hints[mg]+` · сложность: ${lvl}`;
 const body=document.getElementById('mbody');body.innerHTML='';
 if(mg==='timing')miniTiming(body);else if(mg==='sequence')miniSequence(body);else miniTargets(body);}
function closeMini(){document.getElementById('mini').classList.remove('on');
 if(window._mt)cancelAnimationFrame(window._mt);}
async function finish(q){closeMini();let r;
 if(pending.kind==='do')r=await api('/api/do',{activity:pending.activity,quality:q});
 else r=await api('/api/help',{quality:q});
 toast((r.msg||'')+(r.pay?` (+${r.pay}⌾)`:'')+(r.skill_level?` · ${r.skill} ${r.skill_level}`:''));
 if(r.quest_done)setTimeout(()=>toast(`✅ Квест для ${r.quest_done.giver}: +${r.quest_done.reward}⌾, теперь он должен услугу`),2500);
 if(r.chain_next)setTimeout(()=>toast(`🔗 ${r.chain_next.giver} просит: ${r.chain_next.title} (${r.chain_next.reward}⌾) — возьми на доске`),5000);
 refresh();}
function miniTiming(body){body.innerHTML='<div id="tbar"><div class="zone"></div><div class="bar"></div></div><button id="hit">Бей! (3)</button>';
 const bar=body.querySelector('.bar'),zone=body.querySelector('.zone');
 const zw=Math.max(8,30-DIFF*24),zx=10+Math.random()*(90-zw);   // уже зона — сложнее
 zone.style.left=zx+'%';zone.style.width=zw+'%';
 const spd=1.2+DIFF*2.0;                                         // быстрее бегунок — сложнее
 let pos=0,dir=1,left=3,sc=[];
 function step(){pos+=dir*spd;if(pos>100){pos=100;dir=-1}if(pos<0){pos=0;dir=1}bar.style.left=pos+'%';window._mt=requestAnimationFrame(step);}step();
 body.querySelector('#hit').onclick=e=>{const c=zx+zw/2,dist=Math.abs(pos-c),half=zw/2;
  sc.push(Math.max(0,1-dist/(half+18)));left--;e.target.textContent=`Бей! (${left})`;
  if(left<=0){cancelAnimationFrame(window._mt);finish(sc.reduce((a,b)=>a+b,0)/sc.length);}};}
function miniSequence(body){const icons=['🌿','🪵','🫐','🐟','🔨','🌾'];let seq=[],inp=[];
 const n=3+Math.round(DIFF*5);for(let i=0;i<n;i++)seq.push(Math.floor(Math.random()*icons.length)); // длиннее — сложнее
 body.innerHTML='<div id="show" style="font-size:30px;letter-spacing:6px;min-height:40px"></div><div id="pad"></div>';
 const sh=body.querySelector('#show'),pad=body.querySelector('#pad');let i=0;
 const t=setInterval(()=>{sh.textContent=icons[seq[i]];i++;if(i>=seq.length){clearInterval(t);
  setTimeout(()=>{sh.textContent='повтори →';icons.forEach((ic,idx)=>{const b=document.createElement('button');
   b.className='seqbtn';b.textContent=ic;b.onclick=()=>{inp.push(idx);
    if(inp[inp.length-1]!==seq[inp.length-1]){finish((inp.length-1)/seq.length);return;}
    if(inp.length===seq.length)finish(1);};pad.appendChild(b);});},600);}},650);}
function miniTargets(body){body.innerHTML='<div id="field"></div><div class="sub" id="cnt">поймано 0</div>';
 const f=body.querySelector('#field'),cnt=body.querySelector('#cnt');let c=0,sp=0,done=false;
 const tot=8+Math.round(DIFF*8),life=Math.max(450,950-DIFF*520),gap=Math.max(240,460-DIFF*240);
 function spawn(){if(done)return;sp++;const t=document.createElement('div');t.className='target';
  t.style.left=(Math.random()*82)+'%';t.style.top=(Math.random()*76)+'%';
  t.onclick=()=>{c++;cnt.textContent='поймано '+c;t.remove();};f.appendChild(t);
  setTimeout(()=>t.remove(),life);if(sp<tot)setTimeout(spawn,gap+Math.random()*200);}spawn();
 setTimeout(()=>{done=true;f.querySelectorAll('.target').forEach(t=>t.remove());finish(Math.min(1,c/tot));},6000);}

async function accept(q){toast((await api('/api/quest',{qid:q})).msg);refresh();}
async function callFavor(n){toast((await api('/api/favor',{npc:n})).msg);refresh();}
async function house(a){toast((await api('/api/house',{action:a})).msg);refresh();}
async function item(a,id,slot){toast((await api('/api/item',{action:a,id,slot})).msg);refresh();}
async function trade(a,res){toast((await api('/api/trade',{action:a,res,amount:10})).msg);refresh();}
async function travel(to){toast((await api('/api/travel',{to})).msg);refresh();}
async function deliver(qid){toast((await api('/api/deliver',{qid})).msg);refresh();}
async function save(){toast((await api('/api/save',{})).msg);}

refresh();setInterval(refresh,2000);
</script></body></html>"""


if __name__ == "__main__":
    threading.Thread(target=_loop, daemon=True).start()
    print(f"Играбельное окно (регион из {N_VILLAGES} деревень): "
          f"http://127.0.0.1:{PORT}  (Ctrl+C — стоп)")
    try:
        ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
    except KeyboardInterrupt:
        _running = False
        with _lock:
            _persist()
        print("\nмир остановлен, прогресс сохранён.")
