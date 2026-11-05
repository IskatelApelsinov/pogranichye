package sim

import (
	"testing"

	"pogranichye/core"
)

// villagerAt ставит жителя с ролью в точке сцены.
func villagerAt(w *core.World, role string, x, y float64, set core.ID) *core.Entity {
	e := w.Spawn(
		&core.IdentC{Name: "Истома", Role: role, Sex: "м"},
		&core.MindC{Goal: "rest", LastAct: "отдыхает"},
		&core.VitalsC{Morale: 50, Health: 100},
		&core.PlaceC{Settlement: set},
	)
	w.Attach(e, &ScenePosC{X: x, Y: y, Stam: 1})
	return e
}

// player — тело игрока с монетами в точке сцены.
func playerAt(w *core.World, x, y, coins float64, set core.ID) *core.Entity {
	e := w.Spawn(
		&core.IdentC{Name: "Игрок", Role: "farmer", Sex: "м"},
		&core.VitalsC{Morale: 50, Health: 100},
		&core.PurseC{Coins: coins, Debts: map[string]float64{}},
		&core.PlaceC{Settlement: set},
	)
	w.Attach(e, &ScenePosC{X: x, Y: y, Stam: 1})
	return e
}

// Знакомство открывает имя: пока не поговорил — аноним, после "greet" — Knows=true.
func TestAcquaintReveal(t *testing.T) {
	w := core.NewWorld("t")
	tx, ty := poiByKind("tavern")
	p := playerAt(w, tx, ty, 10, 100)
	npc := villagerAt(w, "fisher", tx+1, ty, 100)
	if Knows(p, npc.ID) {
		t.Fatal("до разговора игрок не знает жителя")
	}
	if DescFor("fisher") != "рыбак" {
		t.Fatalf("обезличенное описание неверно: %s", DescFor("fisher"))
	}
	if ok, why := StartTalk(w, p, npc.ID); !ok {
		t.Fatalf("заговорить рядом должно получиться: %s", why)
	}
	if ok, _ := Say(w, p, "greet"); !ok {
		t.Fatal("реплика знакомства должна пройти")
	}
	if !Knows(p, npc.ID) {
		t.Fatal("после знакомства имя должно быть открыто")
	}
}

// Далеко — заговорить нельзя.
func TestTalkTooFar(t *testing.T) {
	w := core.NewWorld("t")
	p := playerAt(w, 0, 0, 10, 100)
	npc := villagerAt(w, "smith", 40, 40, 100)
	if ok, _ := StartTalk(w, p, npc.ID); ok {
		t.Fatal("издалека заговорить нельзя")
	}
}

// Угощение знакомого в корчме: платит игрок, у НПС растёт настрой, пиво убывает.
func TestTreatNPC(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	p := playerAt(w, tx, ty, 10, se.ID)
	npc := villagerAt(w, "fisher", tx+1, ty, se.ID)
	StartTalk(w, p, npc.ID)
	Say(w, p, "greet")
	before := core.Vitals(npc).Morale
	coinsBefore := core.Purse(p).Coins
	if ok, why := Say(w, p, "drink"); !ok {
		t.Fatalf("угостить знакомого в корчме должно получиться: %s", why)
	}
	if core.Vitals(npc).Morale <= before {
		t.Fatal("угощение должно поднять настрой НПС")
	}
	if core.Purse(p).Coins >= coinsBefore {
		t.Fatal("игрок должен заплатить за угощение")
	}
	if core.Settle(se).Store[BeerGood] != 4 {
		t.Fatal("две кружки должны уйти из корчмы")
	}
}

// Нельзя угостить незнакомого (сперва знакомство).
func TestTreatRequiresAcquaint(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	p := playerAt(w, tx, ty, 10, se.ID)
	npc := villagerAt(w, "fisher", tx+1, ty, se.ID)
	StartTalk(w, p, npc.ID)
	if ok, _ := Say(w, p, "drink"); ok {
		t.Fatal("незнакомого угощать нельзя")
	}
}

// Co-drink двух игроков: приглашение + согласие → оба платят, веселеют, знакомятся.
func TestCoDrink(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, se.ID)
	b := playerAt(w, tx+1, ty, 10, se.ID)
	ctl := func(id core.ID) bool { return id == a.ID || id == b.ID }
	if ok, why := InviteCoDrink(w, a, b.ID, ctl); !ok {
		t.Fatalf("приглашение должно пройти: %s", why)
	}
	if CoDrink(b).State != "in" {
		t.Fatal("у приглашённого должно быть приглашение")
	}
	mb := core.Vitals(b).Morale
	if ok, why := AcceptCoDrink(w, b); !ok {
		t.Fatalf("согласие должно пройти: %s", why)
	}
	if core.Vitals(b).Morale <= mb {
		t.Fatal("после совместной кружки настрой растёт")
	}
	if core.Settle(se).Store[BeerGood] != 4 {
		t.Fatal("две кружки должны уйти")
	}
	if !Knows(a, b.ID) || !Knows(b, a.ID) {
		t.Fatal("выпив вместе, игроки знакомятся")
	}
	if CoDrink(b).State != "" {
		t.Fatal("после питья приглашение снимается")
	}
}

// Нельзя звать не-игрока (НПС) на co-drink.
func TestCoDrinkOnlyPlayers(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, se.ID)
	npc := villagerAt(w, "fisher", tx+1, ty, se.ID)
	ctl := func(id core.ID) bool { return id == a.ID }
	if ok, _ := InviteCoDrink(w, a, npc.ID, ctl); ok {
		t.Fatal("на co-drink зовут только игрока, не НПС")
	}
}

// Разрыв связи снимает приглашение у обоих.
func TestClearCoDrink(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, se.ID)
	b := playerAt(w, tx+1, ty, 10, se.ID)
	InviteCoDrink(w, a, b.ID, func(id core.ID) bool { return true })
	ClearCoDrink(w, a) // a «отключился»
	if CoDrink(a).State != "" || CoDrink(b).State != "" {
		t.Fatal("разрыв должен снять приглашение у обоих")
	}
}
