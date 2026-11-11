package sim

import (
	"math"
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
func TestSiteCoordinatesUseSphericalProjectionAndStayDeterministic(t *testing.T) {
	build := func() map[string]SiteC {
		w, _ := GenerateStarter("site-geodesy-seed")
		planet := EnsurePlanet(w)
		projection := PlanetProjection{
			Origin: SurfacePoint{LatitudeDeg: planet.CauldronLat, LongitudeDeg: planet.CauldronLon},
			RadiusMeters: planet.RadiusKm * 1000,
		}
		out := map[string]SiteC{}
		w.Each("settlement", func(e *core.Entity) {
			settlement := core.Settle(e)
			site := SiteOf(w, e)
			if site.CoordVersion != SurfaceProjectionVersion {
				t.Errorf("%s coordinate version = %d", settlement.Name, site.CoordVersion)
			}
			local := LocalMeters{
				East: (settlement.X - 500) * planet.KmPerUnit * 1000,
				North: (settlement.Y - 500) * planet.KmPerUnit * 1000,
			}
			expected, ok := projection.Inverse(local)
			if !ok {
				t.Errorf("%s projection failed", settlement.Name)
				return
			}
			wantLat := math.Round(expected.LatitudeDeg*100) / 100
			wantLon := math.Round(expected.LongitudeDeg*100) / 100
			if site.Lat != wantLat || site.Lon != wantLon {
				t.Errorf("%s coordinates = (%.2f, %.2f), want (%.2f, %.2f)", settlement.Name, site.Lat, site.Lon, wantLat, wantLon)
			}
			out[settlement.Name] = *site
		})
		return out
	}
	first, second := build(), build()
	if len(first) != len(second) {
		t.Fatalf("settlement count changed: %d != %d", len(first), len(second))
	}
	for name, a := range first {
		b, ok := second[name]
		if !ok || a.Lat != b.Lat || a.Lon != b.Lon || a.State != b.State {
			t.Errorf("%s is not deterministic: first=%+v second=%+v", name, a, b)
		}
	}
}

func TestSiteCoordinatesMigrateLegacySiteAndUsePersistedStateCoords(t *testing.T) {
	w := core.NewWorld("site-migration")
	w.Spawn(&PlanetC{
		Name: "Тестовая планета", RadiusKm: 6000, CauldronLat: 0, CauldronLon: 179.9, KmPerUnit: 0.25,
		States: []State{
			{Name: "Дальняя держава", LatR: 0, LonR: 179.5},
			{Name: "За меридианом", LatR: 0, LonR: -179.8},
		},
	})
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "hills"})
	legacy := &SiteC{Lat: 12.34, Lon: 56.78, Climate: "cold", State: "Старая держава",
		Terrain: "hills", Features: map[string]bool{"mine": true}, Specialty: "руда и камень"}
	settlement := w.Spawn(&core.SettlementC{Name: "Пограничное", X: 500, Y: 500, Region: region.ID}, legacy)
	got := SiteOf(w, settlement)
	if got.CoordVersion != SurfaceProjectionVersion {
		t.Fatalf("legacy site was not migrated: version=%d", got.CoordVersion)
	}
	if got.Lat != 0 || got.Lon != 179.9 {
		t.Fatalf("migrated coordinates = (%.2f, %.2f), want (0, 179.9)", got.Lat, got.Lon)
	}
	if got.State != "За меридианом" {
		t.Fatalf("nearest state = %q, want antimeridian-near state; transient State.Lat/Lon are not persisted", got.State)
	}
	if got.Terrain != "hills" || !got.Features["mine"] || got.Specialty != "руда и камень" {
		t.Fatalf("migration changed authored site identity: %+v", got)
	}
	again := SiteOf(w, settlement)
	if again != got || again.CoordVersion != SurfaceProjectionVersion {
		t.Fatal("site migration was not idempotent")
	}
}
func TestOuterWorldSettlementsStayNearTheirOwnRegionAnchors(t *testing.T) {
	w, _ := GenerateStarter("outer-region-coordinates")
	regions := map[core.ID]bool{}
	outerSettlements := 0
	w.Each("settlement", func(e *core.Entity) {
		settlement := core.Settle(e)
		region := core.Region(w.Get(settlement.Region))
		if region == nil || !region.Outer {
			return
		}
		outerSettlements++
		regions[settlement.Region] = true
		if math.Abs(settlement.X-region.X) > 110.000001 || math.Abs(settlement.Y-region.Y) > 110.000001 {
			t.Errorf("outer settlement %s at (%.1f, %.1f) escaped region anchor (%.1f, %.1f)",
				settlement.Name, settlement.X, settlement.Y, region.X, region.Y)
		}
	})
	if outerSettlements < 20 {
		t.Fatalf("expected generated outer villages and cities, got %d settlements", outerSettlements)
	}
	if len(regions) < 4 {
		t.Fatalf("outer settlements collapsed into %d regions, want all four generated regions", len(regions))
	}
}

