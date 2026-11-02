package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

func spawnRealm(w *core.World, name string, x, y float64) *core.Entity {
	cap := w.Spawn(&core.SettlementC{Name: name + "-столица", X: x, Y: y, Store: map[string]float64{}})
	return w.Spawn(&econ.RealmC{Name: name, Capital: cap.ID,
		Territory: []core.ID{cap.ID}, Relations: map[core.ID]float64{}})
}

// Война точит вражду; затем долгий мир роднит до союза. Дипломатия складывается из дел.
func TestDiplomacyWarThenPeace(t *testing.T) {
	w := core.NewWorld("dip")
	hub := dice.NewHub("dip")
	a := spawnRealm(w, "A", 0, 0)
	b := spawnRealm(w, "B", 100, 0)
	ra, rb := econ.Realm(a), econ.Realm(b)
	day := core.Clock{TotalHours: 24}

	// воюют 20 дней → стояние уходит в минус
	ra.WarWith, rb.WarWith = b.ID, a.ID
	for i := 0; i < 20; i++ {
		DiplomacySystem{}.Hour(w, day, hub)
	}
	if ra.Stand(b.ID) >= 0 {
		t.Fatalf("после войны стояние должно быть отрицательным: %.1f", ra.Stand(b.ID))
	}

	// замирились; долгий мир теплеет до союза
	ra.WarWith, rb.WarWith = 0, 0
	ra.Relations[b.ID] = 0 // обиды улеглись (для чистоты теста)
	rb.Relations[a.ID] = 0
	allied := false
	for i := 0; i < 500 && !allied; i++ {
		DiplomacySystem{}.Hour(w, day, hub)
		allied = econ.Allied(ra, b.ID)
	}
	if !allied {
		t.Fatalf("долгий мир должен привести к союзу: %.1f", ra.Stand(b.ID))
	}
}

// Враги (глубоко отрицательное стояние) не теплеют сразу — сперва должны остыть.
func TestRivalsDoNotWarmImmediately(t *testing.T) {
	w := core.NewWorld("dip2")
	hub := dice.NewHub("dip2")
	a := spawnRealm(w, "A", 0, 0)
	b := spawnRealm(w, "B", 100, 0)
	ra, rb := econ.Realm(a), econ.Realm(b)
	ra.Relations[b.ID] = -60 // глубокая вражда
	rb.Relations[a.ID] = -60
	day := core.Clock{TotalHours: 24}
	for i := 0; i < 10; i++ {
		DiplomacySystem{}.Hour(w, day, hub)
	}
	if !econ.IsRival(ra, b.ID) {
		t.Fatalf("глубокие враги не должны быстро теплеть: %.1f", ra.Stand(b.ID))
	}
}

// На союзника войной не идут: если единственный сосед — союзник, цели для войны нет.
func TestWarSkipsAllies(t *testing.T) {
	w := core.NewWorld("dip3")
	a := spawnRealm(w, "A", 0, 0)
	b := spawnRealm(w, "B", 100, 0)
	econ.Realm(a).Relations[b.ID] = 80 // союзник
	realms := []*core.Entity{a, b}
	if foe := nearestPeacefulRealm(w, realms, a); foe != nil {
		t.Fatal("на союзника войной идти нельзя — цели быть не должно")
	}
	// а на нейтрала/врага — можно
	econ.Realm(a).Relations[b.ID] = -10
	if foe := nearestPeacefulRealm(w, realms, a); foe == nil {
		t.Fatal("на не-союзника цель должна находиться")
	}
}
