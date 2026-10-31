"""
Балансовый стенд (dev-инструмент, не для прода). Меряем реальные числа, а не
на глаз: экономику игрока, винрейты боя, профит торговли, прогресс навыков.
    python3 balance.py
"""
from __future__ import annotations
import random
from world.model import Character
from world import combat as C, items as I, player as P


def combat_winrates(quality=0.6, trials=600):
    print(f"=== ВИНРЕЙТ БОЯ (качество мини-игры {quality}, {trials} попыток) ===")
    weapons = [("без оружия", ""), ("нож", "knife"), ("рогатина", "spear"), ("лук", "bow")]
    beasts = ["hare", "fox", "boar", "wolf", "bear", "bandit"]
    rng = random.Random(1)
    hdr = "зверь\\оружие".ljust(12) + "".join(w[0].ljust(12) for w in weapons)
    print(hdr)
    for bid in beasts:
        row = (C.info(bid)["name"] + f"(p{C.info(bid)['power']})").ljust(12)
        for _, wid in weapons:
            wins = 0
            for _ in range(trials):
                ch = Character(name="t", role="t", skills={"hunting": 20, "combat": 20})
                ch.weapon = wid
                r = C.resolve(ch, bid, quality, rng)
                wins += 1 if r.get("win") else 0
            row += f"{wins*100//trials}%".ljust(12)
        print(row)
    print()


def skill_growth():
    print("=== РОСТ НАВЫКА (сколько дел до мастера 60) ===")
    w = _fake_settlement()
    for act in ["firewood", "fishing"]:
        ch = Character(name="t", role="t")
        rng = random.Random(1)
        n = 0
        sk = P.ACTIVITIES[act]["skill"]
        while ch.skill(sk) < 60 and n < 100000:
            P.do_activity(w, ch, act, 0.7, rng)
            n += 1
        print(f"  {act:9} навык {sk:9}: ~{n} выполнений до мастера (60)")
    print()


def economy(days=60):
    print(f"=== ЭКОНОМИКА: грайнд активностей, {days} дней ===")
    rng = random.Random(1)
    ch = Character(name="t", role="t", coins=60.0, home="owned")
    w = _fake_settlement()
    coins_log = []
    WORK_PER_DAY = 10            # примерно столько активностей успевает игрок в день
    for d in range(days):
        for _ in range(WORK_PER_DAY):
            act = rng.choice(["firewood", "garden", "fishing", "berries", "build"])
            P.do_activity(w, ch, act, 0.65, rng)
        P.player_day(ch)
        if d in (0, 9, 29, 59):
            coins_log.append((d + 1, round(ch.coins, 1)))
    print("  монеты по дням (грайнд, дом свой, без квестов/торга):", coins_log)
    print(f"  навыки к концу: " + ", ".join(f"{k} {v:.0f}" for k, v in ch.skills.items()))
    print()


def trade_profit():
    print("=== ПРОФИТ ТОРГОВЛИ (караван: купить дёшево → продать дорого) ===")
    cheap = _fake_settlement(market=0.75)      # Кривичи
    dear = _fake_settlement(market=1.35)       # Светлояр
    cheap.store["food"] = 200
    ch = Character(name="t", role="t", coins=200.0)
    r = P.buy_goods(cheap, ch, "food", 40)
    spent = 200.0 - ch.coins
    P.sell_goods(dear, ch, "food", 40)
    got = ch.coins - (200.0 - spent)
    print(f"  куплено 40 food за {spent:.0f}⌾ (Кривичи ×0.75), продано за {got:.0f}⌾ (Светлояр ×1.35)")
    print(f"  чистый профит за рейс: {got - spent:.0f}⌾ ({(got/spent-1)*100:.0f}%)")
    print()


def hunting_yield():
    print("=== ДОБЫЧА С ОХОТЫ (средняя за победу, качество 0.6) ===")
    rng = random.Random(1)
    for bid in ["hare", "boar", "bear"]:
        tot_raw = tot_coin = wins = 0
        for _ in range(400):
            ch = Character(name="t", role="t", weapon="spear", skills={"hunting": 25})
            r = C.resolve(ch, bid, 0.6, rng)
            if r.get("win"):
                wins += 1
                tot_raw += sum(r.get("loot", {}).values())
                tot_coin += r.get("coins_gain", 0)
        if wins:
            print(f"  {C.info(bid)['name']:9}: победа→ ~{tot_raw/wins:.0f} raw, +{tot_coin/wins:.1f}⌾")
    print()


def _fake_settlement(market=1.0):
    from world.model import Settlement
    s = Settlement(name="тест")
    s.store.update({"food": 50, "wood": 50, "raw": 20, "honey": 10, "mead": 10, "tools": 2})
    s.market = market
    return s


if __name__ == "__main__":
    combat_winrates()
    hunting_yield()
    trade_profit()
    economy()
    skill_growth()
