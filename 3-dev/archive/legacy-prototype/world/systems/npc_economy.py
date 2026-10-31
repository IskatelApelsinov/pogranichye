"""Экономическая жизнь НПС: они тратят деньги, берут и отдают долги, выполняют
квесты — чтобы монеты реально ходили по миру, а не копились мёртвым грузом.

Раз в сутки (on_day) в каждой горячей деревне:
  * житель с достатком докупает себе снаряжение из лавки (оружие/тёплая одежда);
  * должник отдаёт долг (монеты возвращаются кредитору);
  * кто-то берётся за открытый квест деревни и выполняет его (слава + награда).
Холодные деревни живут упрощённо (world/offline.py), это их не касается.
"""
from __future__ import annotations
from .. import items as I
from .. import economy as E
from .. import quests as Q
from ..engine import System

# что НПС хотят докупить (по возрастанию «статуса»): тёплая одежда и оружие получше
UPGRADE_WISHLIST = ["ushanka", "coat", "fur", "boots", "knife", "spear", "bow", "sword", "amulet"]


class NpcEconomySystem(System):
    name = "npc_economy"

    def on_day(self, w, clock, rng):
        live = [c for c in w.living() if c.driver != "player"]
        if not live:
            return
        self._spend_on_gear(w, live, rng)
        self._repay_debts(w, live, rng)
        self._npc_quests(w, live, clock, rng)

    # ── траты на снаряжение из лавки ──
    def _spend_on_gear(self, w, live, rng):
        for ch in live:
            if ch.coins < 25 or rng.random() > 0.3:
                continue
            # чего не хватает из списка желаний и что есть в лавке
            for iid in UPGRADE_WISHLIST:
                if w.wares.get(iid, 0) <= 0:
                    continue
                it = I.CATALOG.get(iid)
                if not it:
                    continue
                # не покупаем то, что уже надето/есть
                if iid == getattr(ch, "weapon", "") or iid in ch.inventory:
                    continue
                price = E.ware_price(w, iid)
                if ch.coins < price + 5:
                    continue
                r = E.buy_ware(ch, w, iid)
                if r.get("ok"):
                    I.equip(ch, iid)                 # сразу надевает обновку
                    break

    # ── возврат долгов ──
    def _repay_debts(self, w, live, rng):
        for ch in live:
            if not ch.debts or rng.random() > 0.5:
                continue
            for who, amt in list(ch.debts.items()):
                if ch.coins < amt:
                    amt = ch.coins                    # отдаёт сколько может (частично)
                if amt <= 0:
                    continue
                ch.coins -= amt
                ch.debts[who] = ch.debts.get(who, 0) - amt
                if ch.debts[who] <= 0.01:
                    ch.debts.pop(who, None)
                cred = w.by_name(who)
                if cred:
                    cred.coins += amt
                break

    # ── выполнение квестов жителями ──
    def _npc_quests(self, w, live, clock, rng):
        for q in Q.open_quests(w):
            if q.kind != "activity" or q.done:
                continue
            if rng.random() > 0.35:
                continue
            helper = rng.choice([c for c in live if c.name != q.giver] or live)
            q.have = q.need
            q.done = True
            reward = q.reward()
            helper.coins += reward
            helper.reputation += 1.0
            w.add_news(clock, f"{helper.name} выручил {q.giver}: {q.title} (−долг, +{reward:.0f}⌾).")
            w.add_chronicle(clock, f"{helper.name} помог {q.giver} ({q.title}).")
            return                                    # по одному квесту в день — не частим
