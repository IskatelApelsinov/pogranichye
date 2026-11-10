package sim

import (
	"testing"

	"pogranichye/core"
)

func TestSitesDifferAndGateWork(t *testing.T) {
	w, e, se := starterPlayer(t)
	home := SiteOf(w, se)
	for _, f := range []string{"river", "woods", "mine", "garden"} {
		if !home.Features[f] {
			t.Fatalf("в учебной деревне должно быть всё, нет %s", f)
		}
	}
	terr := map[string]bool{}
	var noRiver *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		c := SiteOf(w, o)
		if c.Cauldron {
			terr[c.Terrain] = true
			if !c.Features["river"] && noRiver == nil {
				noRiver = o
			}
		}
		if c.State == "" || c.Climate == "" || len(c.Features) == 0 {
			t.Fatalf("у места должны быть держава, климат и хоть одно угодье: %+v", c)
		}
	})
	if len(terr) < 3 {
		t.Fatalf("деревни котла должны быть разными: %v", terr)
	}
	if p := EnsurePlanet(w); len(p.States) != 20 || p.Name != "Ёф" {
		t.Fatal("планета: двадцать держав")
	}
	if a, b := SiteOf(w, se), SiteOf(w, se); a != b {
		t.Fatal("место создаётся один раз")
	}
	if noRiver == nil {
		t.Skip("во всех деревнях котла есть река")
	}
	core.Place(e).Settlement = noRiver.ID
	GoTo(w, e, "river")
	core.Vitals(e).Energy = 100
	if ok, why := FishCatch(w, e, "pf1"); ok || why == "" {
		t.Fatal("где нет реки, рыбу не ловят")
	}
}
