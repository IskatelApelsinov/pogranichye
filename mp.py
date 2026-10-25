"""
Мультиплеер-рантайм: общий мир из НЕСКОЛЬКИХ ШАРДОВ, много игроков. Вся игровая
логика — из world/; здесь «кто есть кто», шардинг и применение намерений.

Шардинг (деление мира на части): мир — список шардов, каждый шард — самостоятельный
Region (несколько деревень) со своими часами; тикают в ногу. Деревни адресуются
ГЛОБАЛЬНЫМ индексом; mp знает, в каком шарде какая. Путешествие в деревню другого
шарда = ПЕРЕДАЧА (handoff): персонаж сериализуется из одного шарда и воссоздаётся
в другом — ровно то, что в бою произойдёт через сеть между процессами.

Решения: 1 аккаунт — 1 персонаж; перма-смерть с хроникой; офлайн-игрок переходит
под AI (driver='ai', флаг afk) и остаётся в мире: пользуется своими предметами и
только ПРИБАВЛЯЕТ (не теряет старое).
"""
from __future__ import annotations
import threading, time, random
from dataclasses import asdict

from world import player as P
from world import quests as Q
from world import items as I
from world import names
from world import traits as _traits
from world.durak import Durak, YOU as DURAK_YOU, BOT as DURAK_BOT
from world import economy as ECON
from world import combat as C
from world import roadevents as RE
from world import tuning as T
from world.content import (TAVERN_TOASTS, TAVERN_RUMORS,
                            NPC_CHAT_REPLIES, NPC_KEEPER_REPLIES)
from world.model import Character
from world.region import make_shards
from world.archive import restore_settlement
from world import persistence as PS
from world.storage import JsonStorage

# 10 деревень на 3 шардах — проверяем шардинг и LoD «в бою»
SHARD_GROUPS = [[0, 1, 2, 3], [4, 5, 6], [7, 8, 9]]
OFFLINE_AFTER = 25.0
AUTOSAVE_EVERY = 60
COOL_AFTER = 30                     # тиков пустоты до «остывания» деревни (гистерезис)


class Slot:
    def __init__(self, aid, char, loc):
        self.aid = aid; self.char = char; self.loc = loc
        self.transit = None; self.last_seen = time.time()
        self.online = True; self.dead = False
        self.pending = None            # незакрытый энкаунтер (бой): {'beast','where'}
        self.choice = None             # незакрытая развилка квеста: {'giver','activity','options'}
        self.drank = -99               # час последнего тоста (для совместного питья)
        self.card = None               # партия в «очко» в таверне: {deck,player,dealer,bet,done}
        self.durak = None              # партия в «дурака»: world.durak.Durak
        self.durak_bet = 0


