package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// ROADMAP P0: «занятия-мини-игры на сервере: помощь в поле/кузнице как реальные действия» — проверяем,
// что Farmwork/Forge реально меняют мир (склад села), не удваивают фоновую экономику и платят игроку.

func TestFarmworkRequiresBeingAtTheField(t *testing.T) {
	w, e, _ := starterPlayer(t)
	if ok, why := Farmwork(w, e, core.Spring, "fw0"); ok || why == "" {
		t.Fatalf("без прихода к полю не должно получиться: ok=%t why=%q", ok, why)
	}
}

func TestFarmworkSowsInSpring(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "garden")
	s := core.Settle(se)
	before := s.Store[econ.GoodWheat]
	coinsBefore := core.Purse(e).Coins

	ok, why := Farmwork(w, e, core.Spring, "fw1")
	if !ok {
		t.Fatalf("сев должен получиться: %s", why)
	}
	if got := before - s.Store[econ.GoodWheat]; got != econ.SeedPerField() {
		t.Fatalf("засев должен снять %v зерна со склада, снял %v", econ.SeedPerField(), got)
	}
	var sown int
	w.Each("resnode", func(ne *core.Entity) {
		n := econ.Node(ne)
		if n != nil && n.Node == econ.NodeField && n.Settlement == se.ID && n.Sown {
			sown++
		}
	})
	if sown != 1 {
		t.Fatalf("ровно одна пашня должна стать засеянной, стало %d", sown)
	}
	if core.Purse(e).Coins <= coinsBefore {
		t.Fatal("за труд в поле должна быть выплата (econ.Wage)")
	}

	// Повтор того же reqid идемпотентен — повторного списания зерна нет.
	afterFirst := s.Store[econ.GoodWheat]
	if ok, why := Farmwork(w, e, core.Spring, "fw1"); !ok {
		t.Fatalf("повтор reqid должен быть идемпотентным ok=true: %s", why)
	}
	if s.Store[econ.GoodWheat] != afterFirst {
		t.Fatal("повтор того же reqid не должен снова списывать зерно")
	}
}

func TestFarmworkHasNothingToDoOutOfSeason(t *testing.T) {
	w, e, _ := starterPlayer(t)
	GoTo(w, e, "garden")
	if ok, why := Farmwork(w, e, core.Summer, "fw2"); ok || why == "" {
		t.Fatalf("летом в поле у игрока нет действия (сев/жатва не летом): ok=%t why=%q", ok, why)
	}
}

func TestFarmworkReapsRipeFieldInAutumn(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "garden")
	s := core.Settle(se)

	// Подготовка: одна пашня засеяна и вызрела (как к осени у фоновой экономики).
	var field *econ.ResourceNodeC
	w.Each("resnode", func(ne *core.Entity) {
		n := econ.Node(ne)
		if field == nil && n != nil && n.Node == econ.NodeField && n.Settlement == se.ID && !n.Fallow {
			field = n
		}
	})
	if field == nil {
		t.Fatal("должна быть хоть одна небросовая пашня")
	}
	field.Sown, field.SownSeed, field.Ripe = true, econ.SeedPerField(), 0.9
	before := s.Store[econ.GoodWheat]

	ok, why := Farmwork(w, e, core.Autumn, "fw3")
	if !ok {
		t.Fatalf("жатва должна получиться: %s", why)
	}
	got := s.Store[econ.GoodWheat] - before
	want := econ.SeedPerField() * econ.AverageHarvestRatio()
	if got != want {
		t.Fatalf("урожай должен быть сам-N от посеянного: получили %v, ждали %v", got, want)
	}
	if field.Sown {
		t.Fatal("убранное поле должно освободиться (Sown=false) — повторная жатва не задвоится")
	}
}

func TestForgeRequiresSmithyAndOre(t *testing.T) {
	w, e, se := starterPlayer(t)
	if ok, why := Forge(w, e, "fg0"); ok || why == "" {
		t.Fatalf("без прихода в кузню не должно получиться: ok=%t why=%q", ok, why)
	}
	GoTo(w, e, "smithy")
	s := core.Settle(se)
	s.Store[econ.GoodOre] = 1 // меньше forgeOreCost
	if ok, why := Forge(w, e, "fg1"); ok || why == "" {
		t.Fatalf("без руды на складе не должно получиться: ok=%t why=%q", ok, why)
	}
}

func TestForgeSmeltsOreForTheSettlement(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "smithy")
	s := core.Settle(se)
	s.Store[econ.GoodOre] = 40 // старт Починка (см. GenerateStarter)
	oreBefore, metalBefore := s.Store[econ.GoodOre], s.Store[econ.GoodMetal]
	coinsBefore := core.Purse(e).Coins

	ok, why := Forge(w, e, "fg2")
	if !ok {
		t.Fatalf("плавка должна получиться: %s", why)
	}
	if oreBefore-s.Store[econ.GoodOre] != forgeOreCost {
		t.Fatalf("должно уйти %v руды со склада, ушло %v", forgeOreCost, oreBefore-s.Store[econ.GoodOre])
	}
	if got, want := s.Store[econ.GoodMetal]-metalBefore, forgeOreCost*econ.MetalFromOre(); got != want {
		t.Fatalf("выход металла должен быть %v, получили %v", want, got)
	}
	if core.Purse(e).Coins <= coinsBefore {
		t.Fatal("за труд у горна должна быть выплата (econ.Wage)")
	}
	if sk := core.Skills(e); sk == nil || sk.Level("smith") <= 0 {
		t.Fatal("подмога в кузне должна растить навык smith")
	}

	// Идемпотентность: повтор reqid не плавит руду второй раз.
	oreAfterFirst := s.Store[econ.GoodOre]
	if ok, why := Forge(w, e, "fg2"); !ok {
		t.Fatalf("повтор reqid должен быть идемпотентным ok=true: %s", why)
	}
	if s.Store[econ.GoodOre] != oreAfterFirst {
		t.Fatal("повтор того же reqid не должен снова плавить руду")
	}
}
