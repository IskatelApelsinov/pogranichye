package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

func TestHorseLoop(t *testing.T) {
	w, e, se := starterPlayer(t)
	core.Purse(e).Coins = 100
	if ok, _ := Horse(w, e, "buy", "", 0, "h0"); ok {
		t.Fatal("лошадей продают только у конюшни")
	}
	GoTo(w, e, stablePOI)
	stock := stableStock(core.Settle(se))
	if ok, why := Horse(w, e, "buy", "", 0, "h1"); !ok {
		t.Fatalf("купить: %s", why)
	}
	Horse(w, e, "buy", "", 0, "h1") // повтор reqid
	if core.Purse(e).Coins != 60 || core.Settle(se).Store[horseGood] != stock-1 {
		t.Fatal("лошадь куплена один раз: 40 монет и минус одна в конюшне")
	}
	if ok, _ := Horse(w, e, "buy", "", 0, "h2"); ok {
		t.Fatal("вторую лошадь не продают")
	}
	// седельная сумка отдельна от сумы всадника
	addStash(w, e, econ.GoodWood, 10)
	if ok, why := Horse(w, e, "stow", econ.GoodWood, 6, "h3"); !ok {
		t.Fatalf("сложить в седельную сумку: %s", why)
	}
	_, h := horseOf(w, e)
	if econ.Stash(e).Goods[econ.GoodWood] != 4 || h.Bag[econ.GoodWood] != 6 {
		t.Fatal("груз должен перейти в седельную сумку")
	}
	if ok, _ := Horse(w, e, "stow", econ.GoodWood, 99, "h4"); ok {
		t.Fatal("нельзя сложить больше, чем есть")
	}
	// верхом быстрее
	p := ScenePos(e)
	p.X, p.Y = 0, -60
	UpdateBurden(w, 0.1)
	ApplyMove(p, 1, 0, false, 0, 0.2, 1)
	foot := p.X
	Horse(w, e, "mount", "", 0, "h5")
	UpdateBurden(w, 0.1)
	p.X = 0
	ApplyMove(p, 1, 0, false, 0, 0.2, 2)
	if p.X <= foot*1.5 {
		t.Fatalf("верхом должно быть быстрее: пешком %.2f, верхом %.2f", foot, p.X)
	}
	Horse(w, e, "dismount", "", 0, "h6")
	// перегруженный пеший медленнее
	addStash(w, e, econ.GoodOre, 500)
	UpdateBurden(w, 0.1)
	p.X = 0
	ApplyMove(p, 1, 0, true, 0, 0.2, 3)
	if p.X >= foot {
		t.Fatal("с перегруженной сумой идут медленнее и не бегают")
	}
	if BagView(e)["over"] != true {
		t.Fatal("кадр должен показывать перегруз")
	}
}
