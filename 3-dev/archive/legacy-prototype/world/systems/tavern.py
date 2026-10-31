"""
Таверна — сердце со-присутствия и слухов. Вечером жители сходятся, пьют медовуху
(расслабляются → выше настрой → выше здоровье деревни), и здесь живёт «история»:
слухи обсуждаются, порой западают в долгую память.

Это ЗАЛОЖЕНО В БАЗУ ради сетевой игры: `tavern_tonight` — ростер тех, кто сейчас
в таверне вместе. Любой из них может быть игроком (driver='player'). Когда появится
сеть, «пить медовуху с другом» уже готово: двое игроков в одной таверне в один
вечер — и есть та самая сцена. Мир → история → иммерсивность → пиво с другом.
"""
from __future__ import annotations
from .. import tuning as T
from .. import player as _player
from ..engine import System


class TavernSystem(System):
    name = "tavern"

    def on_hour(self, w, clock, rng):
        if not w.tavern:
            return
        hour = clock.hour
        if hour not in T.TAVERN_HOURS:
            if hour == T.TAVERN_HOURS[-1] + 1:
                w.tavern_tonight = []          # вечер кончился — таверна пустеет
            return

        patrons = []
        for ch in w.living():
            # в таверну идут не валящиеся с ног и не голодные (сначала дела/еда)
            if ch.fatigue < 85 and ch.hunger < 70 and hour not in T.SLEEP_HOURS:
                patrons.append(ch)

        if not patrons:
            return

        # пьют медовуху ЗА МОНЕТЫ (деньги идут корчмарю) — расслабляются и веселеют.
        # Нет монет — берут в долг у корчмаря (растёт долг перед ним), либо просто сидят.
        keeper = w.by_name(w.keeper) if w.keeper else None
        present = []
        for ch in patrons:
            if w.store.get("mead", 0) < T.DRINK_MEAD:
                ch.last_act = "сидит в таверне"      # медовуха кончилась — просто вечер вместе
                present.append(ch); continue
            paid = False
            if ch.coins >= T.DRINK_PRICE:
                ch.coins -= T.DRINK_PRICE; paid = True
                if keeper and keeper is not ch:
                    keeper.coins += T.DRINK_PRICE
            elif keeper and keeper is not ch and rng.random() < 0.5:
                # в долг у корчмаря (запишем долг перед ним)
                ch.debts[keeper.name] = ch.debts.get(keeper.name, 0.0) + T.DRINK_PRICE
                paid = True
            if not paid:
                ch.last_act = "сидит в таверне (без монет)"
                present.append(ch); continue
            w.store["mead"] -= T.DRINK_MEAD
            ch.fatigue = max(0.0, ch.fatigue - T.DRINK_RELAX)
            _player.update_condition(ch, cheered=5.0)
            ch.last_act = "пьёт медовуху в таверне"
            present.append(ch)

        w.tavern_tonight = [c.name for c in present]

        # история: раз за вечер в таверне могут вспомнить слух → в долгую память
        if hour == T.TAVERN_HOURS[0] and w.news and rng.random() < 0.15:
            gossip = w.news[-1].split(": ", 1)[-1]
            w.add_chronicle(clock, f"Вечером в таверне вспоминали: {gossip.lower()}")
