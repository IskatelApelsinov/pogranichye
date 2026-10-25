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

        # пьют медовуху, пока она есть — расслабляются (ниже усталость → выше настрой)
        for ch in patrons:
            if w.store.get("mead", 0) >= T.DRINK_MEAD:
                w.store["mead"] -= T.DRINK_MEAD
                ch.fatigue = max(0.0, ch.fatigue - T.DRINK_RELAX)
                ch.last_act = "пьёт медовуху в таверне"
            else:
                ch.last_act = "сидит в таверне"      # медовуха кончилась — просто вечер вместе

        w.tavern_tonight = [c.name for c in patrons]

        # история: раз за вечер в таверне могут вспомнить слух → в долгую память
        if hour == T.TAVERN_HOURS[0] and w.news and rng.random() < 0.15:
            gossip = w.news[-1].split(": ", 1)[-1]
            w.add_chronicle(clock, f"Вечером в таверне вспоминали: {gossip.lower()}")
