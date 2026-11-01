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
var DebugSpells, DebugSpellFails, DebugHeals, DebugWards, DebugAwakened, DebugSparks int

type MagicSystem struct{}

func (MagicSystem) Name() string { return "magic" }

const (
	healComplexity = 6.0  // сложность плетения лечения
	wardComplexity = 5.0  // сложность плетения оберега
	wardGain       = 0.45 // насколько маг поднимает оберег за удачное плетение
	wardDecay      = 0.7  // оберег сам слабеет день ото дня — его надо подновлять
	fieldDrain     = 0.02 // доля вытянутой маны, на которую проседает поле места (мёртвые зоны; worldmut добавит реген)
	dangerForWard  = 0.1  // выше этой опасности угодий маг берётся за оберег

	// пробуждение дара — НАМЕРЕННО редкое и медленное: латент почти всю жизнь остаётся при своём
	// ремесле (мир не должен обезлюдеть на ремёсла из-за магии); пробуждаются единицы.
	awakenAt       = 32.0 // навык магии, с которого латент становится магом (высокий порог — годы учения)
	manaLearn      = 0.4  // как быстро латент учится от МАНЫ места (× плотность маны)
	teachLearn     = 0.4  // вклад каждого учителя-мага в селе
	elderTeachBon  = 0.5  // старец-маг (долгожитель) учит сильнее
	schoolLearnBon = 0.8  // школа большого города ускоряет обучение
	seekChance     = 0.01 // шанс латенту уйти в настоящую школу (большой город с магом)
	mageTargetFrac = 0.01 // целевая доля пробуждённых магов в мире (редко)
	godSparkChance = 0.03 // шанс, что боги даруют искру, когда магов меньше цели (равновесие)
	godSparkMinPop = 60   // боги не вмешиваются в малых мирах — там магов может и не быть
	elderAge       = 60.0 // возраст «старца»
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

	// Проход 1: по сёлам — сила учителей (пробуждённые маги; старцы учат сильнее) и есть ли школа
	// (большой город с пробуждённым магом). Заодно считаем, сколько магов уже пробудилось в мире.
	teach := map[core.ID]float64{}
	school := map[core.ID]bool{}
	var schools, placesOfPower []core.ID
	awakenedTotal := 0
	people := 0
	w.Each("mage", func(e *core.Entity) {
		mg := arcane.Mage(e)
		pl := core.Place(e)
		if mg == nil || !mg.Awakened || pl == nil || pl.Settlement == 0 {
			return
		}
		awakenedTotal++
		t := teachLearn
		if v := core.Vitals(e); v != nil && v.Age >= elderAge {
			t += elderTeachBon // старец-архимаг — лучший учитель (канон)
		}
		teach[pl.Settlement] += t
	})
	w.Each("place", func(e *core.Entity) {
		if core.Ident(e) != nil {
			people++
		}
	})
	w.Each("settlement", func(se *core.Entity) {
		set := core.Settle(se)
		if set.Tier == "city" && teach[se.ID] > 0 {
			school[se.ID] = true
			schools = append(schools, se.ID)
		}
		if set.Mana >= 1.3 {
			placesOfPower = append(placesOfPower, se.ID)
		}
	})

	// Проход 2: каждый носитель дара — латент учится/пробуждается/ищет, пробуждённый плетёт
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
		sk := core.Skills(e)
		skill := 0.0
		if sk != nil {
			skill = sk.Level("mage")
		}

		if !mg.Awakened {
			latentStep(w, e, mg, sk, skill, set, pl.Settlement, teach, school, schools, placesOfPower, ms)
			return
		}

		// --- пробуждённый маг: наполнение маной и плетение по нужде места ---
		drawn := mg.Refill(skill, set.Mana)
		if drawn > 0 {
			set.Mana -= drawn * fieldDrain
			if set.Mana < 0.05 {
				set.Mana = 0.05
			}
		}
		if target := worstWounded(idx[pl.Settlement]); target != nil && mg.Mana >= healComplexity {
			ok, backfire := mg.Weave(healComplexity, skill, ms)
			DebugSpells++
			if ok {
				if b := battle.Body(target); b != nil {
					b.Mend(6 + skill*0.1)
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
			return
		}
		// нет нужды — маг с учениками-латентами в селе учит (иначе просто упражняется)
		if hasLatent(idx[pl.Settlement]) {
			if m := core.Mind(e); m != nil {
				m.LastAct = "обучает ученика"
			}
		}
	})

	// Проход 3: боги хранят равновесие — если пробуждённых меньше редкой цели, изредка даруют искру.
	// В МАЛЫХ мирах боги не вмешиваются: там магов может и не быть вовсе (ремёсла важнее).
	desired := float64(people) * mageTargetFrac
	if people >= godSparkMinPop && float64(awakenedTotal) < desired && ms.Chance(godSparkChance) {
		grantSpark(w, ms)
	}
}

