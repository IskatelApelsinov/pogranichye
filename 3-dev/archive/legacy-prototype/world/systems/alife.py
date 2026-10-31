"""
A-LIFE: у жителей свои потребности, и они гонят РАЗВИТИЕ и ПЕРЕДВИЖЕНИЕ — мир
движется сам, а не по случайности.

Потребности (0..100):
  * social   — тяга к людям; растёт день ото дня, гаснет вечером в таверне (есть
    медовуха). Высокая social склоняет вечером идти в корчму (её и так наполняет
    TavernSystem — здесь мы поддерживаем саму потребность для будущих решений).
  * ambition — жажда доли/роста; растёт у незанятых и у тех, кто не растёт (мало
    навыка, роль idler), гаснет, когда человек при деле. Накопив амбицию и не видя
    роста здесь, житель УХОДИТ искать лучшей доли (осмысленная миграция вместо
    случайной). Ушедших мир расселяет (departures → странники → другие деревни).

Игроков это не трогает (у них свой драйвер). Раз в сутки, подключаемый модуль.
"""
from __future__ import annotations
from .. import tuning as T
from ..engine import System

AMBITION_LEAVE = 80.0
MIN_STAY = 4                 # не опустошаем деревню ниже этого


class AlifeSystem(System):
    name = "alife"

    def on_day(self, w, clock, rng):
        liv = w.living()
        has_mead = w.tavern and w.store.get("mead", 0) > 0
        leavers = []
        for ch in liv:
            if ch.driver == "player" or getattr(ch, "afk", False):
                continue                                  # игроков A-LIFE не гонит
            # социальная потребность
            ch.social = min(100.0, ch.social + 6.0)
            if has_mead:
                ch.social = max(0.0, ch.social - 10.0)    # вечер в корчме гасит тягу
            # дом слегка изнашивается — нужда поддерживать его (WorkSystem.homebuild)
            ch.dwelling = max(0.0, ch.dwelling - 0.5)
            # амбиция: растёт у незанятых/нерастущих, гаснет у занятых
            top = max(ch.skills.values()) if ch.skills else 0.0
            idle = (ch.role == "idler") or (ch.last_act in ("на досуге", "отдыхает", "—"))
            if idle or top < 5.0:
                ch.ambition = min(100.0, ch.ambition + 5.0)
            else:
                ch.ambition = max(0.0, ch.ambition - 2.0)
            # обжитой дом держит человека на месте (довольство гасит тягу уйти)
            if ch.dwelling >= 50.0:
                ch.ambition = max(0.0, ch.ambition - 4.0)
            if ch.ambition >= AMBITION_LEAVE and rng.random() < 0.4:
                leavers.append(ch)
        # осмысленный уход: амбициозные, если деревня не опустеет
        for ch in leavers:
            if len(w.living()) <= MIN_STAY:
                break
            w.residents.remove(ch)
            ch.ambition = 0.0
            w.departures.append(ch)
            if len(w.departures) > 50:
                del w.departures[:-50]
            w.add_news(clock, f"{ch.name} {ch.v('затосковал', 'затосковала')} по иной доле "
                              f"и {ch.v('ушёл', 'ушла')} искать счастья в других краях.")
