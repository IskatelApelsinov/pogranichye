package sim

import (
	"testing"

	"pogranichye/core"
)

// TestAOINeighborsCrossBorder — у границы двух близких сёл (минимум по правилам генерации — 500 м,
// см. worldgen.go NewSettlement) житель соседнего села должен быть виден, если он физически
// БЛИЖЕ, чем житель своего же села, который стоит дальше (запрос владельца, comms 0179/0182/0196:
// AOI через границу, этап 3 RFC). Якорь села A = (0,0) м (X=500), якорь села B = (500,0) м (X=502,
// ровно минимальные 500 м — (502-500)*0.25*1000=500, т.к. PlanetC.KmPerUnit=0.25 фиксирован).
func TestAOINeighborsCrossBorder(t *testing.T) {
	w := core.NewWorld("seed-aoi")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain", X: 500, Y: 500, Mana: 1.0})

	a := w.Spawn(&core.SettlementC{Name: "А", X: 500, Y: 500, Region: region.ID})
	b := w.Spawn(&core.SettlementC{Name: "Б", X: 502, Y: 500, Region: region.ID}) // 500 м от A

	viewer := w.Spawn(&core.IdentC{Name: "Смотрящий"}, &core.PlaceC{Settlement: a.ID},
		&ScenePosC{X: 200, Y: 0, Facing: 0})
	nearSame := w.Spawn(&core.IdentC{Name: "ДальнийСвой"}, &core.PlaceC{Settlement: a.ID},
		&ScenePosC{X: 50, Y: 0, Facing: 0}) // мир (50,0) — 150 м от смотрящего
	crossBorder := w.Spawn(&core.IdentC{Name: "БлижнийЧужой"}, &core.PlaceC{Settlement: b.ID},
		&ScenePosC{X: -200, Y: 0, Facing: 0}) // мир (300,0) — 100 м от смотрящего: БЛИЖЕ своего!

	far := w.Spawn(&core.SettlementC{Name: "Далёкое", X: 700, Y: 500, Region: region.ID}) // 50 км от A
	w.Spawn(&core.IdentC{Name: "Далёкий"}, &core.PlaceC{Settlement: far.ID}, &ScenePosC{X: 0, Y: 0, Facing: 0})

	terr := NewTerrain(w)
	got := AOINeighbors(w, terr, viewer, a.ID, 24)

	if len(got) != 2 {
		var names []string
		for _, e := range got {
			names = append(names, core.Ident(e).Name)
		}
		t.Fatalf("ожидались ровно 2 соседа (свой + близкий через границу), получено %d: %v", len(got), names)
	}
	if got[0].ID != crossBorder.ID {
		t.Fatalf("ближайшим должен быть сосед из соседнего села (он физически ближе): получили первым %q",
			core.Ident(got[0]).Name)
	}
	if got[1].ID != nearSame.ID {
		t.Fatalf("вторым должен быть дальний свой житель: получили %q", core.Ident(got[1]).Name)
	}
}

// TestAOINeighborsNoNearbySettlements — мир без близких соседей (обычное дело — большинство сёл
// разнесены намного дальше aoiSettlementPrefilterM) должен давать РОВНО то же, что и раньше: только
// жителей своего села, по реальному расстоянию (совпадает с локальной сценой при общем якоре).
func TestAOINeighborsNoNearbySettlements(t *testing.T) {
	w := core.NewWorld("seed-aoi-lonely")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain", X: 500, Y: 500, Mana: 1.0})
	a := w.Spawn(&core.SettlementC{Name: "А", X: 500, Y: 500, Region: region.ID})
	far := w.Spawn(&core.SettlementC{Name: "Далёкое", X: 700, Y: 500, Region: region.ID})

	viewer := w.Spawn(&core.IdentC{Name: "Смотрящий"}, &core.PlaceC{Settlement: a.ID}, &ScenePosC{})
	own := w.Spawn(&core.IdentC{Name: "Свой"}, &core.PlaceC{Settlement: a.ID}, &ScenePosC{X: 10, Y: 0})
	w.Spawn(&core.IdentC{Name: "Далёкий"}, &core.PlaceC{Settlement: far.ID}, &ScenePosC{})

	terr := NewTerrain(w)
	got := AOINeighbors(w, terr, viewer, a.ID, 24)
	if len(got) != 1 || got[0].ID != own.ID {
		t.Fatalf("ожидался только свой житель, получено %d", len(got))
	}
}


// TestAOINeighborsIncludesPhysicalMigrantsBeyondAnchorPrefilter — a traveller's PlaceC belongs to
// the nearest endpoint, but its actual body may be several kilometres from that settlement anchor.
// Such a traveller must still appear when physically close to the viewer.
func TestAOINeighborsIncludesPhysicalMigrantsBeyondAnchorPrefilter(t *testing.T) {
	w := core.NewWorld("seed-aoi-migrant")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain", X: 500, Y: 500, Mana: 1})
	a := w.Spawn(&core.SettlementC{Name: "Ближнее", X: 500, Y: 500, Region: region.ID})
	b := w.Spawn(&core.SettlementC{Name: "Дальнее", X: 510, Y: 500, Region: region.ID}) // 2.5 км от A

	viewer := w.Spawn(&core.IdentC{Name: "Смотрящий"}, &core.PlaceC{Settlement: a.ID}, &ScenePosC{})
	migrant := w.Spawn(&core.IdentC{Name: "Путник"}, &core.PlaceC{Settlement: b.ID},
		&ScenePosC{X: -2500, Y: 0, Facing: 0},
		&MigrationC{From: b.ID, To: a.ID, Route: []core.ID{b.ID, a.ID}, TotalHours: 12})
	w.Spawn(&core.IdentC{Name: "Далёкий житель"}, &core.PlaceC{Settlement: b.ID}, &ScenePosC{})

	got := AOINeighbors(w, NewTerrain(w), viewer, a.ID, 24)
	if len(got) != 1 || got[0].ID != migrant.ID {
		t.Fatalf("физически близкий путник должен быть виден вне anchor-prefilter, получено %v", got)
	}
}
