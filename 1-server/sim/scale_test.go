package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
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
		foundNewSettlement(w, dice.NewStream(uint64(1000+d)), int64(d+1))
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
	migrants := 0
	maxHours := 0.0
	w.Each("migration", func(e *core.Entity) {
		if m := Migration(e); m != nil && m.To == newSet {
			migrants++
			if m.TotalHours > maxHours {
				maxHours = m.TotalHours
			}
		}
	})
	if migrants < 3 {
		t.Fatalf("переселенцы должны отправиться в путь с сохранением личностей: %d", migrants)
	}
	if len(residentsOf(w, newSet)) != 0 {
		t.Fatal("переселенцы не должны появляться в новом поселении до прибытия")
	}
	for h := 0; h <= int(maxHours); h++ {
		(MigrationSystem{}).Hour(w, core.Clock{}, nil)
	}
	if len(residentsOf(w, newSet)) < 3 {
		t.Fatal("после завершения пути в новом поселении должна прибыть ватага переселенцев")
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

// Болезнь только от реальных причин: скученный город без медицины страдает от хвори, а тот же
// город со снадобьем/знахарем — нет. Маленькая деревня не болеет вовсе.
func TestPlagueFromCrowdingNotSeason(t *testing.T) {
	build := func(pop int, remedy float64, herbalists int) *core.World {
		w := core.NewWorld("plague")
		se := w.Spawn(&core.SettlementC{Name: "Город", Tier: "city",
			Store: map[string]float64{econ.GoodBread: 9e6, econ.GoodRemedy: remedy}})
		for i := 0; i < pop; i++ {
			role := "farmer"
			if i < herbalists {
				role = "herbalist"
			}
			w.Spawn(&core.IdentC{Name: "Г", Role: role, Sex: "м"},
				&core.VitalsC{Age: 25, Health: 100, Hunger: 0, Energy: 90},
				&core.PlaceC{Settlement: se.ID})
		}
		return w
	}
	popOf := func(w *core.World) int {
		n := 0
		w.Each("ident", func(*core.Entity) { n++ })
		return n
	}
	// крупный город без медицины — хворь должна проредить
	wBad := build(30, 0, 0)
	n0 := popOf(wBad)
	rb := core.NewRunner("plague", DiseaseSystem{})
	rb.Run(wBad, 500*core.HoursPerDay)
	if popOf(wBad) >= n0 {
		t.Fatalf("скученный город без медицины должен страдать от хвори (было %d, стало %d)", n0, popOf(wBad))
	}
	// тот же город со снадобьем и знахарями — хвори нет
	wGood := build(30, 5000, 4)
	m0 := popOf(wGood)
	rg := core.NewRunner("plague", DiseaseSystem{})
	rg.Run(wGood, 500*core.HoursPerDay)
	if popOf(wGood) < m0 {
		t.Fatalf("медицина должна гасить хворь (было %d, стало %d)", m0, popOf(wGood))
	}
	// маленькая деревня не болеет
	wSmall := build(8, 0, 0)
	s0 := popOf(wSmall)
	rs := core.NewRunner("plague", DiseaseSystem{})
	rs.Run(wSmall, 500*core.HoursPerDay)
	if popOf(wSmall) < s0 {
		t.Fatalf("малая деревня без скученности болеть не должна (было %d, стало %d)", s0, popOf(wSmall))
	}
}
