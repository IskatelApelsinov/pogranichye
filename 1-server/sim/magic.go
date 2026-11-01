package sim

import (
	"pogranichye/arcane"
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
)

// --- Магия: маги тянут ману из поля места и плетут заклятья в неактивном окне ---
// По канону маг наполняется маной из местного поля (гуще в местах силы), а тратит её на ПЛЕТЕНИЕ,
// исход которого решают ёмкость ума и навык (см. arcane). Два первых заклятья, нужных живому миру:
//   - Лечение — ускоряет обмен веществ раненого (смыкается с лекарями: маг-целитель мощнее знахаря).
//   - Оберег — держит над селом узор, отваживающий зверьё (снижает опасные встречи охотников/стад).
//
// Маг творит не по приказу, а по реальной нужде места: есть раненый — лечит; рыщет опасный зверь —
// крепит оберег. Сорванное плетение бьёт по самому магу (откат) — сила мага честно ограничена умом.
var DebugSpells, DebugSpellFails, DebugHeals, DebugWards int

type MagicSystem struct{}

func (MagicSystem) Name() string { return "magic" }

const (
	healComplexity = 6.0  // сложность плетения лечения
	wardComplexity = 5.0  // сложность плетения оберега
	wardGain       = 0.45 // насколько маг поднимает оберег за удачное плетение
	wardDecay      = 0.7  // оберег сам слабеет день ото дня — его надо подновлять
	fieldDrain     = 0.02 // доля вытянутой маны, на которую проседает поле места (мёртвые зоны; worldmut добавит реген)
	dangerForWard  = 0.1  // выше этой опасности угодий маг берётся за оберег
)

func (MagicSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	ms := hub.Stream("magic")

	// оберег сам слабеет — маги его подновят ниже; без мага он быстро сойдёт на нет
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s.Ward > 0 {
			s.Ward *= wardDecay
			if s.Ward < 0.02 {
				s.Ward = 0
			}
		}
	})

	idx := residentIndex(w)
	w.Each("mage", func(e *core.Entity) {
		mg := arcane.Mage(e)
		v := core.Vitals(e)
		pl := core.Place(e)
		if mg == nil || v == nil || pl == nil || pl.Settlement == 0 || v.Energy <= 5 {
			return
		}
		se := w.Get(pl.Settlement)
		if se == nil {
			return
		}
		set := core.Settle(se)
		skill := 0.0
		if sk := core.Skills(e); sk != nil {
			skill = sk.Level("mage")
		}

		// 1) наполнение маной из поля места; поле на столько и проседает (истощение местности)
		drawn := mg.Refill(skill, set.Mana)
		if drawn > 0 {
			set.Mana -= drawn * fieldDrain
			if set.Mana < 0.05 {
				set.Mana = 0.05 // поле не высыхает в абсолютный ноль мгновенно
			}
		}

		// 2) по нужде места выбрать плетение: сперва раненые, затем оберег от зверья. Плетёт лишь
		// когда накопил довольно маны — иначе копит дальше (магия идёт неспешно, в такт полю места).
		if target := worstWounded(idx[pl.Settlement]); target != nil && mg.Mana >= healComplexity {
			ok, backfire := mg.Weave(healComplexity, skill, ms)
			DebugSpells++
			if ok {
				if b := battle.Body(target); b != nil {
					b.Mend(6 + skill*0.1) // разгон обмена: раны затягиваются скачком
					DebugHeals++
				}
			} else {
				DebugSpellFails++
				backfireHurt(w, e, backfire, ms)
			}
			trainMage(e)
			return
		}
		if f := battle.Fauna(se); f != nil && f.Danger() > dangerForWard && set.Ward < 0.7 && mg.Mana >= wardComplexity {
			ok, backfire := mg.Weave(wardComplexity, skill, ms)
			DebugSpells++
			if ok {
				set.Ward = clamp(set.Ward+wardGain, 0, 0.8)
				DebugWards++
			} else {
				DebugSpellFails++
				backfireHurt(w, e, backfire, ms)
			}
			trainMage(e)
		}
	})
}

// worstWounded — самый тяжело раненный житель поселения (кому магия нужнее всего), или nil.
func worstWounded(res []*core.Entity) *core.Entity {
	var worst *core.Entity
	worstSev := 0.0
	for _, r := range res {
		b := battle.Body(r)
		if b == nil {
			continue
		}
		sev := b.Severity() + b.Bleeding*0.05 + b.Infection*0.05
		if sev > worstSev {
			worstSev = sev
			worst = r
		}
	}
	if worstSev <= 0 {
		return nil
	}
	return worst
}

// backfireHurt — сорванное плетение бьёт по магу (рана по телу): честная цена перенапряжения.
func backfireHurt(w *core.World, e *core.Entity, backfire float64, s *dice.Stream) {
	if backfire <= 0.5 {
		return
	}
	b := battle.Body(e)
	if b == nil {
		nb := &battle.BodyC{Wounds: map[string]float64{}}
		w.Attach(e, nb)
		b = nb
	}
	b.Hurt(backfire, s)
}

func trainMage(e *core.Entity) {
	if sk := core.Skills(e); sk != nil {
		sk.Train("mage", 0.4)
	}
}
