"""
Квесты живого мира. Не скрипт — отражение настоящих нужд поселения.

Житель (квестодатель) просит то, чего деревне сейчас не хватает: нет дров —
кто-то попросит дров; голодно — попросит рыбы/еды. Выполнил — плата монетами,
и квестодатель остаётся ТЕБЕ должен услугу (бартер встроен в мир, favors), а мир
записывает помощь в хронику.

ЦЕПОЧКИ. Часть просьб — лишь первое звено. Помог Горазду с дровами — он просит
отвезти готовую поклажу в соседнюю деревню (караван-доставка). Доставил — плата
больше, и в той деревне появляется новый знакомый, который теперь тебе должен.
Так локальная работа втягивает игрока в путешествие и расширяет круг связей.

Два вида квестов:
  * activity — выполнить активность N раз (прогресс капает сам из мини-игр);
  * deliver  — отвезти поклажу в деревню назначения (сдать по прибытии).
"""
from __future__ import annotations
from dataclasses import dataclass, field
import random

NEED_ACTIVITY = {"wood": "firewood", "food": "garden", "repair": "build"}
_TITLES = {
    "firewood": "нужны дрова на зиму",
    "garden":   "помоги с огородом — закрома пусты",
    "build":    "подлатать избы, совсем обветшали",
    "fishing":  "рыбы бы к столу",
    "hunting":  "дичи бы добыть",
}
# что именно повезём на втором звене цепочки (поклажа зависит от первой работы)
_PARCEL = {
    "firewood": "воз дров",
    "garden":   "мешки зерна",
    "build":    "тёсаные брёвна",
    "fishing":  "бочонок рыбы",
    "hunting":  "связку дичи",
}
MAX_OPEN = 3
REWARD_PER_UNIT = 1.2
QUEST_NEED_THRESHOLD = 45.0
CHAIN_CHANCE = 0.5          # с какой вероятностью просьба — начало цепочки
DELIVERY_REWARD = 14.0      # плата за доставку (звено 2) — путь стоит дороже


@dataclass
class Quest:
    qid: int
    giver: str
    activity: str                 # для deliver не используется (ставим "deliver")
    title: str
    need: int
    have: int = 0
    accepted: bool = False
    done: bool = False
    kind: str = "activity"        # 'activity' | 'deliver'
    dest: int = -1                # индекс деревни назначения (deliver)
    dest_name: str = ""           # имя деревни назначения (deliver)
    parcel: str = ""              # что везём (deliver)
    reward_coins: float = 0.0     # фиксированная награда (deliver); 0 => считаем от need
    chain: bool = False           # активити-квест, за которым последует доставка

    def reward(self) -> float:
        return self.reward_coins if self.reward_coins else round(self.need * REWARD_PER_UNIT, 1)

    def as_dict(self) -> dict:
        return {"qid": self.qid, "giver": self.giver, "activity": self.activity,
                "title": self.title, "need": self.need, "have": self.have,
                "accepted": self.accepted, "done": self.done, "kind": self.kind,
                "dest": self.dest, "dest_name": self.dest_name, "parcel": self.parcel,
                "chain": self.chain, "reward": self.reward()}


def _next_id(w) -> int:
    w._quest_seq = getattr(w, "_quest_seq", 0) + 1
    return w._quest_seq


def open_quests(w) -> list:
    return [q for q in getattr(w, "quests", []) if not q.done]


