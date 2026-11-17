package sim

import (
	"fmt"
	"sort"

	"pogranichye/arcane"
	"pogranichye/battle"
	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Report печатает агрегаты жизни мира (для -demo и инструментов): поселения и их ступени,
// возрастные группы, чем заняты, ремёсла. Контраст с мёртвым логом прототипа.
func Report(w *core.World, clock core.Clock) {
	fmt.Printf("Год %d, сезон %s\nПоселения:\n", clock.Year(), clock.Season())
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		res := 0
		w.Each("place", func(e *core.Entity) {
			if p := core.Place(e); p != nil && p.Settlement == se.ID && core.Ident(e) != nil {
				res++
			}
		})
		fmt.Printf("  %-14s [%-7s] жителей %-3d · еды %.0f · зерна %.0f\n",
			s.Name, s.Tier, res, econ.FoodOnHand(se), s.Store[econ.GoodWheat])
	})

	ages := map[string]int{"дети (0-15)": 0, "взрослые (16-55)": 0, "старики (56+)": 0}
	goals := map[string]int{}
	roles := map[string]int{}
	w.Each("vitals", func(e *core.Entity) {
		a := core.Vitals(e).Age
		switch {
		case a < 16:
			ages["дети (0-15)"]++
		case a < 56:
			ages["взрослые (16-55)"]++
		default:
			ages["старики (56+)"]++
		}
		if m := core.Mind(e); m != nil {
			goals[m.Goal]++
		}
		if id := core.Ident(e); id != nil {
			roles[id.Role]++
		}
	})
	fmt.Printf("\nВозрастные группы: %s\n", kv(ages))
	fmt.Printf("Чем заняты (цели):  %s\n", kv(goals))
	fmt.Printf("Ремёсла:            %s\n", kv(roles))

	// боёвка: сейчас раненых + итоги охоты за прогон + мясо/шкуры на рынках
	wounded := 0
	w.Each("body", func(e *core.Entity) {
		if b := battle.Body(e); b != nil && (len(b.Wounds) > 0 || b.Bleeding > 0) {
			wounded++
		}
	})
	meat, hide := 0.0, 0.0
	beasts := map[string]float64{}
	manaSum, manaN := 0.0, 0
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		meat += s.Store[econ.GoodMeat]
		hide += s.Store[econ.GoodHide]
		manaSum += s.Mana
		manaN++
		if f := battle.Fauna(se); f != nil {
			for sp, p := range f.Pop {
				beasts[sp] += p
			}
		}
	})
	avgMana := 0.0
	if manaN > 0 {
		avgMana = manaSum / float64(manaN)
	}
	fmt.Printf("Охота и раны:       сейчас ранено=%d · охот=%d (добыто=%d, ранено в них=%d) · погибло в бою=%d · мяса=%.0f шкур=%.0f\n",
		wounded, DebugHunts, DebugWins, DebugWounds, DebugBattleDeaths, meat, hide)
	fmt.Printf("Зверьё в угодьях:   заяц=%.0f лиса=%.0f кабан=%.0f волк=%.0f медведь=%.0f · средняя мана %.2f\n",
		beasts["hare"], beasts["fox"], beasts["boar"], beasts["wolf"], beasts["bear"], avgMana)

	latent, awake, warded := 0, 0, 0
	w.Each("mage", func(e *core.Entity) {
		if mg := arcane.Mage(e); mg != nil && mg.Awakened {
			awake++
		} else {
			latent++
		}
	})
	w.Each("settlement", func(se *core.Entity) {
		if core.Settle(se).Ward > 0.05 {
			warded++
		}
	})
	fmt.Printf("Магия:              пробуждённых=%d латентов=%d · пробуждений=%d искр от богов=%d\n",
		awake, latent, DebugAwakened, DebugSparks)
	fmt.Printf("                    плетений=%d (сорвано=%d) · исцелений=%d · оберегов=%d · сёл под оберегом=%d\n",
		DebugSpells, DebugSpellFails, DebugHeals, DebugWards, warded)

	treasury := 0.0
	w.Each("realm", func(re *core.Entity) { treasury += econ.Realm(re).Treasury })
	inTransit := 0
	w.Each("caravan", func(*core.Entity) { inTransit++ })
	fmt.Printf("Торговля держав:    караванов снаряжено=%d дошло=%d ограблено=%d · в пути сейчас=%d · казна держав=%.0f · шкур на рынках %.0f\n",
		econ.DebugCaravans, econ.DebugArrived, econ.DebugRobbed, inTransit, treasury, hide)

	atWar := 0
	w.Each("realm", func(re *core.Entity) {
		if econ.Realm(re).WarWith != 0 {
			atWar++
		}
	})
	fmt.Printf("Беды и хворь:       войн было=%d (сейчас воюют держав=%d) · павших в войне=%d · непогод=%d · умерших от хвори=%d\n",
		DebugWars, atWar, DebugWarDead, DebugClimate, DebugPlague)

	allies, rivals := relationsSnapshot(w)
	fmt.Printf("Дипломатия держав:  союзных пар=%d · враждебных пар=%d\n", allies, rivals)

	if c := chronicle.Of(w); c != nil {
		keys := make([]string, 0, len(c.Tally))
		for k := range c.Tally {
			keys = append(keys, k)
		}
		sort.Strings(keys) // стабильный порядок отчёта (карта сама по себе неупорядочена)
		tally := ""
		for _, k := range keys {
			tally += fmt.Sprintf("%s=%d  ", k, c.Tally[k])
		}
		fmt.Printf("\nЛетопись мира (всего по родам): %s\n", tally)
		fmt.Println("Последнее, что помнит мир:")
		from := len(c.Entries) - 8
		if from < 0 {
			from = 0
		}
		for _, e := range c.Entries[from:] {
			fmt.Printf("  день %-5d [%-9s] %s\n", e.Day, e.Kind, e.Text)
		}
	}
}

// Sample печатает выборку жителей (для -demo -v).
func Sample(w *core.World, n int) {
	fmt.Printf("\nВыборка жителей:\n")
	var es []*core.Entity
	w.Each("ident", func(e *core.Entity) { es = append(es, e) })
	if len(es) > n {
		es = es[:n]
	}
	for _, e := range es {
		id := core.Ident(e)
		v := core.Vitals(e)
		m := core.Mind(e)
		full := id.Name
		if id.Surname != "" {
			full += " " + id.Surname
		}
		fmt.Printf("  %-22s %s %-13s возр %4.1f  здоровье %3.0f  настрой %3.0f  %s\n",
			full, id.Sex, id.Role, v.Age, v.Health, v.Morale, m.LastAct)
	}
}

func kv(m map[string]int) string {
	keys := make([]string, 0, len(m))
	for k := range m {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	out := ""
	for _, k := range keys {
		out += fmt.Sprintf("%s=%d  ", k, m[k])
	}
	return out
}
