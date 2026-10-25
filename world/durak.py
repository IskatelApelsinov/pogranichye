"""Подкидной дурак 1 на 1 (игрок против корчмаря-бота). Чистый движок без сетевых
зависимостей: сервер держит состояние, клиент шлёт только ход. Бот ходит
автоматически после хода игрока, пока очередь снова не дойдёт до игрока или партия
не кончится. Колода 36 карт (6..Т), козырь — масть нижней карты.

Карта: {"r": ранг 6..14, "s": масть, "label": "строка"}.
"""
from __future__ import annotations

RANKS = [(6, "6"), (7, "7"), (8, "8"), (9, "9"), (10, "10"),
         (11, "В"), (12, "Д"), (13, "К"), (14, "Т")]
SUITS = ["♠", "♥", "♦", "♣"]
HAND = 6
YOU, BOT = "you", "bot"


def _label(r, s):
    rl = dict(RANKS)[r]
    return rl + s


class Durak:
    def __init__(self, rng):
        self.rng = rng
        deck = [{"r": r, "s": s, "label": _label(r, s)} for (r, _) in RANKS for s in SUITS]
        rng.shuffle(deck)
        self.deck = deck
        self.trump = deck[-1]["s"]                 # козырь — масть нижней карты
        self.hands = {YOU: [], BOT: []}
        for _ in range(HAND):
            self.hands[YOU].append(deck.pop(0))
            self.hands[BOT].append(deck.pop(0))
        # кто начинает: у кого младший козырь
        self.attacker = self._first_attacker()
        self.table = []                            # [{"a":card,"d":card|None}]
        self.phase = "attack"                      # attack | defend | over
        self.loser = None
        self.msg = "Твой ход." if self.attacker == YOU else "Корчмарь заходит."
        self._sort_hands()
        if self.attacker == BOT:
            self._bot_turn()

    # ── утилиты ──
    def _sort_hands(self):
        for h in self.hands.values():
            h.sort(key=lambda c: (c["s"] == self.trump, c["r"]))

    def _first_attacker(self):
        best = None; who = YOU
        for w in (YOU, BOT):
            for c in self.hands[w]:
                if c["s"] == self.trump and (best is None or c["r"] < best):
                    best = c["r"]; who = w
        return who

    def beats(self, d, a):
        if d["s"] == a["s"]:
            return d["r"] > a["r"]
        return d["s"] == self.trump and a["s"] != self.trump

    def _defender(self):
        return BOT if self.attacker == YOU else YOU

    def _table_ranks(self):
        rs = set()
        for pair in self.table:
            rs.add(pair["a"]["r"])
            if pair["d"]:
                rs.add(pair["d"]["r"])
        return rs

    def _undef(self):
        for pair in self.table:
            if pair["d"] is None:
                return pair
        return None

    def _draw_to_six(self):
        order = [self.attacker, self._defender()]
        for w in order:
            while len(self.hands[w]) < HAND and self.deck:
                self.hands[w].append(self.deck.pop(0))
        self._sort_hands()

    def _check_over(self):
        if self.deck:
            return
        you_empty = not self.hands[YOU]
        bot_empty = not self.hands[BOT]
        if you_empty or bot_empty:
            self.phase = "over"
            if you_empty and bot_empty:
                self.loser = None; self.msg = "Ничья — оба вышли."
            elif you_empty:
                self.loser = BOT; self.msg = "🏆 Ты вышел первым — корчмарь в дураках!"
            else:
                self.loser = YOU; self.msg = "🃏 Ты остался с картами — ты дурак."

    def _end_bout(self, defended):
        """Закрыть бой: отбито — карты в отбой и роли меняются; взято — защитник забрал."""
        if defended:
            self.table = []
            self.attacker = self._defender()        # кто отбился — тот заходит
        else:
            # защитник берёт все карты со стола
            d = self._defender()
            for pair in self.table:
                self.hands[d].append(pair["a"])
                if pair["d"]:
                    self.hands[d].append(pair["d"])
            self.table = []
            # атакующий остаётся атакующим (защитник пропускает заход)
        self._draw_to_six()
        self.phase = "attack"
        self._check_over()

    # ── ходы игрока (YOU) ──
    def act(self, action, idx=-1):
        if self.phase == "over":
            return self.view()
        if self.attacker == YOU:
            self._you_attack_side(action, idx)
        else:
            self._you_defend_side(action, idx)
        # после хода игрока — отработать бота, пока очередь не вернётся к игроку
        self._bot_turn()
        return self.view()

    def _you_attack_side(self, action, idx):
        if action == "done":                        # «бито» — завершить бой (если нет незащищённых)
            if self._undef() is None and self.table:
                self._end_bout(defended=True)
                self.msg = "Бито. " + ("Твой заход." if self.attacker == YOU else "Корчмарь заходит.")
            return
        if action == "attack":
            card = self._take_from_hand(YOU, idx)
            if not card:
                return
            if self.table and card["r"] not in self._table_ranks():
                self.hands[YOU].append(card); self._sort_hands()      # нельзя — вернуть
                self.msg = "Подкинуть можно только карту ранга, что уже на столе."
                return
            if len([p for p in self.table if p["d"] is None]) >= 1 and self.table:
                # уже есть незащищённая — ждём ответа защитника (бот ответит)
                pass
            self.table.append({"a": card, "d": None})
            self.phase = "defend"

    def _you_defend_side(self, action, idx):
        if action == "take":
            self._end_bout(defended=False)
            self.msg = "Ты взял карты. Корчмарь заходит снова."
            return
        if action == "defend":
            pair = self._undef()
            if not pair:
                return
            card = self._take_from_hand(YOU, idx)
            if not card:
                return
            if not self.beats(card, pair["a"]):
                self.hands[YOU].append(card); self._sort_hands()
                self.msg = "Этой картой не побить."
                return
            pair["d"] = card
            self.phase = "attack"                    # ход возвращается атакующему (боту) — подкинуть/бито

    def _take_from_hand(self, who, idx):
        h = self.hands[who]
        if idx < 0 or idx >= len(h):
            return None
        return h.pop(idx)

    # ── бот (корчмарь) ──
    def _bot_turn(self):
        guard = 0
        while self.phase != "over" and guard < 40:
            guard += 1
            if self.attacker == BOT and self.phase in ("attack",):
                if not self._bot_attack():
                    break
            elif self._defender() == BOT and self.phase == "defend":
                if not self._bot_defend():
                    break
            else:
                break

    def _bot_attack(self):
        hand = self.hands[BOT]
        if not self.table:
            # первый заход — младшей не-козырной, иначе младшей
            card = self._lowest(hand, prefer_nontrump=True)
            if not card:
                self.phase = "over"; self._check_over(); return False
            hand.remove(card)
            self.table.append({"a": card, "d": None})
            self.phase = "defend"
            self.msg = "Корчмарь зашёл: " + card["label"]
            return True
        # подкинуть карту ранга со стола (не больше 6 и не больше руки защитника)
        ranks = self._table_ranks()
        def_hand = len(self.hands[YOU])
        if len(self.table) < HAND and len(self.table) < def_hand + 1:
            cand = [c for c in hand if c["r"] in ranks and c["s"] != self.trump]
            cand += [c for c in hand if c["r"] in ranks and c["s"] == self.trump]
            if cand and self._undef() is None:
                card = cand[0]; hand.remove(card)
                self.table.append({"a": card, "d": None})
                self.phase = "defend"
                self.msg = "Корчмарь подкинул: " + card["label"]
                return True
        # нечего подкинуть — бито
        if self._undef() is None:
            self._end_bout(defended=True)
            self.msg = "Корчмарь говорит «бито». " + ("Твой заход." if self.attacker == YOU else "Корчмарь заходит.")
            return True
        return False

    def _bot_defend(self):
        pair = self._undef()
        if not pair:
            self.phase = "attack"
            return True
        hand = self.hands[BOT]
        # побить наименьшей подходящей (козырь — в крайнем случае)
        beats = [c for c in hand if self.beats(c, pair["a"])]
        beats.sort(key=lambda c: (c["s"] == self.trump, c["r"]))
        if beats:
            card = beats[0]; hand.remove(card)
            pair["d"] = card
            self.phase = "attack"
            self.msg = "Корчмарь отбился: " + card["label"]
            return True
        # не может побить — берёт
        self._end_bout(defended=False)
        self.msg = "Корчмарь забрал карты. Твой заход."
        return True

    def _lowest(self, hand, prefer_nontrump=False):
        if not hand:
            return None
        pool = [c for c in hand if c["s"] != self.trump] if prefer_nontrump else []
        pool = pool or list(hand)
        return min(pool, key=lambda c: c["r"])

    # ── вид для клиента (глазами игрока) ──
    def view(self):
        return {
            "ok": True,
            "trump": self.trump,
            "trump_card": (self.deck[-1]["label"] if self.deck else None),
            "deck": len(self.deck),
            "hand": list(self.hands[YOU]),
            "bot_cards": len(self.hands[BOT]),
            "table": [{"a": p["a"], "d": p["d"]} for p in self.table],
            "attacker": self.attacker,
            "phase": self.phase,
            "your_turn": (self.phase != "over" and (
                (self.attacker == YOU and self.phase == "attack") or
                (self._defender() == YOU and self.phase == "defend"))),
            "role": ("attack" if self.attacker == YOU else "defend"),
            "over": self.phase == "over",
            "loser": self.loser,
            "msg": self.msg,
        }
