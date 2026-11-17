package econ

import (
	"testing"

	"pogranichye/core"
)

// Работник уносит своё добытое домой со склада-рынка: добро переходит в личные закрома (сохранение),
// кладовая не переполняется, а общинный посевной резерв по домам не растаскивают.
func TestTakeHomeMovesGoods(t *testing.T) {
	w := core.NewWorld("st")
	se := w.Spawn(&core.SettlementC{Store: map[string]float64{GoodFish: 50}})
	e := w.Spawn(&core.IdentC{Role: "fisher"}, &core.PlaceC{Settlement: se.ID})

	before := store(se)[GoodFish]
	TakeHome(se, e, GoodFish)
	s := Stash(e)
	if s == nil || s.Goods[GoodFish] <= 0 {
		t.Fatal("рыбак должен унести рыбу домой")
	}
	if store(se)[GoodFish]+s.Goods[GoodFish] != before {
		t.Fatal("сохранение нарушено: сумма склада и закромов должна остаться прежней")
	}

	// кладовая не переполняется сверх предела
	for i := 0; i < 50; i++ {
		TakeHome(se, e, GoodFish)
	}
	if stashTotal(s) > larderCap+0.001 {
		t.Fatalf("кладовая не должна превышать предел: %.1f", stashTotal(s))
	}

	// зерно ниже посевного резерва по домам не растаскивают
	se2 := w.Spawn(&core.SettlementC{Store: map[string]float64{GoodWheat: sowReserve - 10}})
	e2 := w.Spawn(&core.IdentC{Role: "farmer"}, &core.PlaceC{Settlement: se2.ID})
	TakeHome(se2, e2, GoodWheat)
	if st := Stash(e2); st != nil && st.Goods[GoodWheat] > 0 {
		t.Fatal("посевной резерв общины не уносят по личным закромам")
	}
}

// Сперва едят из своих закромов; пусто — нечего есть из своего.
func TestEatFromStash(t *testing.T) {
	w := core.NewWorld("st2")
	e := w.Spawn(&core.IdentC{Role: "hunter"})
	if EatFromStash(e) {
		t.Fatal("из пустых закромов есть нечего")
	}
	s := ensureStash(e)
	s.Goods[GoodMeat] = 2
	if !EatFromStash(e) {
		t.Fatal("должен поесть своё мясо")
	}
	if s.Goods[GoodMeat] != 1 {
		t.Fatalf("после еды мяса должно стать на меру меньше: %.1f", s.Goods[GoodMeat])
	}
}
