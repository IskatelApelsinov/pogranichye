package sim

import (
	"fmt"
	"math"
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


// assertEverySettlementRegionHasWildSites verifies that no generated settlement region is left as
// empty wilderness. This is deliberately checked through the public world entities, not helper internals.
func assertEverySettlementRegionHasWildSites(t *testing.T, w *core.World) {
	t.Helper()
	regions := map[core.ID]bool{}
	counts := map[core.ID]int{}
	w.Each("settlement", func(e *core.Entity) {
		if s := core.Settle(e); s != nil && s.Region != 0 {
			regions[s.Region] = true
		}
	})
	w.Each("wildsite", func(e *core.Entity) {
		if ws := WildSite(e); ws != nil {
			counts[ws.Region]++
		}
	})
	if len(regions) == 0 {
		t.Fatal("ожидались регионы с поселениями")
	}
	for region := range regions {
		if counts[region] == 0 {
			t.Errorf("регион %d с поселениями остался без диких мест", region)
		}
	}
}

func TestGenerateStarterPopulatesEverySettlementRegionWithWildSites(t *testing.T) {
	w, _ := GenerateStarter("seed-starter-wildsites")
	assertEverySettlementRegionHasWildSites(t, w)
}

func TestGenerateStarterWildSitesAreDeterministic(t *testing.T) {
	w1, _ := GenerateStarter("seed-starter-wildsites-deterministic")
	w2, _ := GenerateStarter("seed-starter-wildsites-deterministic")
	a, b := WildSitesView(w1), WildSitesView(w2)
	if len(a) != len(b) || len(a) == 0 {
		t.Fatalf("ожидалось одинаковое ненулевое число диких мест: %d vs %d", len(a), len(b))
	}
	for i := range a {
		for _, key := range []string{"name", "wild_type", "region", "e", "n", "u"} {
			if a[i][key] != b[i][key] {
				t.Fatalf("дикое место #%d отличается по %s: %v vs %v", i, key, a[i][key], b[i][key])
			}
		}
	}
}


// TestWildSitesSeedMatrix — generation must keep wilderness populated and biome-plausible across
// multiple independent seeds, not only one hand-picked fixture.
func TestWildSitesSeedMatrix(t *testing.T) {
	allowed := map[string]map[string]bool{
		"hills":  {"cave": true, "ruins": true, "tower": true},
		"forest": {"grove": true, "ruins": true, "cave": true},
		"coast":  {"spring": true, "ruins": true},
		"plain":  {"tower": true, "ruins": true, "grove": true},
		"steppe": {"tower": true, "ruins": true},
	}
	for i := 0; i < 8; i++ {
		seed := fmt.Sprintf("wildsite-matrix-%02d", i)
		var worlds []*core.World
		starter, _ := GenerateStarter(seed)
		generated, _ := Generate(seed, 12+i)
		worlds = append(worlds, starter, generated)
		for worldIndex, world := range worlds {
			assertEverySettlementRegionHasWildSites(t, world)
			world.Each("wildsite", func(entity *core.Entity) {
				site := WildSite(entity)
				if site == nil || site.Region == 0 {
					t.Errorf("seed %s world %d: дикое место без компонента/региона", seed, worldIndex)
					return
				}
				region := core.Region(world.Get(site.Region))
				if region == nil {
					t.Errorf("seed %s world %d: регион дикого места %d отсутствует", seed, worldIndex, site.Region)
					return
				}
				if !allowed[region.Biome][site.Type] {
					t.Errorf("seed %s world %d: тип %q не подходит биому %q", seed, worldIndex, site.Type, region.Biome)
				}
				if math.IsNaN(site.X) || math.IsInf(site.X, 0) ||
					math.IsNaN(site.Y) || math.IsInf(site.Y, 0) ||
					math.IsNaN(site.Z) || math.IsInf(site.Z, 0) ||
					site.Mana < 0.1 || site.Mana > 3.0 ||
					site.Danger < 0 || site.Danger > 1 {
					t.Errorf("seed %s world %d: некорректные параметры дикого места: %+v", seed, worldIndex, site)
				}
			})
		}
	}
}