def post_from_needs(w, clock, rng: random.Random) -> None:
    """Раз в день житель может повесить квест на самую острую нужду деревни."""
    if not hasattr(w, "quests"):
        w.quests = []
    live = w.living()
    if not live or len(open_quests(w)) >= MAX_OPEN:
        return
    needs = w.needs()
    active_acts = {q.activity for q in open_quests(w)}
    candidates = []
    for need_key, act in NEED_ACTIVITY.items():
        if act in active_acts:
            continue
        sev = needs.get(need_key, 0.0)
        if sev >= QUEST_NEED_THRESHOLD:
            candidates.append((sev, need_key, act))
    if not candidates:
        return
    candidates.sort(reverse=True)
    sev, need_key, act = candidates[0]
    giver = rng.choice([c for c in live if c.name != "Ты"] or live).name
    need = 2 + int(sev // 30)
    q = Quest(_next_id(w), giver, act, _TITLES.get(act, "нужна помощь"), need,
              chain=(rng.random() < CHAIN_CHANCE))
    w.quests.append(q)
    tail = " (и будет ещё просьба)" if q.chain else ""
    w.add_news(clock, f"{giver} ищет помощи: {q.title} (награда {q.reward():.0f}⌾){tail}.")


def make_delivery(w, giver: str, activity: str, dest: int, dest_name: str,
                  rng: random.Random) -> "Quest":
    """Создать второе звено цепочки — доставку поклажи в другую деревню."""
    parcel = _PARCEL.get(activity, "поклажу")
    q = Quest(_next_id(w), giver, "deliver",
              f"отвези {parcel} в {dest_name}", need=1, kind="deliver",
              dest=dest, dest_name=dest_name, parcel=parcel,
              reward_coins=DELIVERY_REWARD)
    w.quests.append(q)
    return q


def accept(w, qid: int) -> dict:
    for q in open_quests(w):
        if q.qid == qid:
            q.accepted = True
            verb = "Везёшь" if q.kind == "deliver" else "Взялся за"
            return {"ok": True, "msg": f"{verb}: «{q.title}» для {q.giver}.",
                    "quest": q.as_dict()}
    return {"ok": False, "msg": "квеста уже нет"}


def credit_activity(w, ch, activity: str, clock) -> dict | None:
    """После активности игрока: засчитать прогресс активным activity-квестам.
    На завершении — награда, долг-услуга, хроника. Если это звено цепочки,
    сообщаем вызывающему (он создаст доставку, зная соседние деревни)."""
    done_info = None
    for q in open_quests(w):
        if q.kind != "activity" or not q.accepted or q.activity != activity:
            continue
        if q.have < q.need:
            q.have += 1
            if q.have >= q.need:
                q.done = True
                ch.coins += q.reward()
                ch.favors[q.giver] = ch.favors.get(q.giver, 0) + 1
                w.add_chronicle(clock, f"{ch.name} {ch.v('выручил', 'выручила')} "
                                       f"{q.giver}: {q.title}.")
                done_info = {"quest_done": q.title, "giver": q.giver,
                             "reward": q.reward(), "chain": q.chain,
                             "activity": q.activity}
    return done_info


def complete_delivery(src, dest, ch, qid: int, clock) -> dict:
    """Сдать поклажу в деревне назначения. src — деревня-заказчик (где висит квест),
    dest — где игрок сейчас. Плата + долг заказчика + новый знакомый в dest."""
    q = next((x for x in open_quests(src) if x.qid == qid and x.kind == "deliver"), None)
    if q is None:
        return {"ok": False, "msg": "этой доставки уже нет"}
    q.done = True
    q.have = q.need
    ch.coins += q.reward()
    ch.favors[q.giver] = ch.favors.get(q.giver, 0) + 1
    # новый знакомый в деревне назначения — круг связей ширится
    contact = None
    locals_ = [c for c in dest.living() if c is not ch]
    if locals_:
        import random as _r
        contact = _r.Random(qid + clock.total_hours).choice(locals_).name
        ch.favors[contact] = ch.favors.get(contact, 0) + 1
    dest.add_chronicle(clock, f"{ch.name} {ch.v('доставил', 'доставила')} "
                              f"{q.parcel} от {q.giver} из {src.name}.")
    src.add_news(clock, f"{q.giver}: поклажа дошла до {dest.name}, спасибо {ch.name}.")
    return {"ok": True, "msg": f"Поклажа сдана в {dest.name}: +{q.reward():.0f}⌾, "
                               f"{q.giver} должен услугу"
                               + (f", а {contact} теперь знакомый" if contact else "") + ".",
            "coins": round(ch.coins, 1), "contact": contact}
