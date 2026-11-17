package worldmut

import (
	"testing"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Перерубленный лес редеет, и крупному зверю в нём жить негде; мана мёртвого места тянется к
// норме края. Всё в пределах и без скачков.
func TestDeforestationAndManaRegen(t *testing.T) {
	w := core.NewWorld("t")
	hub := dice.NewHub("t")
	reg := w.Spawn(&core.RegionC{Name: "край", Biome: "forest", Mana: 1.0})
	se := w.Spawn(&core.SettlementC{Name: "село", Region: reg.ID, Mana: 0.3,
		Store: map[string]float64{}})
	// лес перерублен: запас у самого дна
	fn := &econ.ResourceNodeC{Node: econ.NodeForest, Good: econ.GoodWood,
		Settlement: se.ID, Stock: 8, Cap: 400}
	w.Spawn(fn)
	w.Attach(se, battle.SeedFauna("forest", 1.0, hub.Stream("seed")))

	f := battle.Fauna(se)
	bear0 := f.Cap["bear"]

	day := core.Clock{TotalHours: 24} // граница суток — система работает раз в день
	for i := 0; i < 600; i++ {
		fn.Stock = 8 // держим лес выбитым (его продолжают рубить)
		System{}.Hour(w, day, hub)
	}

	if fn.Cap >= 400 {
		t.Fatalf("перерубленный лес должен редеть: cap=%.1f", fn.Cap)
	}
	if f.Cap["bear"] >= bear0 {
		t.Fatalf("в вырубленном лесу медведю негде жить — предел должен упасть: было=%.1f стало=%.1f",
			bear0, f.Cap["bear"])
	}
	if core.Settle(se).Mana <= 0.3 {
		t.Fatalf("мана мёртвого места должна тянуться к норме края: %.2f", core.Settle(se).Mana)
	}
	if core.Settle(se).Mana > 1.0 {
		t.Fatalf("мана не должна превысить норму края: %.2f", core.Settle(se).Mana)
	}
}

// Нетронутый лес зарастает к пределу, а не редеет.
func TestRestedForestRegrows(t *testing.T) {
	w := core.NewWorld("t2")
	hub := dice.NewHub("t2")
	reg := w.Spawn(&core.RegionC{Biome: "forest", Mana: 1.0})
	se := w.Spawn(&core.SettlementC{Region: reg.ID, Mana: 1.0, Store: map[string]float64{}})
	fn := &econ.ResourceNodeC{Node: econ.NodeForest, Good: econ.GoodWood,
		Settlement: se.ID, Stock: 300, Cap: 300}
	w.Spawn(fn)
	w.Attach(se, battle.SeedFauna("forest", 1.0, hub.Stream("seed")))
	day := core.Clock{TotalHours: 24}
	for i := 0; i < 400; i++ {
		fn.Stock = fn.Cap // лес полон, его не трогают
		System{}.Hour(w, day, hub)
	}
	if fn.Cap <= 300 {
		t.Fatalf("нетронутый лес должен зарастать: cap=%.1f", fn.Cap)
	}
}

// Выработанная шахта мертва насовсем.
func TestMineExhausts(t *testing.T) {
	w := core.NewWorld("t3")
	hub := dice.NewHub("t3")
	se := w.Spawn(&core.SettlementC{Store: map[string]float64{}})
	mn := &econ.ResourceNodeC{Node: econ.NodeMine, Good: econ.GoodOre,
		Settlement: se.ID, Stock: 0.4, Cap: 600}
	w.Spawn(mn)
	System{}.Hour(w, core.Clock{TotalHours: 24}, hub)
	if mn.Cap != 0 || mn.Stock != 0 {
		t.Fatalf("выработанная жила должна умереть: cap=%.1f stock=%.1f", mn.Cap, mn.Stock)
	}
}
