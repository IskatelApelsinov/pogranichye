package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

func adult(w *core.World, sex string, set core.ID) *core.Entity {
	return w.Spawn(
		&core.IdentC{Sex: sex, Role: "farmer"},
		&core.VitalsC{Age: 30, Health: 100},
		&core.PlaceC{Settlement: set},
	)
}

// Без взрослых обоих полов пары не складываются — значит и детей не будет (саморегуляция).
func TestPairingNeedsBothSexes(t *testing.T) {
	w := core.NewWorld("k")
	var men []*core.Entity
	for i := 0; i < 4; i++ {
		men = append(men, adult(w, "м", 1))
	}
	byHome := map[core.ID][]*core.Entity{1: men}
	s := dice.NewStream(1)
	for i := 0; i < 80; i++ {
		pairUp(w, byHome, s)
	}
	for _, e := range men {
		if k := Kin(e); k != nil && k.Partner != 0 {
			t.Fatal("из одних мужчин пары складываться не должны")
		}
	}
}

// Мужчины и женщины в одном селе со временем сходятся в пары.
func TestPairingFormsCouples(t *testing.T) {
	w := core.NewWorld("k2")
	var res []*core.Entity
	for i := 0; i < 3; i++ {
		res = append(res, adult(w, "м", 1))
		res = append(res, adult(w, "ж", 1))
	}
	byHome := map[core.ID][]*core.Entity{1: res}
	s := dice.NewStream(2)
	for i := 0; i < 80; i++ {
		pairUp(w, byHome, s)
	}
	paired := 0
	for _, e := range res {
		if k := Kin(e); k != nil && k.Partner != 0 {
			paired++
		}
	}
	if paired == 0 {
		t.Fatal("в селе с мужчинами и женщинами должны сложиться пары")
	}
}

// Смерть партнёра делает человека вдовцом: связь очищается, и он снова свободен для пары.
func TestWidowClearsPartner(t *testing.T) {
	w := core.NewWorld("k3")
	a := adult(w, "м", 1)
	b := adult(w, "ж", 1)
	bond(w, a, b)
	if partnerAlive(w, a) == nil {
		t.Fatal("после образования пары партнёр должен находиться")
	}
	w.Remove(b.ID) // партнёрша покинула мир
	if partnerAlive(w, a) != nil {
		t.Fatal("после смерти партнёрши связь должна рваться")
	}
	if Kin(a).Partner != 0 {
		t.Fatal("вдовец должен снова стать свободным")
	}
}
