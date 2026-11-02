package econ

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

func worker(w *core.World, role string, se core.ID) {
	w.Spawn(
		&core.IdentC{Role: role, Sex: "м"},
		&core.VitalsC{Age: 30, Health: 100},
		&core.PlaceC{Settlement: se},
	)
}

// Углублённые цепочки: руда → металл → инструмент (кузнец) и зерно → мука → хлеб (повар).
// Проверяем, что переделы идут и конечный товар появляется (металл/мука — реальные промежутки).
func TestDeepCraftChains(t *testing.T) {
	w := core.NewWorld("craft")
	hub := dice.NewHub("craft")
	se := w.Spawn(&core.SettlementC{Name: "Кузня", Tier: "village",
		Store: map[string]float64{GoodOre: 30, GoodWheat: sowReserve + 120}})
	worker(w, "smith", se.ID)
	worker(w, "cook", se.ID)

	day := core.Clock{TotalHours: 24}
	for i := 0; i < 10; i++ {
		EconomySystem{}.Hour(w, day, hub)
	}
	st := core.Settle(se).Store
	if st[GoodTools] <= 0 {
		t.Fatalf("кузнец должен выковать инструмент по цепочке руда→металл→инструмент: tools=%.2f", st[GoodTools])
	}
	if st[GoodBread] <= 0 {
		t.Fatalf("повар должен испечь хлеб по цепочке зерно→мука→хлеб: bread=%.2f", st[GoodBread])
	}
	// руда реально тратится (не из воздуха)
	if st[GoodOre] >= 30 {
		t.Fatalf("руда должна убывать при плавке: ore=%.2f", st[GoodOre])
	}
}
