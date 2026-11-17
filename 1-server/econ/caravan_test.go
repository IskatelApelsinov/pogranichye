package econ

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// Караван выходит из столицы с излишком товара и довозит его в столицу другой державы, где
// товара мало; выручка идёт в казну продавца. Присутствие товара в чужом краю — итог реального
// пути, а не телепорта.
func TestCaravanCarriesSurplusToDeficit(t *testing.T) {
	w := core.NewWorld("car")
	hub := dice.NewHub("car")
	capA := w.Spawn(&core.SettlementC{Name: "A", X: 0, Y: 0, Mana: 0.4, Coin: 2000,
		Store: map[string]float64{GoodLeather: 300}})
	capB := w.Spawn(&core.SettlementC{Name: "B", X: 150, Y: 0, Mana: 0.4, Coin: 2000,
		Store: map[string]float64{}})
	rA := w.Spawn(&RealmC{Name: "A", Capital: capA.ID, Territory: []core.ID{capA.ID}})
	w.Spawn(&RealmC{Name: "B", Capital: capB.ID, Territory: []core.ID{capB.ID}})

	beforeDispatched := DebugCaravans
	beforeArrived := DebugArrived
	day := core.Clock{TotalHours: 24}
	for i := 0; i < 300; i++ {
		CaravanSystem{}.Hour(w, day, hub)
	}

	if DebugCaravans <= beforeDispatched {
		t.Fatal("держава с излишком должна была снарядить хоть один караван")
	}
	if DebugArrived <= beforeArrived {
		t.Fatal("хоть один караван должен был дойти")
	}
	if core.Settle(capB).Store[GoodLeather] <= 0 {
		t.Fatal("товар должен был прибыть на рынок столицы-получателя")
	}
	if Realm(rA).Treasury <= 0 {
		t.Fatalf("выручка должна идти в казну продавца: %.1f", Realm(rA).Treasury)
	}
}

// На лихой (высокомана) дороге обозы грабят чаще, чем на спокойной.
func TestBanditsHitDangerousRoads(t *testing.T) {
	robbedSafe := countRobbedOnRoad(t, 0.3)
	robbedWild := countRobbedOnRoad(t, 1.9)
	if robbedWild <= robbedSafe {
		t.Fatalf("на лихой дороге грабят чаще: спокойная=%d лихая=%d", robbedSafe, robbedWild)
	}
}

func countRobbedOnRoad(t *testing.T, mana float64) int {
	w := core.NewWorld("rob")
	hub := dice.NewHub("rob")
	capA := w.Spawn(&core.SettlementC{Name: "A", X: 0, Y: 0, Mana: mana, Coin: 100000,
		Store: map[string]float64{GoodLeather: 100000}})
	capB := w.Spawn(&core.SettlementC{Name: "B", X: 400, Y: 0, Mana: mana, Coin: 100000,
		Store: map[string]float64{}})
	w.Spawn(&RealmC{Name: "A", Capital: capA.ID, Territory: []core.ID{capA.ID}})
	w.Spawn(&RealmC{Name: "B", Capital: capB.ID, Territory: []core.ID{capB.ID}})
	before := DebugRobbed
	day := core.Clock{TotalHours: 24}
	for i := 0; i < 500; i++ {
		core.Settle(capB).Store[GoodLeather] = 0 // получатель всё потребляет — нужда держится, обозы идут потоком
		CaravanSystem{}.Hour(w, day, hub)
	}
	return DebugRobbed - before
}
