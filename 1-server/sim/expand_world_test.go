package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Новые края: три большие города и 50 деревень добавляются один раз; город — кварталы
// многоэтажных домов без реки, полей и леса, со множеством ремёсел; деревни — разного облика.
func TestExpandWorldAddsRealmsCitiesAndVariedVillages(t *testing.T) {
	w, hub := Generate("expand", 10)
	before := w.CountKind("settlement")
	if n := ExpandWorld(w, 0); n != 53 {
		t.Fatalf("добавлено %d поселений, ждали 53", n)
	}
	if ExpandWorld(w, 0) != 0 || w.CountKind("settlement") != before+53 {
		t.Fatal("повторный вызов не должен добавлять края")
	}
	var cities []*core.Entity
	w.Each("metropolis", func(se *core.Entity) { cities = append(cities, se) })
	if len(cities) != 3 {
		t.Fatalf("больших городов %d", len(cities))
	}
	for _, c := range cities {
		l := LayoutOf(w, c.ID)
		if l.Plan != "grid" || l.HasRiver || len(l.Zones) != 0 || l.Land.Water != "well" {
			t.Fatalf("%s: город должен быть без реки, полей и леса: plan=%s river=%v zones=%d", core.Settle(c).Name, l.Plan, l.HasRiver, len(l.Zones))
		}
		tall := 0
		for _, b := range l.Buildings {
			if b.Storeys >= 3 {
				tall++
			}
		}
		if len(l.Buildings) < 50 || tall < 30 {
			t.Fatalf("%s: застройка не плотная/не многоэтажная: домов %d, от 3 ярусов %d", core.Settle(c).Name, len(l.Buildings), tall)
		}
		roles := map[string]int{}
		for _, r := range residentsOf(w, c.ID) {
			roles[core.Ident(r).Role]++
		}
		if len(residentsOf(w, c.ID)) < 140 || len(roles) < 12 || roles["farmer"] > 0 || roles["fisher"] > 0 {
			t.Fatalf("%s: жителей %d, ремёсел %d, пахарей %d", core.Settle(c).Name, len(residentsOf(w, c.ID)), len(roles), roles["farmer"])
		}
		w.Each("resnode", func(ne *core.Entity) {
			if econ.Node(ne).Settlement == c.ID {
				t.Fatalf("%s: у города не должно быть угодий", core.Settle(c).Name)
			}
		})
		if r := realmNameOf(w, c.ID); r == "" {
			t.Fatalf("%s: город должен быть столицей державы", core.Settle(c).Name)
		}
	}
	// облик деревень разный
	water, fields, forest := map[string]bool{}, map[string]bool{}, map[string]bool{}
	w.Each("settlement", func(se *core.Entity) {
		if IsMetropolis(se) {
			return
		}
		l := LayoutOf(w, se.ID)
		water[l.Land.Water], fields[l.Land.Fields], forest[l.Land.Forest] = true, true, true
	})
	for _, need := range [][2]string{{"water", "river"}, {"water", "pond"}, {"water", "well"}, {"fields", "strip"}, {"fields", "yards"},
		{"forest", "side"}, {"forest", "ring"}} {
		m := map[string]map[string]bool{"water": water, "fields": fields, "forest": forest}[need[0]]
		if !m[need[1]] {
			t.Fatalf("ни одного села с обликом %s=%s: %v %v %v", need[0], need[1], water, fields, forest)
		}
	}
	// город живёт: месяц спустя народ на месте, еда есть, обозы ходили
	r := core.NewRunnerWith(hub, core.Clock{}, Default()...)
	caravans := 0
	for d := 0; d < 30; d++ {
		for h := 0; h < 24; h++ {
			r.Step(w)
		}
		caravans += w.CountKind("caravan")
	}
	for _, c := range cities {
		cs := core.Settle(c)
		if pop := len(residentsOf(w, c.ID)); pop < 120 || foodStock(cs.Store) < float64(pop) {
			t.Fatalf("%s вымирает: жителей %d, еды %.0f", cs.Name, pop, foodStock(cs.Store))
		}
	}
	if caravans == 0 {
		t.Fatal("за месяц ни одного обоза в города или из них")
	}
}

// Облик села не сдвигает его дворы: раскладка с теми же входами повторяется до метра.
func TestLandStyleDeterministic(t *testing.T) {
	w, _ := Generate("style", 12)
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		site := SiteOf(w, se)
		a := generateLayout(w.Seed, se.ID, s.Tier, site.Terrain, site, "street")
		b := generateLayout(w.Seed, se.ID, s.Tier, site.Terrain, site, "street")
		if a.Land != b.Land || len(a.Zones) != len(b.Zones) || len(a.Buildings) != len(b.Buildings) {
			t.Fatalf("%s: облик не повторяется", s.Name)
		}
	})
}