class World:
    def __init__(self, seed: int = 1, storage=None):
        self.lock = threading.RLock()
        self.store = storage or JsonStorage()
        self.rng = random.Random(777)
        data = self.store.world_load() if self.store.world_exists() else None
        if data and "shards" in data:
            self.shards = [PS.region_from_dict(d, seed) for d in data["shards"]]
        else:
            self.shards = make_shards(SHARD_GROUPS, seed)
        self._reindex()
        self.clock = self.shards[0].clock           # все часы шардов идут в ногу
        self.players: dict[str, Slot] = {}
        self._prev_day = self.clock.day
        self._ticks = 0
        self._empty = {}                             # глоб.индекс -> тиков без игрока
        self._hot_applied = {}                       # shard_id -> последний применённый набор горячих
        self._chat = {}                              # глоб.индекс деревни -> [ {who,text,day} ] (эфемерный чат в баре)

    # ── глобальная адресация деревень поверх шардов ──
    def _reindex(self):
        self._map = []                               # глоб.индекс -> (shard_id, local_idx)
        self._shard_globals = {}                     # shard_id -> [глоб.индексы по порядку]
        for sid, reg in enumerate(self.shards):
            self._shard_globals[sid] = []
            for li in range(len(reg.settlements)):
                self._shard_globals[sid].append(len(self._map))
                self._map.append((sid, li))

    def _settle(self, g):
        sid, li = self._map[g]; return self.shards[sid].settlements[li]

    def _shard(self, g):
        return self._map[g][0]

    def _nset(self):
        return len(self._map)

    def _dist(self, a, b):
        sa, sb = self._settle(a), self._settle(b)
        d = ((sa.x - sb.x) ** 2 + (sa.y - sb.y) ** 2) ** 0.5
        return max(2, int(round(d)))

    def _seed_known(self, char, loc):
        """Туман войны: родную деревню — «был», соседей по шарду — «слышал»."""
        k = char.known if isinstance(char.known, dict) else {}
        for g in range(self._nset()):
            if self._shard(g) == self._shard(loc):
                k[str(g)] = max(k.get(str(g), 0), 1)
        k[str(loc)] = 2
        char.known = k

    def _learn(self, char, g, level):
        """Узнать о деревне (1 слышал / 2 был), не понижая уже известного."""
        char.known[str(g)] = max(char.known.get(str(g), 0), level)

    def _warm(self, g):
        """Разогреть деревню сейчас (вернуть тела из архива), чтобы игрок не затёрся
        последующей разархивацией. Температуру закрепит _manage_temp."""
        s = self._settle(g)
        if s.archived:
            restore_settlement(s)
        self._empty[g] = 0

    # ── вход/офлайн ──
    def join(self, aid):
        with self.lock:
            slot = self.players.get(aid)
            if slot and not slot.dead:
                slot.online = True; slot.last_seen = time.time()
                if slot.char.alive:
                    slot.char.driver = "player"; slot.char.afk = False
                return slot
            loaded = self.store.player_load(aid)
            if loaded and loaded[0].alive:
                char, loc = loaded
            else:
                char, loc = self._newborn(aid)
            char.driver = "player"; char.afk = False
            self._seed_known(char, loc)              # знания о мире (туман войны)
            self._warm(loc)                          # вернуть деревню в горячий режим перед входом
            s = self._settle(loc)
            if char not in s.residents:
                s.residents.append(char)
            slot = Slot(aid, char, loc); self.players[aid] = slot
            s.add_news(self.clock, f"{char.name} объявился в деревне.")
            return slot

    def _newborn(self, aid):
        """Новый игрок рождается в СЛУЧАЙНОЙ деревне со случайной предысторией:
        либо местный житель, либо пришлый странник (как и задумано)."""
        g = random.Random(hash(aid) & 0xffffffff)
        loc = g.randrange(self._nset())
        sex = names.random_sex(g); f, m = names.random_parents(g)
        village = self._settle(loc).name
        if g.random() < 0.5:
            origin = f"местный, родом из деревни {village}"; role = "villager"
        else:
            origin = f"странник, пришедший в {village} издалека"; role = "newcomer"
        surname, nick = names.identity(sex, role, g)
        char = Character(name=names.random_name(sex, g), role=role, driver="player",
                         sex=sex, age=round(g.uniform(18, 35), 1), father=f, mother=m,
                         surname=surname, nick=nick,
                         coins=60.0, origin=origin, trait=_traits.assign(g))
        I.outfit_for_role(char, g)                    # новичок приходит одетым по ремеслу
        return char, loc

    def touch(self, aid):
        with self.lock:
            slot = self.players.get(aid)
            if slot and not slot.dead:
                slot.last_seen = time.time(); slot.online = True
                if slot.char.alive and slot.char.driver != "player":
                    slot.char.driver = "player"; slot.char.afk = False

    def leave(self, aid):
        with self.lock:
            slot = self.players.get(aid)
            if slot and not slot.dead and slot.char.alive:
                slot.online = False
                slot.char.driver = "ai"; slot.char.afk = True   # офлайн → AI пользуется телом

    def _manage_temp(self):
        """Параллельная жизнь локаций (LoD): горячо там, где игрок (или куда он едет —
        греем заранее, чтобы прибытие было бесшовным); пустое остывает с гистерезисом
        и считается дёшево (offline). Так детально живёт лишь занятая часть мира."""
        desired = set()
        for slot in self.players.values():
            if slot.dead:
                continue
            if slot.loc is not None:
                desired.add(slot.loc)
            if slot.transit:
                desired.add(slot.transit["to"])      # прогрев деревни назначения заранее
        hot = set()
        for g in range(self._nset()):
            if g in desired:
                self._empty[g] = 0; hot.add(g)
            else:
                self._empty[g] = self._empty.get(g, 0) + 1
                if self._empty[g] <= COOL_AFTER:
                    hot.add(g)                        # ещё в гистерезисе — держим горячей
        for sid, reg in enumerate(self.shards):
            local = [li for li, g in enumerate(self._shard_globals[sid]) if g in hot]
            if self._hot_applied.get(sid) != local:
                reg.set_hot(local)                    # смена режима: архивация/разархивация тел
                self._hot_applied[sid] = local

    # ── такт мира ──
    def tick(self):
        with self.lock:
            self._manage_temp()
            for reg in self.shards:
                reg.step_hour()
            if self.clock.day != self._prev_day:
                for slot in self.players.values():
                    if slot.char.alive:
                        w = self._cur(slot)
                        P.player_day(slot.char, cold=bool(w and getattr(w, "cold", False)))
                self._cross_migrate()               # переселение между шардами (переполнение)
                self._prev_day = self.clock.day
            now = time.time()
            night = self.clock.hour in T.SLEEP_HOURS
            for slot in list(self.players.values()):
                if slot.transit and self.clock.total_hours >= slot.transit["arrive"]:
                    self._arrive(slot)
                # ночью персонаж сам идёт спать (и онлайн, и в отлучке под AI)
                if night and slot.char.alive and slot.loc is not None:
                    rec = 3.0 if slot.char.home != "none" else 1.0
                    slot.char.fatigue = max(0.0, slot.char.fatigue - rec)
                if slot.online and now - slot.last_seen > OFFLINE_AFTER:
                    slot.online = False
                    if slot.char.alive:
                        slot.char.driver = "ai"; slot.char.afk = True
                if not slot.char.alive and not slot.dead:
                    self._bury(slot)
            self._ticks += 1
            if self._ticks % AUTOSAVE_EVERY == 0:
                self._persist()

    def _bury(self, slot):
        loc = slot.loc if slot.loc is not None else (slot.transit["from"] if slot.transit else 0)
        self._warm(loc); w = self._settle(loc)
        w.graves += 1
        dead = slot.char
        w.add_chronicle(self.clock, f"Пал(а) {dead.name}. Деревня помнит.")
        if dead in w.residents:
            w.residents.remove(dead)
        if dead.heir_name:
            # РОД ПРОДОЛЖАЕТСЯ: наследник принимает двор — смерть не обнуляет всё
            heir = self._make_heir(dead)
            w.residents.append(heir)
            slot.char = heir; slot.loc = loc; slot.transit = None; slot.dead = False
            w.add_chronicle(self.clock, f"Двор принял {heir.name} — род {dead.name} продолжился.")
            w.add_news(self.clock, f"{heir.name} принял наследство рода {dead.name}.")
            self.store.player_save(slot.aid, heir, loc)
        else:
            slot.dead = True
            self.store.player_save(slot.aid, dead, loc)

    def _make_heir(self, dead):
        sex = names.random_sex(self.rng)
        return Character(
            name=dead.heir_name, role="heir", driver="player", sex=sex,
            age=round(self.rng.uniform(18, 24), 1), father=dead.name, mother=dead.mother,
            coins=round(dead.coins * 0.5, 1), reputation=round(dead.reputation * 0.5, 1),
            home="owned", home_comfort=dead.home_comfort * 0.5,
            weapon=dead.weapon, head=dead.head, body=dead.body,
            outfit=dead.outfit, feet=dead.feet, trinket=dead.trinket,
            inventory=list(dead.inventory), storage=list(dead.storage),
            plot=list(dead.plot), origin=f"наследник рода {dead.name}",
            surname=dead.surname,                       # род продолжается — фамилия та же
            nick=names.identity(sex, "heir", self.rng)[1],
            trait=_traits.assign(self.rng))

    def _arrive(self, slot):
        to = slot.transit["to"]; from_shard = slot.transit["from_shard"]
        ev = slot.transit.get("event")
        self._warm(to)                               # на всякий случай: деревня готова принять
        dest = self._settle(to)
        if self._shard(to) != from_shard:
            # ПЕРЕДАЧА между шардами: сериализуем и воссоздаём персонажа (как через сеть)
            slot.char = Character(**asdict(slot.char))
            dest.add_news(self.clock, f"Караван из другого края привёл {slot.char.name}.")
        else:
            dest.add_news(self.clock, f"С караваном {slot.char.v('прибыл', 'прибыла')} {slot.char.name}.")
        dest.residents.append(slot.char)
        slot.loc = to; slot.transit = None
        self._learn(slot.char, to, 2)             # побывал — деревня открыта на карте
        if not slot.online:                       # прибыл под AI — остаётся afk
            slot.char.driver = "ai"; slot.char.afk = True
        # событие пути
        if ev:
            if ev["kind"] == "ambush":
                if slot.online:
                    slot.pending = {"beast": ev["beast"], "where": "road"}
                    b = C.info(ev["beast"])
                    dest.add_news(self.clock, f"⚔ На дороге была засада — {b['name']}!")
                else:
                    C.resolve(slot.char, ev["beast"], 0.5, self.rng)   # afk отбивается сам
            else:
                dest.add_news(self.clock, RE.apply_passive(ev, slot.char, self.rng))

    def _cross_migrate(self):
        """Переполнение шарда расселяется по ВСЕМУ миру: странники, которым не нашлось
        места в своём шарде, идут в наименее населённую деревню любого шарда. Так
        население течёт между шардами, а не копится у границы."""
        pool = []
        for reg in self.shards:
            if reg.wanderers:
                pool.extend(reg.wanderers); reg.wanderers = []
        if not pool:
            return
        for ch in pool:
            dests = [g for g in range(self._nset()) if self._settle(g).pop() < T.SETTLEMENT_CAPACITY]
            if not dests:
                self.shards[0].wanderers.append(ch); continue   # мест нет — ждёт, не теряется
            g = min(dests, key=lambda g: self._settle(g).pop())
            s = self._settle(g); ch.role = "idler"
            (s.pending_arrivals if s.archived else s.residents).append(ch)
            s.add_news(self.clock, f"Издалека пришёл новый житель — {ch.name}.")

    def _cur(self, slot):
        return None if slot.loc is None else self._settle(slot.loc)

    def _slot(self, aid):
        return self.players.get(aid)

    # ── намерения ──
    def do(self, aid, activity, quality):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None:
                return {"ok": False, "msg": "ты в пути"}
            if not slot.char.alive:
                return {"ok": False, "msg": "ты пал — мир помнит тебя"}
            w = self._cur(slot)
            r = P.do_activity(w, slot.char, activity, float(quality), self.rng)
            if r.get("ok"):
                info = Q.credit_activity(w, slot.char, r["activity"], self.clock)
                if info:
                    slot.char.reputation += 2.0          # добрая слава за выполненную просьбу
                    r["quest_done"] = info; r["coins"] = round(slot.char.coins, 1)
                    if info.get("chain"):
                        # РАЗВИЛКА: отвезти за деньги или раздать нуждающимся за славу
                        others = [g for g in range(self._nset()) if g != slot.loc]
                        dj = self.rng.choice(others) if others else slot.loc
                        self._learn(slot.char, dj, 1)      # услышал о деревне назначения
                        slot.choice = {"giver": info["giver"], "activity": info["activity"],
                                       "dest": dj, "dest_name": self._settle(dj).name}
                        r["choice"] = {"giver": info["giver"], "dest_name": self._settle(dj).name}
            return r

    def choose(self, aid, opt):
        """Закрыть развилку квеста: 'deliver' (поклажа за монеты) или 'charity'
        (раздать нуждающимся — без денег, зато добрая слава)."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or not slot.choice or slot.loc is None:
                return {"ok": False, "msg": "нет развилки"}
            ch_choice = slot.choice; slot.choice = None
            w = self._cur(slot); ch = slot.char
            if opt == "deliver":
                dq = Q.make_delivery(w, ch_choice["giver"], ch_choice["activity"],
                                     ch_choice["dest"], ch_choice["dest_name"], self.rng)
                w.add_news(self.clock, f"{ch_choice['giver']} просит отвезти {dq.parcel} в {dq.dest_name}.")
                return {"ok": True, "msg": f"Взял поручение: отвезти {dq.parcel} в {dq.dest_name}.",
                        "chain_next": dq.as_dict()}
            if opt == "charity":
                ch.reputation += 6.0
                ch.favors[ch_choice["giver"]] = ch.favors.get(ch_choice["giver"], 0) + 1
                w.add_chronicle(self.clock, f"{ch.name} раздал(а) нажитое нуждающимся — молва пошла по краю.")
                return {"ok": True, "msg": "Ты раздал нуждающимся — монет нет, зато слава растёт.",
                        "reputation": round(ch.reputation, 1)}
            return {"ok": False, "msg": "?"}

    def help(self, aid, quality):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            w = self._cur(slot)
            others = [c for c in w.living() if c is not slot.char]
            npc = self.rng.choice(others).name if others else "сосед"
            res = P.help_npc(w, slot.char, npc, float(quality), self.rng)
            if res.get("ok"):
                slot.char.reputation += 1.0
            return res

    def favor(self, aid, npc):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None:
                return {"ok": False, "msg": "ты в пути"}
            return P.call_favor(self._cur(slot), slot.char, npc, self.rng)

    def quest(self, aid, qid):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None:
                return {"ok": False, "msg": "ты в пути"}
            w = self._cur(slot); res = Q.accept(w, int(qid)); q = res.get("quest")
            if res.get("ok") and q and q["kind"] == "deliver":
                slot.char.deliveries.append({"src": slot.loc, "qid": q["qid"], "dest": q["dest"],
                    "dest_name": q["dest_name"], "giver": q["giver"], "parcel": q["parcel"],
                    "title": q["title"], "reward": q["reward"]})
            return res

    def deliver(self, aid, qid):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None:
                return {"ok": False, "msg": "ты в пути"}
            d = next((x for x in slot.char.deliveries if x["qid"] == int(qid)), None)
            if not d:
                return {"ok": False, "msg": "нет такой поклажи"}
            if slot.loc != d["dest"]:
                return {"ok": False, "msg": f"сдать можно в {d['dest_name']}"}
            src = self._settle(d["src"])
            res = Q.complete_delivery(src, self._cur(slot), slot.char, int(qid), self.clock)
            if res.get("ok"):
                slot.char.deliveries.remove(d)
                slot.char.reputation += 3.0          # доставка — заметное дело, слава растёт
            return res

    def hunt(self, aid):
        """Выйти на зверя: нужно оружие (голыми руками зверя не взять). Сервер выбирает
        добычу по навыку охотника и ставит энкаунтер-бой."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            if slot.pending:
                b = C.info(slot.pending["beast"])
                return {"ok": True, "beast": slot.pending["beast"], "name": b["name"], "icon": b["icon"]}
            if not slot.char.weapon:
                return {"ok": False, "msg": "без оружия зверя не возьмёшь — купи/скрафти оружие или поставь силок (🪤)"}
            skill = slot.char.skill("hunting")
            tier = "big" if skill >= 50 and self.rng.random() < 0.3 else ("medium" if skill >= 20 else "small")
            bid = C.pick(self.rng, tier)
            slot.pending = {"beast": bid, "where": "hunt"}
            b = C.info(bid)
            return {"ok": True, "beast": bid, "name": b["name"], "icon": b["icon"],
                    "msg": f"Ты вышел на {b['name']} {b['icon']}"}

    def trap(self, aid):
        """Поставить силок на мелкую дичь — без боя и риска, но скромно. Нужна ловушка
        в инвентаре; иногда силок ломается. Путь для тех, у кого нет оружия."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            ch = slot.char
            if "trap" not in ch.inventory:
                return {"ok": False, "msg": "нужен силок (🪤) — купи в лавке или скрафти"}
            bid = C.pick(self.rng, "small")                 # силок берёт только мелочь
            b = C.info(bid)
            got = max(1, round(b["loot"].get("raw", 2) * self.rng.uniform(0.4, 0.8)))
            ch.cargo["raw"] = ch.cargo.get("raw", 0) + got
            ch.train("hunting", 0.4)
            broke = self.rng.random() < 0.2               # силок иногда рвётся
            if broke:
                ch.inventory.remove("trap")
            msg = f"🪤 В силок попался {b['name']} {b['icon']} — {got} raw." + (" Силок порвался." if broke else "")
            return {"ok": True, "msg": msg, "loot": {"raw": got}}

    def strike(self, aid, quality):
        """Разрешить текущий бой качеством боевой мини-игры."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or not slot.pending:
                return {"ok": False, "msg": "драться не с кем"}
            bid = slot.pending["beast"]; slot.pending = None
            res = C.resolve(slot.char, bid, float(quality), self.rng)
            if res.get("win"):
                slot.char.reputation += 1.0          # победа над зверем прибавляет славы
            return res

    def drink(self, aid):
        """Выпить медовухи в таверне. Атмосфера + совместное питьё при со-присутствии —
        социальное сердце: «пить пиво с другом в таверне»."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            w = self._cur(slot)
            if not getattr(w, "tavern", False):
                return {"ok": False, "msg": "в этой деревне нет корчмы"}
            if w.store.get("mead", 0) < T.DRINK_MEAD:
                return {"ok": False, "msg": "медовуха кончилась — надо подождать, пока сварят"}
            ch = slot.char
            keeper = w.keeper
            # платят монетой — или корчмарь закрывает стол в уплату долга (услуги)
            on_the_house = keeper and ch.favors.get(keeper, 0) > 0
            if on_the_house:
                ch.favors[keeper] -= 1
                paymsg = f"{keeper} закрыл тебе стол в уплату долга"
            else:
                if ch.coins < T.DRINK_PRICE:
                    return {"ok": False, "msg": f"нет монет на кружку (нужно {T.DRINK_PRICE:.0f}⌾)"}
                ch.coins -= T.DRINK_PRICE
                paymsg = f"-{T.DRINK_PRICE:.0f}⌾"
            w.store["mead"] -= T.DRINK_MEAD
            relax = T.DRINK_RELAX * _traits.mod(ch, "tavern", 1.0)   # общительным корчма милее
            ch.fatigue = max(0.0, ch.fatigue - relax)
            P.update_condition(ch, cheered=6.0)                      # медовуха веселит душу
            slot.drank = self.clock.total_hours
            if ch.name not in w.tavern_tonight:
                w.tavern_tonight.append(ch.name)
            toast = self.rng.choice(TAVERN_TOASTS)
            companions = [s.char.name for s in self.players.values()
                          if s is not slot and not s.dead and s.loc == slot.loc
                          and self.clock.total_hours - s.drank <= 2]
            evening = self.clock.hour in T.TAVERN_HOURS
            if companions:
                names_all = companions + [ch.name]
                w.add_chronicle(self.clock, f"За одним столом у очага: {', '.join(names_all)}.")
                ch.fatigue = max(0.0, ch.fatigue - 4.0)            # в доброй компании отдыхается лучше
                msg = f"🍯 {toast} Вы пьёте вместе: {', '.join(companions)}. На душе теплее."
            else:
                mood = " Вечер, народ подтягивается к очагу." if evening else " Днём тихо, но медовуха хороша."
                msg = f"🍯 {toast}{mood}"
            rumor = None
            if self.rng.random() < 0.4:                 # за кружкой доносится слух
                rumor = self.rng.choice(TAVERN_RUMORS)
                Q.post_from_needs(w, self.clock, self.rng)   # слух иногда оборачивается делом
                # иногда прослышишь о неведомой деревне — она проявляется на карте
                unknown = [g for g in range(self._nset()) if ch.known.get(str(g), 0) == 0]
                if unknown:
                    g = self.rng.choice(unknown)
                    self._learn(ch, g, 1)
                    rumor += f" (ты прослышал о деревне {self._settle(g).name})"
            if rumor:
                msg += f"  🗣 {rumor}"
            return {"ok": True, "msg": f"{msg} ({paymsg})", "companions": companions,
                    "rumor": rumor, "coins": round(ch.coins, 1),
                    "mead_left": round(w.store.get("mead", 0))}

    # ── «ОЧКО» (русский блэкджек) в таверне: сервер держит колоду, клиент шлёт лишь ход ──
    CARD_BET = 10                        # ставка за партию (монеты)
    # очки как в привычном блэкджеке: картинки по 10, Туз 11 (падает до 1 при переборе)
    CARD_RANKS = [("6", 6), ("7", 7), ("8", 8), ("9", 9), ("10", 10),
                  ("В", 10), ("Д", 10), ("К", 10), ("Т", 11)]   # Валет/Дама/Король/Туз
    CARD_SUITS = ["♠", "♥", "♦", "♣"]

    def _card_total(self, hand):
        """Сумма очков; Туз (11) опускается до 1, чтобы не перебрать.
        Карта = (label, value, suit)."""
        total = sum(v for _, v, _ in hand)
        aces = sum(1 for _, v, _ in hand if v == 11)
        while total > 21 and aces:
            total -= 10; aces -= 1
        return total

    def _card_view(self, hand):
        return [{"c": label + suit, "v": val} for label, val, suit in hand]

    def _card_state(self, slot, reveal=False):
        g = slot.card
        dealer = g["dealer"] if reveal else g["dealer"][:1]
        return {"ok": True, "bet": g["bet"], "done": g["done"],
                "player": self._card_view(g["player"]),
                "ptotal": self._card_total(g["player"]),
                "dealer": self._card_view(dealer),
                "dtotal": self._card_total(dealer) if reveal else None,
                "hidden": 0 if reveal else max(0, len(g["dealer"]) - 1),
                "msg": g.get("msg", ""), "coins": round(slot.char.coins, 1)}

    def card_start(self, aid):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            w = self._cur(slot)
            if not getattr(w, "tavern", False):
                return {"ok": False, "msg": "за карты садятся в корчме"}
            if slot.card and not slot.card["done"]:
                return self._card_state(slot)
            ch = slot.char
            if ch.coins < self.CARD_BET:
                return {"ok": False, "msg": f"нужно {self.CARD_BET}⌾ на кон, у тебя {ch.coins:.0f}"}
            ch.coins -= self.CARD_BET
            deck = [(label, val, s) for (label, val) in self.CARD_RANKS for s in self.CARD_SUITS]
            self.rng.shuffle(deck)
            slot.card = {"deck": deck, "player": [deck.pop(), deck.pop()],
                         "dealer": [deck.pop(), deck.pop()], "bet": self.CARD_BET,
                         "done": False, "msg": "Твой ход: ещё карту или хватит?"}
            # «очко» сразу (21 на двух) — проверим разом
            if self._card_total(slot.card["player"]) == 21:
                return self._card_finish(slot, blackjack=True)
            return self._card_state(slot)

    def card_hit(self, aid):
        with self.lock:
            slot = self._slot(aid)
            if not slot or not slot.card or slot.card["done"]:
                return {"ok": False, "msg": "партия не идёт — начни заново"}
            slot.card["player"].append(slot.card["deck"].pop())
            if self._card_total(slot.card["player"]) > 21:
                return self._card_finish(slot, bust=True)
            slot.card["msg"] = "Ещё карту или хватит?"
            return self._card_state(slot)

    def card_stand(self, aid):
        with self.lock:
            slot = self._slot(aid)
            if not slot or not slot.card or slot.card["done"]:
                return {"ok": False, "msg": "партия не идёт — начни заново"}
            return self._card_finish(slot)

    def _card_finish(self, slot, bust=False, blackjack=False):
        g = slot.card; ch = slot.char; g["done"] = True
        pt = self._card_total(g["player"])
        if bust:
            g["msg"] = f"🃏 Перебор ({pt}). Ставка {g['bet']}⌾ ушла заведению."
            return self._card_state(slot, reveal=True)
        if blackjack:
            win = int(g["bet"] * 2.5)
            ch.coins += win
            g["msg"] = f"🂡 Очко! Чистая победа — забираешь {win}⌾."
            return self._card_state(slot, reveal=True)
        # корчмарь-банкир добирает до 17
        while self._card_total(g["dealer"]) < 17 and g["deck"]:
            g["dealer"].append(g["deck"].pop())
        dt = self._card_total(g["dealer"])
        if dt > 21 or pt > dt:
            ch.coins += g["bet"] * 2
            g["msg"] = f"🏆 Ты {pt}, банкир {dt if dt<=21 else 'перебор'}. Выигрыш {g['bet']*2}⌾!"
        elif pt == dt:
            ch.coins += g["bet"]
            g["msg"] = f"🤝 Ничья ({pt}). Ставка {g['bet']}⌾ возвращается."
        else:
            g["msg"] = f"🪙 Ты {pt}, банкир {dt}. Ставка ушла заведению."
        return self._card_state(slot, reveal=True)

    # ── «ДУРАК» (подкидной) против корчмаря в таверне ──
    DURAK_BET = 10

    def _durak_payout(self, slot):
        g = slot.durak
        if not g or not g.view()["over"]:
            return
        ch = slot.char; bet = slot.durak_bet
        loser = g.view()["loser"]
        if loser == DURAK_BOT:
            ch.coins += bet * 2
            g.msg += f" Выигрыш {bet*2}⌾!"
        elif loser is None:
            ch.coins += bet
            g.msg += f" Ставка {bet}⌾ возвращается."
        else:
            g.msg += f" Ставка {bet}⌾ ушла корчмарю."

    def _durak_view(self, slot):
        v = slot.durak.view()
        v["bet"] = slot.durak_bet
        v["coins"] = round(slot.char.coins, 1)
        return v

    def durak_start(self, aid):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            w = self._cur(slot)
            if not getattr(w, "tavern", False):
                return {"ok": False, "msg": "за карты садятся в корчме"}
            if slot.durak and not slot.durak.view()["over"]:
                return self._durak_view(slot)
            if slot.char.coins < self.DURAK_BET:
                return {"ok": False, "msg": f"нужно {self.DURAK_BET}⌾ на кон"}
            slot.char.coins -= self.DURAK_BET
            slot.durak_bet = self.DURAK_BET
            slot.durak = Durak(self.rng)
            return self._durak_view(slot)

    def durak_act(self, aid, action, idx=-1):
        with self.lock:
            slot = self._slot(aid)
            if not slot or not slot.durak:
                return {"ok": False, "msg": "партия не идёт — начни заново"}
            was_over = slot.durak.view()["over"]
            slot.durak.act(action, int(idx))
            if slot.durak.view()["over"] and not was_over:
                self._durak_payout(slot)
            return self._durak_view(slot)

    def say(self, aid, text):
        """Сказать слово в деревне/баре. Чат эфемерный, привязан к месту (loc):
        слышат только те игроки, кто физически здесь. Сервер чистит и ограничивает ввод."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            t = (text or "").replace("\n", " ").replace("\r", " ").strip()
            if not t:
                return {"ok": False, "msg": "пусто"}
            if len(t) > 160:
                t = t[:160]
            ring = self._chat.setdefault(slot.loc, [])
            ring.append({"who": slot.char.name, "text": t, "day": self.clock.day,
                         "hour": self.clock.hour})
            # A-LIFE: мир слышит — завсегдатай корчмы или корчмарь иногда откликается
            w = self._cur(slot)
            reply = None
            if getattr(w, "tavern", False):
                present = [c for c in w.living()
                           if c.name in w.tavern_tonight and c.name != slot.char.name]
                roll = self.rng.random()
                if w.keeper and roll < 0.18:
                    reply = {"who": w.keeper, "text": self.rng.choice(NPC_KEEPER_REPLIES)}
                elif present and roll < 0.55:
                    who = self.rng.choice(present)
                    reply = {"who": who.name, "text": self.rng.choice(NPC_CHAT_REPLIES)}
            if reply:
                reply.update({"day": self.clock.day, "hour": self.clock.hour, "npc": True})
                ring.append(reply)
            if len(ring) > 50:
                del ring[:-50]
            return {"ok": True, "msg": "сказано"}

    def observe_snapshot(self):
        """Read-only срез мира для окна-наблюдателя тестировщика: сущности и дома.
        Ничего не меняет. Горячие деревни — поимённо, холодные — числом."""
        with self.lock:
            players_by_loc = {}
            for s in self.players.values():
                if not s.dead and s.loc is not None:
                    players_by_loc.setdefault(s.loc, {})[s.char.name] = s.char.driver
            settlements = []
            entities = []
            for g in range(self._nset()):
                w = self._settle(g)
                online = players_by_loc.get(g, {})
                settlements.append({
                    "idx": g, "name": w.name, "archived": bool(w.archived),
                    "pop": w.pop(), "health": w.health(),
                    "food_days": round(w.food_days(), 1),
                    "season": getattr(w, "season", "весна"),
                    "weather": getattr(w, "weather", "ясно"),
                    "cold": bool(getattr(w, "cold", False)),
                    "keeper": w.keeper, "graves": w.graves,
                    "house_integrity": round(w.house_integrity),
                    "store": {k: round(v, 1) for k, v in w.store.items()},
                    "online": len(online),
                })
                if w.archived:
                    continue
                for c in w.living():
                    drv = online.get(c.name, c.driver)
                    top = sorted(c.skills.items(), key=lambda kv: kv[1], reverse=True)[:2]
                    entities.append({
                        "name": c.full_name(), "role": c.role, "where": w.name, "idx": g,
                        "driver": drv, "sex": c.sex, "age": round(c.age, 1),
                        "hunger": round(c.hunger), "fatigue": round(c.fatigue),
                        "morale": round(getattr(c, "morale", 60.0)),
                        "health": round(getattr(c, "health", 100.0)),
                        "trait": _traits.label(getattr(c, "trait", "")),
                        "social": round(c.social), "ambition": round(c.ambition),
                        "dwelling": round(c.dwelling),
                        "home": c.home, "comfort": round(c.home_comfort),
                        "coins": round(c.coins, 1), "rep": round(c.reputation, 1),
                        "weapon": I.label(c.weapon) if c.weapon else "",
                        "warmth": I.warmth(c), "worn": len(I.worn(c)),
                        "last_act": c.last_act,
                        "skills": ", ".join(f"{k} {v:.0f}" for k, v in top),
                        "plot": len(c.plot), "inv": len(c.inventory),
                    })
            return {"day": self.clock.day, "hour": self.clock.hour,
                    "shards": len(self.shards), "settlements_n": self._nset(),
                    "entities": entities, "settlements": settlements}

    def dice(self, aid, quality):
        """Застольная игра в кости: ставка монетами, исход — по ловкости (мини-игра)."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            w = self._cur(slot)
            if not getattr(w, "tavern", False):
                return {"ok": False, "msg": "играть негде — нет корчмы"}
            bet = 5.0; ch = slot.char
            if ch.coins < bet:
                return {"ok": False, "msg": f"нет монет на ставку ({bet:.0f}⌾)"}
            win = float(quality) > 0.5
            if win:
                ch.coins += bet
                msg = f"🎲 Кости легли удачно — забираешь банк (+{bet:.0f}⌾)!"
            else:
                ch.coins -= bet
                msg = f"🎲 Не повезло — ставка ушла в банк (−{bet:.0f}⌾)."
            return {"ok": True, "msg": msg, "coins": round(ch.coins, 1)}

    def fix_tavern(self, aid, quality):
        """Подлатать корчму. Корчмарь остаётся должен — потом закроет тебе стол."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            w = self._cur(slot)
            if not getattr(w, "tavern", False) or not w.keeper:
                return {"ok": False, "msg": "чинить нечего — тут нет корчмы"}
            r = P.do_activity(w, slot.char, "build", float(quality), self.rng)
            slot.char.favors[w.keeper] = slot.char.favors.get(w.keeper, 0) + 1
            slot.char.reputation += 2.0
            r["msg"] = f"Ты подлатал корчму — {w.keeper} теперь должен тебе (стол за счёт заведения)."
            r["favor_with"] = w.keeper
            return r

    def craft(self, aid, item_id):
        with self.lock:
            slot = self._slot(aid)
            if not slot:
                return {"ok": False, "msg": "?"}
            return I.craft(slot.char, item_id)

    def build(self, aid, sid):
        with self.lock:
            slot = self._slot(aid)
            if not slot:
                return {"ok": False, "msg": "?"}
            return P.build_structure(slot.char, sid)

    def start_family(self, aid):
        """Завести наследника — род продолжится после смерти. Нужен свой дом."""
        with self.lock:
            slot = self._slot(aid)
            if not slot:
                return {"ok": False, "msg": "?"}
            ch = slot.char
            if ch.home != "owned":
                return {"ok": False, "msg": "семью заводят под своей крышей — сначала свой дом"}
            if ch.heir_name:
                return {"ok": False, "msg": f"наследник уже есть — {ch.heir_name}"}
            sex = names.random_sex(self.rng)
            ch.heir_name = names.random_name(sex, self.rng)
            w = self._cur(slot)
            if w:
                w.add_news(self.clock, f"У {ch.name} в доме прибавление — {ch.heir_name}.")
            return {"ok": True, "msg": f"В доме родился наследник — {ch.heir_name}. Род не прервётся."}

    def _caravans_here(self, g):
        """Караваны, стоящие сейчас в этой деревне (для подсказки в клиенте)."""
        if g is None:
            return []
        sid, li = self._map[g]
        out = []
        for cav in getattr(self.shards[sid], "caravans", []):
            if cav.at == li:
                goods = ", ".join(f"{I.label(k)}×{v}" for k, v in list(cav.cargo.items())[:4])
                out.append({"name": cav.name, "goods": goods or "распродан"})
        return out

    def sleep(self, aid):
        """Отправить персонажа в кровать (ночью). Дома — крепкий сон, без крова — урывками."""
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None or not slot.char.alive:
                return {"ok": False, "msg": "нельзя сейчас"}
            if self.clock.hour not in T.SLEEP_HOURS:
                return {"ok": False, "msg": "днём не спится — ложись ночью"}
            ch = slot.char
            if ch.home != "none":
                ch.fatigue = max(0.0, ch.fatigue - 40.0)
                msg = "Ты лёг в свою кровать — крепкий сон снял усталость."
            else:
                ch.fatigue = max(0.0, ch.fatigue - 18.0)
                msg = "Спал урывками без крыши — отдохнул так себе."
            return {"ok": True, "msg": msg, "fatigue": round(ch.fatigue)}

    def priority(self, aid, mode):
        """Задать приоритет персонажа в AI-режиме (работа/баланс/отдых/полный отдых)."""
        with self.lock:
            slot = self._slot(aid)
            if not slot:
                return {"ok": False, "msg": "?"}
            if mode not in ("work", "balanced", "rest", "full_rest"):
                return {"ok": False, "msg": "неизвестный приоритет"}
            slot.char.priority = mode
            titles = {"work": "больше работать", "balanced": "баланс",
                      "rest": "больше отдыхать", "full_rest": "только отдых"}
            return {"ok": True, "msg": f"Приоритет в отлучке: {titles[mode]}."}

    def house(self, aid, action):
        with self.lock:
            slot = self._slot(aid)
            if not slot:
                return {"ok": False, "msg": "?"}
            return {"rent": P.rent_room, "buy": P.buy_house, "furnish": P.furnish}\
                .get(action, lambda c: {"ok": False, "msg": "?"})(slot.char)

    def item(self, aid, action, iid="", slotname=""):
        with self.lock:
            s = self._slot(aid)
            if not s:
                return {"ok": False, "msg": "?"}
            ch = s.char
            if action == "buy":
                if s.loc is None:
                    return {"ok": False, "msg": "лавка — только в деревне"}
                return ECON.buy_ware(ch, self._cur(s), iid)   # покупка из конечного запаса лавки
            if action == "sell":    return I.sell(ch, iid)
            if action == "store":   return I.to_storage(ch, iid)
            if action == "unstore": return I.from_storage(ch, iid)
            if action == "equip":   return I.equip(ch, iid)
            if action == "unequip": return I.unequip(ch, slotname)
            if action == "use":     return I.use(ch, iid)
            return {"ok": False, "msg": "?"}

    def trade(self, aid, action, res, amount):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.loc is None:
                return {"ok": False, "msg": "ты в пути"}
            w = self._cur(slot)
            if action == "buy":  return P.buy_goods(w, slot.char, res, float(amount))
            if action == "sell": return P.sell_goods(w, slot.char, res, float(amount))
            return {"ok": False, "msg": "?"}

    def travel(self, aid, to):
        with self.lock:
            slot = self._slot(aid)
            if not slot or slot.transit:
                return {"ok": False, "msg": "уже в пути"}
            to = int(to)
            if slot.loc is None or to < 0 or to >= self._nset() or to == slot.loc:
                return {"ok": False, "msg": "некуда"}
            if slot.char.known.get(str(to), 0) < 1:
                return {"ok": False, "msg": "ты не знаешь дороги туда — сперва прослышь об этом месте"}
            hours = self._dist(slot.loc, to)
            src = self._settle(slot.loc)
            if slot.char in src.residents:
                src.residents.remove(slot.char)
            cross = self._shard(to) != self._shard(slot.loc)
            slot.transit = {"to": to, "from": slot.loc, "from_shard": self._shard(slot.loc),
                            "arrive": self.clock.total_hours + hours,
                            "event": RE.roll(self.rng, hours, cross)}
            slot.loc = None
            tail = " (другой край — караван дальний)" if cross else ""
            return {"ok": True, "msg": f"Вышел с караваном в {self._settle(to).name} (~{hours} ч){tail}."}

    def save(self, aid=None):
        with self.lock:
            self._persist(); return {"ok": True, "msg": "Сохранено."}

    def _persist(self):
        self.store.world_save({"shards": [PS.region_to_dict(r) for r in self.shards]})
        for slot in self.players.values():
            loc = slot.loc if slot.loc is not None else (slot.transit["from"] if slot.transit else 0)
            self.store.player_save(slot.aid, slot.char, loc)

    # ── per-player вид ──
    def _activity_difficulty(self, w, ch, activity):
        a = P.ACTIVITIES.get(activity, {})
        skill = ch.skill(a.get("skill", "")) if a else 0.0
        need = 0
        for q in Q.open_quests(w):
            if q.kind == "activity" and q.accepted and q.activity == activity:
                need = max(need, q.need)
        return round(max(0.1, min(0.9, 0.15 + skill / 120.0 + max(0, need - 2) * 0.06)), 2)

    def state(self, aid):
        with self.lock:
            slot = self._slot(aid)
            if not slot:
                return {"error": "no_player"}
            ch = slot.char
            reg = []
            for g in range(self._nset()):
                s = self._settle(g)
                lvl = 2 if g == slot.loc else int(ch.known.get(str(g), 0))
                row = {"idx": g, "shard": self._shard(g), "know": lvl,
                       "current": g == slot.loc,
                       "hours": None if (slot.loc is None or lvl < 1) else self._dist(slot.loc, g)}
                if lvl >= 1:
                    row["name"] = s.name; row["market"] = s.market
                if lvl >= 2:
                    row["pop"] = s.pop(); row["health"] = s.health()
                reg.append(row)
            me = {"name": ch.name, "full_name": ch.full_name(),
                  "surname": ch.surname, "nick": ch.nick,
                  "sex": ch.sex, "age": round(ch.age, 1),
                  "father": ch.father, "mother": ch.mother, "origin": ch.origin,
                  "priority": ch.priority, "journal": list(ch.journal[-8:])[::-1],
                  "reputation": round(ch.reputation, 1), "heir": ch.heir_name,
                  "plot": [{"id": s, "name": P.PLOT[s]["name"], "icon": P.PLOT[s]["icon"]}
                           for s in ch.plot if s in P.PLOT],
                  "coins": round(ch.coins, 1),
                  "home": ch.home, "comfort": round(ch.home_comfort),
                  "fatigue": round(ch.fatigue), "hunger": round(ch.hunger),
                  "morale": round(getattr(ch, "morale", 60.0)),
                  "health": round(getattr(ch, "health", 100.0)),
                  "trait": getattr(ch, "trait", ""),
                  "trait_label": _traits.label(getattr(ch, "trait", "")),
                  "trait_desc": (_traits.info(getattr(ch, "trait", "")) or {}).get("desc", ""),
                  "social": round(ch.social), "ambition": round(ch.ambition),
                  "dwelling": round(ch.dwelling),
                  "alive": ch.alive, "dead": slot.dead,
                  "skills": {k: round(v, 1) for k, v in ch.skills.items()},
                  "favors": dict(ch.favors),
                  "inventory": [{"id": x, "label": I.label(x),
                                 "slot": (I.CATALOG.get(x) or {}).get("slot", "")} for x in ch.inventory],
                  "storage": [{"id": x, "label": I.label(x)} for x in ch.storage],
                  "cargo": {k: round(v) for k, v in ch.cargo.items() if v > 0},
                  "weapon": {"id": ch.weapon, "label": I.label(ch.weapon)} if ch.weapon else None,
                  "outfit": {"id": ch.outfit, "label": I.label(ch.outfit)} if ch.outfit else None,
                  "worn": I.worn(ch),
                  "warmth": I.warmth(ch), "weapon_dmg": I.weapon_dmg(ch),
                  "deliveries": [dict(d, can=(d["dest"] == slot.loc)) for d in ch.deliveries]}
            from world.region import HOT_THRESHOLD
            hot_n = sum(1 for s in (self._settle(g) for g in range(self._nset()))
                        if s.temperature >= HOT_THRESHOLD)
            bodies = sum(len(self._settle(g).residents) for g in range(self._nset()))
            base = {"region": {"name": "Приграничье", "count": self._nset(),
                               "shards": len(self.shards), "hot": hot_n, "bodies": bodies},
                    "day": self.clock.day, "hour": self.clock.hour,
                    "me": me, "settlements": reg, "shop": ECON.wares_list(self._cur(slot)),
                    "recipes": [dict(r, can=I.can_craft(ch, r["id"])) for r in I.recipes_list()],
                    "plots": P.plot_list()}
            if slot.pending:
                b = C.info(slot.pending["beast"])
                base["encounter"] = {"beast": slot.pending["beast"], "name": b["name"],
                                     "icon": b["icon"], "where": slot.pending["where"],
                                     "power": b["power"]}
            if slot.choice:
                base["choice"] = {"giver": slot.choice["giver"],
                                  "dest_name": slot.choice["dest_name"]}
            if slot.dead:
                base["dead"] = True; return base
            if slot.transit:
                dest = self._settle(slot.transit["to"])
                base["transit"] = {"to": dest.name, "left": max(0, slot.transit["arrive"] - self.clock.total_hours)}
                return base
            w = self._cur(slot)
            here_players = [s.char.name for s in self.players.values()
                            if s is not slot and not s.dead and s.loc == slot.loc]
            base.update({
                "here": {"name": w.name, "market": w.market, "health": w.health(),
                         "food_days": round(w.food_days(), 1),
                         "store": {k: round(v) for k, v in w.store.items()},
                         "alive": len(w.living()), "cold": bool(getattr(w, "cold", False)),
                         "season": getattr(w, "season", "весна"),
                         "weather": getattr(w, "weather", "ясно"),
                         "tavern": list(w.tavern_tonight), "players_here": here_players,
                         "roster": [{"name": c.name, "full": c.full_name(), "role": c.role}
                                    for c in w.living()[:24] if c is not ch],
                         "keeper": w.keeper,
                         "free_tab": bool(w.keeper and ch.favors.get(w.keeper, 0) > 0),
                         "drink_price": T.DRINK_PRICE,
                         "news": list(w.news[-6:])[::-1],
                         "chat": list(self._chat.get(slot.loc, []))[-16:],
                         "caravans": self._caravans_here(slot.loc),
                         "chronicle": list(w.chronicle[-6:])[::-1], "graves": w.graves},
                "activities": {k: {"label": v["label"], "minigame": v["minigame"], "skill": v["skill"],
                                   "difficulty": self._activity_difficulty(w, ch, k)}
                               for k, v in P.ACTIVITIES.items()},
                "quests": [q.as_dict() for q in Q.open_quests(w)
                           if not (q.kind == "deliver" and q.accepted)],
                "trade": {r: {"buy": P.buy_price(w, r), "sell": P.sell_price(w, r),
                              "store": round(w.store.get(r, 0))} for r in P.TRADE_BASE},
                "house": {"rent": P.RENT_PER_DAY, "price": P.HOUSE_PRICE, "furnish": P.FURNISH_STEP_COST}})
            return base
