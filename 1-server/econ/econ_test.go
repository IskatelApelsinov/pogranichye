package econ

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// минимальный мир: поселение + (опц.) работник нужного ремесла.
func mini(store map[string]float64, role string, age float64) (*core.World, *core.Entity) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "Тест", Tier: "village", Store: store})
	if role != "" {
		w.Spawn(
			&core.IdentC{Name: "Р", Role: role, Sex: "м"},
			&core.VitalsC{Age: age, Health: 100},
			&core.PlaceC{Settlement: se.ID},
		)
	}
	return w, se
}

func dayHour(day int) core.Clock { return core.Clock{TotalHours: int64(day-1) * core.HoursPerDay} }

func run(w *core.World, day int) {
	hub := dice.NewHub("t")
	EconomySystem{}.Hour(w, dayHour(day), hub)
}

// Без пахаря пашня не засевается — хлеба не будет (сохранение: нет труда → нет блага).
func TestNoFarmerNoSowing(t *testing.T) {
	w, se := mini(map[string]float64{GoodSeed: 500}, "", 0)
	w.Spawn(&ResourceNodeC{Node: NodeField, Good: GoodWheat, Settlement: se.ID})
	run(w, 1) // день 1 — весна
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	if n.Sown {
		t.Fatal("без пахаря пашня не должна засеваться")
	}
}

// С пахарём весной сеют (семена убывают), осенью жнут (зерно-еда прибывает, сохранение).
func TestSowAndHarvest(t *testing.T) {
	store := map[string]float64{GoodSeed: 500}
	w, se := mini(store, "farmer", 30)
	w.Spawn(&ResourceNodeC{Node: NodeField, Good: GoodWheat, Settlement: se.ID})
	run(w, 1) // весна: сев
	seedAfterSow := store[GoodSeed]
	if seedAfterSow >= 500 {
		t.Fatalf("сев не израсходовал семена: %.0f", seedAfterSow)
	}
	// довести поле до спелости и сжать осенью
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	n.Ripe = 1
	run(w, 50) // осень: жатва
	if store[GoodWheat] <= 0 {
		t.Fatal("осенью должна быть жатва зерна")
	}
	// сохранение: урожай не больше, чем посеяно × макс. сам-N
	if store[GoodWheat] > seedPerField*yieldRatioMax+1 {
		t.Fatalf("урожай больше физически возможного: %.0f", store[GoodWheat])
	}
}

// Рыбное место закрыто в нерест (весна) и открыто летом — перелов невозможен весной.
func TestFisheryNerest(t *testing.T) {
	w, se := mini(map[string]float64{}, "fisher", 30)
	w.Spawn(&ResourceNodeC{Node: NodeFishery, Good: GoodFish, Settlement: se.ID, Stock: 100, Cap: 100})
	run(w, 1) // весна — нерест
	if core.Settle(se).Store[GoodFish] > 0 {
		t.Fatal("в нерест (весной) ловить нельзя")
	}
	run(w, 30) // лето — лов открыт
	if core.Settle(se).Store[GoodFish] <= 0 {
		t.Fatal("летом рыбу должны ловить")
	}
}

// Шахта конечна и НЕ возобновляется: запас только убывает, добыча не превышает запас.
func TestMineDepletes(t *testing.T) {
	w, se := mini(map[string]float64{}, "miner", 30)
	w.Spawn(&ResourceNodeC{Node: NodeMine, Good: GoodOre, Settlement: se.ID, Stock: 12, Cap: 600, Regen: 0})
	for d := 30; d < 60; d++ { // лето, много дней подряд
		run(w, d)
	}
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	if n.Stock > 0.01 {
		t.Fatalf("шахту должны были исчерпать, осталось %.2f", n.Stock)
	}
	// добыто не больше, чем было в жиле (сохранение)
	if core.Settle(se).Store[GoodOre] > 12.01 {
		t.Fatalf("добыто больше, чем было руды: %.2f", core.Settle(se).Store[GoodOre])
	}
}

// Семенное зерно НЕ едят ни при каком голоде — сев всегда обеспечен.
func TestSeedIsSacred(t *testing.T) {
	se := &core.Entity{ID: 1, Comps: map[string]core.Component{}}
	se.Add(&core.SettlementC{Store: map[string]float64{GoodSeed: 100}})
	for i := 0; i < 50; i++ {
		if Eat(se) {
			t.Fatal("семенное зерно нельзя есть")
		}
	}
	if core.Settle(se).Store[GoodSeed] != 100 {
		t.Fatalf("семена тронули: %.0f", core.Settle(se).Store[GoodSeed])
	}
}

// Торговля ПЕРЕМЕЩАЕТ зерно (с потерей на провоз), а не создаёт: сумма после ≤ суммы до.
func TestTradeConserves(t *testing.T) {
	w := core.NewWorld("t")
	rich := w.Spawn(&core.SettlementC{Name: "Богатое", Store: map[string]float64{GoodWheat: 1000}})
	poor := w.Spawn(&core.SettlementC{Name: "Бедное", Store: map[string]float64{GoodWheat: 0}})
	// по жителю в каждом, чтобы были «нужда» и «излишек»
	w.Spawn(&core.IdentC{Name: "A", Role: "farmer"}, &core.VitalsC{Age: 30}, &core.PlaceC{Settlement: rich.ID})
	w.Spawn(&core.IdentC{Name: "B", Role: "farmer"}, &core.VitalsC{Age: 30}, &core.PlaceC{Settlement: poor.ID})
	w.Spawn(&RealmC{Name: "Д", TaxRate: 0, Territory: []core.ID{rich.ID, poor.ID}})

	before := core.Settle(rich).Store[GoodWheat] + core.Settle(poor).Store[GoodWheat]
	TradeSystem{}.Hour(w, dayHour(30), dice.NewHub("t"))
	after := core.Settle(rich).Store[GoodWheat] + core.Settle(poor).Store[GoodWheat]

	if after > before+0.01 {
		t.Fatalf("торговля создала зерно из ниоткуда: было %.0f стало %.0f", before, after)
	}
	if core.Settle(poor).Store[GoodWheat] <= 0 {
		t.Fatal("бедное поселение должно было получить зерно по торговле")
	}
}
