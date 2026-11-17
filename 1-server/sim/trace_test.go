package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Диагностика: проследить население, еду и голод по дням — понять, где рвётся экономика.
// Запуск: go test ./sim/ -run TestTrace -v
func TestTrace(t *testing.T) {
	w, hub := Generate("trace-1", 10)
	r := core.NewRunnerWith(hub, core.Clock{}, Default()...)
	for d := 0; d <= 1000; d++ {
		if d%80 == 0 {
			pop := 0
			var totFood, totWheat, totHunger float64
			w.Each("ident", func(e *core.Entity) {
				pop++
				totHunger += core.Vitals(e).Hunger
			})
			w.Each("settlement", func(se *core.Entity) {
				totFood += econ.FoodOnHand(se)
				totWheat += core.Settle(se).Store[econ.GoodWheat]
			})
			avgH := 0.0
			if pop > 0 {
				avgH = totHunger / float64(pop)
			}
			t.Logf("день %4d сезон=%s  души=%3d  еда=%.0f  зерно=%.0f  ср.голод=%.0f",
				d, r.Clock.Season(), pop, totFood, totWheat, avgH)
			t.Logf("   рожд=%d смертей=%d (голод=%d старость=%d)", DebugBirths, DebugDeaths, DebugFamine, DebugOld)
		}
		r.Run(w, core.HoursPerDay)
	}
}