// latentStep — шаг латента: учится у маны места/учителей/школы; накопил навык — пробудился; в бедном
// на магию месте может уйти искать школу или место силы (тяга высвободить дар — в автоматном мозгу).
func latentStep(w *core.World, e *core.Entity, mg *arcane.MageC, sk *core.SkillsC, skill float64,
	set *core.SettlementC, settlement core.ID, teach map[core.ID]float64, school map[core.ID]bool,
	schools, placesOfPower []core.ID, s *dice.Stream) {
	m := core.Mind(e)
	// скорость учения: мана места + учителя + школа
	rate := manaLearn*set.Mana + teach[settlement]
	if school[settlement] {
		rate += schoolLearnBon
	}
	if sk != nil && rate > 0 {
		sk.Train("mage", rate*0.1) // неспешно (×0.1/день): дар зреет годами
		skill = sk.Level("mage")
	}
	if skill >= awakenAt { // ПРОБУЖДЕНИЕ
		mg.Awakened = true
		if id := core.Ident(e); id != nil {
			id.Role = "mage"
		}
		if m != nil {
			m.LastAct = "пробудился как маг"
		}
		if v := core.Vitals(e); v != nil {
			v.Morale = clamp(v.Morale+20, 0, 100)
		}
		DebugAwakened++
		return
	}
	// место бедно на магию (нет ни маны, ни учителя), а где-то есть НАСТОЯЩАЯ школа — латент может
	// уйти туда учиться. Редко (переселение дорого миру), и только в школу (не в любое место силы),
	// чтобы не растаскивать малолюдные сёла.
	poor := set.Mana < 0.9 && teach[settlement] <= 0
	_ = placesOfPower
	if poor && len(schools) > 0 && s.Chance(seekChance) {
		dest := schools[s.Intn(len(schools))]
		if dest != settlement {
			moveToSettlement(e, dest)
			if m != nil {
				m.LastAct = "ушёл в школу магии"
			}
			return
		}
	}
	if m != nil {
		m.LastAct = "тянется к магии" // носит искру, упражняется как умеет
	}
}

// hasLatent — есть ли в селе непробуждённый носитель дара (ученик).
func hasLatent(res []*core.Entity) bool {
	for _, r := range res {
		if mg := arcane.Mage(r); mg != nil && !mg.Awakened {
			return true
		}
	}
	return false
}

// grantSpark — боги даруют искру случайному взрослому без дара (равновесие числа магов).
func grantSpark(w *core.World, s *dice.Stream) {
	var cand []*core.Entity
	w.Each("vitals", func(e *core.Entity) {
		v := core.Vitals(e)
		if v == nil || v.Age < 16 || v.Age > 45 {
			return
		}
		if arcane.Mage(e) != nil || core.Ident(e) == nil {
			return
		}
		cand = append(cand, e)
	})
	if len(cand) == 0 {
		return
	}
	e := cand[s.Intn(len(cand))]
	w.Attach(e, &arcane.MageC{Awakened: false})
	if m := core.Mind(e); m != nil {
		m.LastAct = "ощутил пробуждение дара"
	}
	DebugSparks++
}

// moveToSettlement переселяет человека в указанное поселение (латент — к школе/месту силы).
func moveToSettlement(e *core.Entity, dest core.ID) {
	if pl := core.Place(e); pl != nil {
		pl.Settlement = dest
	}
	if h := core.Home(e); h != nil {
		h.Status = "none"
		h.Settlement = dest
	}
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
