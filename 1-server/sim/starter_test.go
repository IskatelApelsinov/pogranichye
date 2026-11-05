package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Стартовая деревня: один предсказуемый самодостаточный мирок с полным составом, который живёт.
func TestStarterVillage(t *testing.T) {
	w, hub := GenerateStarter("старт")

	settlements, realms, people := 0, 0, 0
	roles := map[string]int{}
	w.Each("settlement", func(*core.Entity) { settlements++ })
	w.Each("realm", func(*core.Entity) { realms++ })
	w.Each("ident", func(e *core.Entity) {
		people++
		roles[core.Ident(e).Role]++
	})
	if settlements < 1 || realms < 1 {
		t.Fatalf("старт — центр мира и соседи: сёл=%d держав=%d", settlements, realms)
	}
	if people < 30 {
		t.Fatalf("ожидали населённый старт (центр + соседние деревни), получили %d", people)
	}
	for _, need := range []string{"farmer", "cook", "smith", "herbalist", "tavernkeeper", "miner", "hunter", "herder"} {
		if roles[need] == 0 {
			t.Fatalf("в стартовом составе должно быть ремесло %q", need)
		}
	}
	// у деревни есть хозяин и шахта (кузнецу/рудокопу есть с чем работать)
	hasMine := false
	w.Each("resnode", func(e *core.Entity) {
		if econ.Node(e).Node == econ.NodeMine {
			hasMine = true
		}
	})
	if !hasMine {
		t.Fatal("в стартовой деревне должна быть шахта (для кузнеца/рудокопа)")
	}
	var se *core.Entity
	w.Each("settlement", func(e *core.Entity) { se = e })
	if core.Settle(se).Keeper == 0 {
		t.Fatal("у стартовой деревни должен быть хозяин (keeper)")
	}

	// мир живёт: прокрутим ~1000 дней, деревня не вымирает
	r := core.NewRunnerWith(hub, core.Clock{}, Default()...)
	r.Run(w, 1000*core.HoursPerDay)
	alive := 0
	w.Each("ident", func(*core.Entity) { alive++ })
	if alive == 0 {
		t.Fatal("стартовая деревня не должна вымирать")
	}
}
