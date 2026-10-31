// pogserver — точка входа сервера живого мира. Пока это демо-прогон: генерируем мир,
// живём заданное число дней и печатаем «отчёт о жизни» — чтобы ГЛАЗАМИ увидеть разницу
// с мёртвым прототипом (где на 5910-й день все были idler'ами в таверне). Сетевой слой
// (websocket к Unity и тонкому клиенту) встанет сюда же следующим шагом — ядро к этому готово.
//
//	go run ./cmd/pogserver                 # 2000 дней, 6 деревень
//	go run ./cmd/pogserver -days 5910 -v    # как в логе, с посписочной выборкой
package main

import (
	"flag"
	"fmt"
	"sort"

	"pogranichye/core"
	"pogranichye/memory"
	"pogranichye/sim"
)

func main() {
	days := flag.Int("days", 2000, "сколько дней прожить миру")
	seed := flag.String("seed", "пограничье-1", "семя мира")
	nset := flag.Int("villages", 6, "сколько поселений")
	verbose := flag.Bool("v", false, "печатать выборку жителей")
	flag.Parse()

	w, hub := sim.Generate(*seed, *nset)
	r := core.NewRunnerWith(hub, core.Clock{}, sim.Default()...)
	fmt.Printf("Старт: %s\n", sim.Describe(w))

	r.Run(w, *days*core.HoursPerDay)

	fmt.Printf("\nПрожито дней: %d\n%s\n\n", *days, sim.Describe(w))
	report(w)

	if *verbose {
		sample(w, 30)
	}

	if blob, err := memory.Save(w, r.Clock, r.Hub); err == nil {
		fmt.Printf("\nСнимок мира: %d КБ (ничего не потеряно — все сущности и свойства)\n", len(blob)/1024)
	}
}

// report — агрегаты жизни: ступени поселений, разброс возрастов, распределение целей.
func report(w *core.World) {
	fmt.Println("Поселения:")
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		res := 0
		w.Each("place", func(e *core.Entity) {
			if p := core.Place(e); p != nil && p.Settlement == se.ID && core.Ident(e) != nil {
				res++
			}
		})
		fmt.Printf("  %-14s [%s] жителей %-3d · склад еды %.0f, дров %.0f\n",
			s.Name, s.Tier, res, s.Store["food"], s.Store["wood"])
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

func sample(w *core.World, n int) {
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
		h := core.Home(e)
		full := id.Name
		if id.Surname != "" {
			full += " " + id.Surname
		}
		fmt.Printf("  %-22s %s %-13s возр %4.1f  здоровье %3.0f  настрой %3.0f  дом %-6s  %s\n",
			full, id.Sex, id.Role, v.Age, v.Health, v.Morale, h.Status, m.LastAct)
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
