package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

func TestNPCMigrationUsesRoadAndPreservesIdentityAcrossSaveLoad(t *testing.T) {
	w := core.NewWorld("npc-road-migration")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain"})
	from := w.Spawn(&core.SettlementC{Name: "Исток", X: 0, Y: 0, Region: region.ID, Store: map[string]float64{econ.GoodBread: 10}})
	to := w.Spawn(&core.SettlementC{Name: "Перевал", X: 200, Y: 0, Region: region.ID})
	person := w.Spawn(
		&core.IdentC{Name: "Странник", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Hunger: 10, Energy: 80},
		&core.DrivesC{Ambition: 90},
		&core.MindC{Goal: "leave"},
		&core.HomeC{Status: "owned", Settlement: from.ID},
		&core.PlaceC{Settlement: from.ID},
		&ScenePosC{X: 4, Y: 8, Stam: 1},
	)
	id := person.ID

	if !relocate(w, person, dice.NewStream(7)) {
		t.Fatal("NPC with a connected destination must begin migration")
	}
	m := Migration(person)
	if m == nil || m.From != from.ID || m.To != to.ID ||
		len(m.Route) != 2 || m.Route[0] != from.ID || m.Route[1] != to.ID || m.FoodSupply != 5 {
		t.Fatalf("migration must retain the actual road route: %+v", m)
	}
	if core.Place(person).Settlement != 0 {
		t.Fatal("traveller must not remain counted in the source or destination population")
	}
	if core.Settle(from).Store[econ.GoodBread] != 5 {
		t.Fatal("reserved travel food must be deducted from the source settlement stock")
	}
	if ScenePos(person) != nil {
		t.Fatal("traveller must not retain a stale physical scene position")
	}
	if len(residentsOf(w, from.ID)) != 0 || len(residentsOf(w, to.ID)) != 0 {
		t.Fatal("traveller must be absent from both settlement resident indexes")
	}
	if core.Home(person).Settlement != to.ID || core.Home(person).Status != "none" {
		t.Fatal("old housing must be relinquished and destination remembered")
	}

	wire, err := w.Dump(nil)
	if err != nil {
		t.Fatalf("dump world during transit: %v", err)
	}
	loaded := core.LoadWorld(wire)
	person = loaded.Get(id)
	m = Migration(person)
	if person == nil || m == nil || m.From != from.ID || m.To != to.ID ||
		len(m.Route) != 2 || m.ProgressHours != 0 || m.FoodSupply != 5 {
		t.Fatalf("transit state and stable entity identity must survive save/load: %+v", m)
	}

	total := int(m.TotalHours)
	for h := 0; h < total-1; h++ {
		(NeedsSystem{}).Hour(loaded, core.Clock{}, nil)
		(MigrationSystem{}).Hour(loaded, core.Clock{}, nil)
		if h == 24 && m.FoodSupply != 4 {
			t.Fatalf("traveller must consume reserved bread from the travel supply: left=%d", m.FoodSupply)
		}
	}
	if m.FoodSupply != 0 {
		t.Fatalf("hourly travel needs must consume the calculated rations, left=%d", m.FoodSupply)
	}
	if Migration(person) == nil || core.Place(person).Settlement != 0 {
		t.Fatal("NPC must remain in transit until the full route duration elapses")
	}
	(MigrationSystem{}).Hour(loaded, core.Clock{}, nil)
	if Migration(person) != nil || core.Place(person).Settlement != to.ID {
		t.Fatal("NPC must arrive at the recorded destination after route duration")
	}
	if person.ID != id {
		t.Fatal("migration must preserve the same entity identity")
	}
}
