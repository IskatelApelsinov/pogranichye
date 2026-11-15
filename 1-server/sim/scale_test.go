package sim

import (
	"fmt"
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
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain"})
	src := NewSettlement(w, g, "Исток", region)
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
		(MigrationSystem{}).Hour(w, core.Clock{TotalHours: int64(h + 1)}, nil)
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


// BenchmarkNPCMovementHotIndex measures the 100 ms locomotion pass while the planet's
// cold population grows. Only hot residents have ScenePosC; cold residents retain MindC
// but no scene body. Runtime should scale with hot bodies, not total planetary population.
// Run: go test ./sim -run '^$' -bench BenchmarkNPCMovementHotIndex -benchmem
func BenchmarkNPCMovementHotIndex(b *testing.B) {
	for _, coldCount := range []int{1000, 10000, 100000} {
		b.Run(fmt.Sprintf("cold_%d", coldCount), func(b *testing.B) {
			w := core.NewWorld("npc-movement-bench")
			hot := w.Spawn(&core.SettlementC{Name: "hot"}, &HeatC{Level: 2})
			cold := w.Spawn(&core.SettlementC{Name: "cold"}, &HeatC{Level: 0})
			for i := 0; i < 64; i++ {
				e := w.Spawn(
					&core.IdentC{Role: "farmer"},
					&core.MindC{Goal: "work"},
					&core.PlaceC{Settlement: hot.ID},
				)
				x, y := SpawnSpot(uint64(e.ID))
				w.Attach(e, &ScenePosC{X: x, Y: y, Facing: 90, Stam: 1})
			}
			for i := 0; i < coldCount; i++ {
				w.Spawn(
					&core.IdentC{Role: "farmer"},
					&core.MindC{Goal: "work"},
					&core.PlaceC{Settlement: cold.ID},
				)
			}
			b.ReportAllocs()
			b.ResetTimer()
			for i := 0; i < b.N; i++ {
				StepNPCMovement(w, 0.1, nil)
			}
		})
	}
}


// BenchmarkHeatTrackerStableTopology compares the production cached tracker with the
// stateless reconciliation path while a large world remains unchanged. The server calls
// the tracker every 100 ms; the cached case should not scale with settlement count.
// Run: go test ./sim -run '^$' -bench BenchmarkHeatTrackerStableTopology -benchmem
func BenchmarkHeatTrackerStableTopology(b *testing.B) {
	for _, settlementCount := range []int{1000, 10000} {
		for _, mode := range []string{"cached_tracker", "stateless_scan"} {
			b.Run(fmt.Sprintf("%s/settlements_%d", mode, settlementCount), func(b *testing.B) {
				w := core.NewWorld("heat-tracker-bench")
				region := w.Spawn(&core.RegionC{Name: "region", Biome: "plain"})
				var first core.ID
				for i := 0; i < settlementCount; i++ {
					se := w.Spawn(
						&core.SettlementC{Name: "village", Region: region.ID},
						&HeatC{Level: 0},
					)
					if i == 0 {
						first = se.ID
					}
					for j := 0; j < 10; j++ {
						w.Spawn(
							&core.IdentC{Role: "farmer"},
							&core.PlaceC{Settlement: se.ID},
						)
					}
				}
				player := w.Spawn(&core.IdentC{Name: "player"}, &core.PlaceC{Settlement: first})
				EnsurePlayer(w, player)
				controlled := func(id core.ID) bool { return id == player.ID }
				var tracker HeatTracker
				tracker.Update(w, controlled) // warm up and establish a stable topology
				b.ReportAllocs()
				b.ResetTimer()
				for i := 0; i < b.N; i++ {
					if mode == "cached_tracker" {
						tracker.Update(w, controlled)
					} else {
						UpdateHeat(w, controlled)
					}
				}
			})
		}
	}
}

// BenchmarkTerrainBuildVsHeightQuery gives the baseline numbers behind the server-side Terrain
// cache (comms 0208, item B): NewTerrain walks every settlement once to collect anchors, so
// rebuilding it per stateJSON/HTTP request from every connected client cost O(P×settlements) per
// game tick instead of O(settlements) — the server now caches one Terrain per world/settlement-count
// (see server.(*Server).terrain). Height queries are NOT covered by that cache: Height's nearest-
// anchor search is a linear scan over every settlement on EVERY call (used by /api/terrain for every
// grid point in a chunk, e.g. 33×33 calls at step=8), so it stays O(settlements) per point regardless
// of how many times Terrain itself is rebuilt. That remains unaddressed — flagged for the next slice
// (e.g. a spatial index over anchors) once settlement counts grow enough to matter.
// Run: go test ./sim -run '^$' -bench BenchmarkTerrainBuildVsHeightQuery -benchmem
func BenchmarkTerrainBuildVsHeightQuery(b *testing.B) {
	for _, settlementCount := range []int{100, 1000, 10000} {
		w := core.NewWorld("terrain-bench")
		region := w.Spawn(&core.RegionC{Name: "region", Biome: "plain"})
		for i := 0; i < settlementCount; i++ {
			w.Spawn(&core.SettlementC{Name: "village", Region: region.ID,
				X: float64(100 + i%800), Y: float64(100 + (i/800)%800)})
		}
		b.Run(fmt.Sprintf("build/settlements_%d", settlementCount), func(b *testing.B) {
			b.ReportAllocs()
			b.ResetTimer()
			for i := 0; i < b.N; i++ {
				_ = NewTerrain(w)
			}
		})
		t := NewTerrain(w) // built once, as the server cache now does across calls (see comms 0208)
		b.Run(fmt.Sprintf("height_query/settlements_%d", settlementCount), func(b *testing.B) {
			b.ReportAllocs()
			b.ResetTimer()
			for i := 0; i < b.N; i++ {
				t.Height(float64(i%2000), float64((i/2000)%2000))
			}
		})
	}
}
