package sim

import (
	"fmt"
	"sort"

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
