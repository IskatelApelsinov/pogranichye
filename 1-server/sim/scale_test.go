package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// Основание новых поселений: переполненная деревня с амбициозными даёт переселенцев,
// которые ставят НОВУЮ деревню (мир растёт числом мест, а не только населением).
func TestFounding(t *testing.T) {
	w := core.NewWorld("found")
	g := dice.NewStream(42)
	src := NewSettlement(w, g, "Исток", nil)
	// набить деревню до скученности амбициозными взрослыми
	for i := 0; i < birthCapPop; i++ {
		p := spawnPerson(w, g, src.ID)
		core.Drives(p).Ambition = 80
		core.Vitals(p).Age = 25
	}
	before := countSettlements(w)

	// прогнать много попыток основания (событие редкое — даём костям шанс сработать)
	founded := false
	for d := 0; d < 400 && !founded; d++ {
		foundNewSettlement(w, dice.NewStream(uint64(1000+d)))
		if countSettlements(w) > before {
			founded = true
		}
	}
	if !founded {
		t.Fatal("из переполненной амбициозной деревни должно было основаться новое поселение")
	}
	// у новой деревни есть угодья и переселенцы
	var newSet core.ID
	w.Each("settlement", func(se *core.Entity) {
		if se.ID != src.ID {
			newSet = se.ID
		}
	})
	if len(residentsOf(w, newSet)) < 3 {
		t.Fatal("в новом поселении должна быть ватага переселенцев")
	}
}

func countSettlements(w *core.World) int {
	n := 0
	w.Each("settlement", func(*core.Entity) { n++ })
	return n
}

// Бенчмарк масштаба: раз язык Go — проверим, тянет ли он большой мир. Печатает душ/угодий
// и скорость тика. Запуск: go test ./sim/ -run x -bench BenchmarkTick -benchmem
func BenchmarkTick(b *testing.B) {
	w, hub := Generate("bench", 300) // 300 поселений
	r := core.NewRunnerWith(hub, core.Clock{}, Default()...)
	// прогреть мир год, чтобы были дети/старики/запасы (реалистичная нагрузка)
	r.Run(w, 200*core.HoursPerDay)
	people := 0
	w.Each("ident", func(*core.Entity) { people++ })
	b.Logf("мир: %d сущностей (%d душ) на старте бенча", w.Count(), people)
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		r.Step(w) // один час мира
	}
}
