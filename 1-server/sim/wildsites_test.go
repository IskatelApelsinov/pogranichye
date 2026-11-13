package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// TestWildSitesDeterministic — одно и то же семя мира даёт одни и те же дикие места (детерминизм
// генерации, как и у самих сёл/краёв): запрос Кати "реалистично раскиданные" места не должен зависеть
// от неконтролируемой случайности.
func TestWildSitesDeterministic(t *testing.T) {
	w1, _ := Generate("seed-wild-1", 3)
	w2, _ := Generate("seed-wild-1", 3)

	collect := func(w *core.World) []map[string]any { return WildSitesView(w) }
	a, b := collect(w1), collect(w2)
	if len(a) == 0 {
		t.Fatal("ожидались дикие места при генерации мира, получено 0")
	}
	if len(a) != len(b) {
		t.Fatalf("разное число диких мест при одинаковом семени: %d vs %d", len(a), len(b))
	}
	for i := range a {
		if a[i]["name"] != b[i]["name"] || a[i]["wild_type"] != b[i]["wild_type"] ||
			a[i]["e"] != b[i]["e"] || a[i]["n"] != b[i]["n"] {
			t.Fatalf("дикое место #%d отличается между запусками с одним семенем: %v vs %v", i, a[i], b[i])
		}
	}
}

// TestWildSitesAvoidSettlements — дикие места не садятся поверх сёл (минимальная дистанция в единицах
// карты края), как и требовала Катя: "между поселениями", а не внутри них.
func TestWildSitesAvoidSettlements(t *testing.T) {
	w, _ := Generate("seed-wild-2", 4)

	var settleXY [][2]float64
	w.Each("settlement", func(e *core.Entity) {
		s := core.Settle(e)
		settleXY = append(settleXY, [2]float64{s.X, s.Y})
	})
	if len(settleXY) == 0 {
		t.Fatal("ожидались поселения")
	}

	var wildCount int
	w.Each("wildsite", func(e *core.Entity) {
		wildCount++
		ws := WildSite(e)
		for _, p := range settleXY {
			dx, dy := ws.X-p[0], ws.Y-p[1]
			if dx*dx+dy*dy < 35*35 {
				t.Fatalf("дикое место %q слишком близко к селу: (%v,%v) vs (%v,%v)", ws.Name, ws.X, ws.Y, p[0], p[1])
			}
		}
	})
	if wildCount == 0 {
		t.Fatal("ожидались дикие места при генерации мира, получено 0")
	}
}

// TestWildSitesHeightConvention — пещера уходит вниз, башня — вверх (запрос Кати про высоту).
func TestWildSitesHeightConvention(t *testing.T) {
	g := dice.NewStream(dice.Seed64("seed-wild-z", "check"))
	for i := 0; i < 50; i++ {
		if z := wildKindZ(g, "cave"); z >= 0 {
			t.Fatalf("пещера должна уходить вниз (Z<0), получено %v", z)
		}
	}
	for i := 0; i < 50; i++ {
		if z := wildKindZ(g, "tower"); z <= 0 {
			t.Fatalf("башня должна уходить вверх (Z>0), получено %v", z)
		}
	}
	if z := wildKindZ(g, "ruins"); z != 0 {
		t.Fatalf("прочие виды должны лежать по рельефу (Z=0), получено %v", z)
	}
}

// TestWorldManifestIncludesWildSites — /api/world отдаёт дикие места в служебной карте.
func TestWorldManifestIncludesWildSites(t *testing.T) {
	w, _ := Generate("seed-wild-3", 3)
	m := WorldManifest(w)
	ws, ok := m["wildsites"].([]map[string]any)
	if !ok {
		t.Fatalf("WorldManifest: ожидался ключ wildsites, получено %T", m["wildsites"])
	}
	if len(ws) == 0 {
		t.Fatal("WorldManifest: ожидались дикие места, получено 0")
	}
}

// TestOpenFrontierAddsWildSites — новое кольцо фронтира тоже получает дикие места, не только
// стартовый котёл: строим внешний (Outer) край с одним поселением и открываем следующее кольцо.
func TestOpenFrontierAddsWildSites(t *testing.T) {
	w := core.NewWorld("seed-wild-frontier")
	hub := dice.NewHub("seed-wild-frontier")
	g := hub.Stream("worldgen")

	outerRegion := w.Spawn(&core.RegionC{Name: "Край", Biome: "hills", X: 500, Y: 500, Mana: 1.0, Outer: true})
	se := NewSettlement(w, g, "Застава", outerRegion)
	ConfigureFrontier(w, DefaultFrontier())

	before := 0
	w.Each("wildsite", func(e *core.Entity) { before++ })
	if name := OpenFrontier(w, nil, se.ID); name == "" {
		t.Fatal("OpenFrontier должен открыть новый край для внешнего поселения")
	}
	after := 0
	w.Each("wildsite", func(e *core.Entity) { after++ })
	if after <= before {
		t.Fatalf("открытие фронтира должно добавить дикие места: было %d, стало %d", before, after)
	}
}
