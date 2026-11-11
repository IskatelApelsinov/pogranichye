package sim

import (
	"math"
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
		len(m.Route) != 2 || m.ProgressHours != 0 || m.StartedAtHour != 0 || m.FoodSupply != 5 {
		t.Fatalf("transit state and stable entity identity must survive save/load: %+v", m)
	}

	total := int(math.Ceil(m.TotalHours))
	for hour := 1; hour <= total; hour++ {
		clock := core.Clock{TotalHours: int64(hour)}
		(NeedsSystem{}).Hour(loaded, clock, nil)
		(MigrationSystem{}).Hour(loaded, clock, nil)
		if hour == 25 && m.FoodSupply != 4 {
			t.Fatalf("traveller must consume reserved bread from the travel supply: left=%d", m.FoodSupply)
		}
		if hour < total && Migration(person) == nil {
			t.Fatal("NPC must remain in transit until the full route duration elapses")
		}
	}
	if m.FoodSupply != 0 {
		t.Fatalf("hourly travel needs must consume the calculated rations, left=%d", m.FoodSupply)
	}
	if Migration(person) != nil || core.Place(person).Settlement != to.ID {
		t.Fatal("NPC must arrive at the recorded destination after route duration")
	}
	if person.ID != id {
		t.Fatal("migration must preserve the same entity identity")
	}
}

func TestMigrationStartTickDoesNotAdvanceTransitBeforeOneWorldHour(t *testing.T) {
	w := core.NewWorld("migration-start-hour")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain"})
	from := w.Spawn(&core.SettlementC{Name: "Исток", X: 0, Y: 0, Region: region.ID, Store: map[string]float64{econ.GoodBread: 10}})
	w.Spawn(&core.SettlementC{Name: "Перевал", X: 200, Y: 0, Region: region.ID})
	person := w.Spawn(
		&core.IdentC{Name: "Странник", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Hunger: 10, Energy: 80},
		&core.DrivesC{Ambition: 90},
		&core.MindC{Goal: "leave", GoalUntil: 10},
		&core.HomeC{Status: "owned", Settlement: from.ID},
		&core.PlaceC{Settlement: from.ID},
	)

	runner := core.NewRunner("migration-start-hour", NeedsSystem{}, MindSystem{}, MigrationSystem{})
	runner.Step(w)
	m := Migration(person)
	if m == nil || m.StartedAtHour != 0 || m.ProgressHours != 0 {
		t.Fatalf("migration created by MindSystem must not advance in its creation step: %+v", m)
	}

	runner.Step(w)
	if m.ProgressHours != 1 {
		t.Fatalf("the first full world hour should advance transit once, got %v", m.ProgressHours)
	}
}

func TestMigrationStartTickSkipsNeedsWhenTransitBeginsBeforeNeedsSystem(t *testing.T) {
	w := core.NewWorld("migration-start-before-needs")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain"})
	from := w.Spawn(&core.SettlementC{Name: "Исток", X: 0, Y: 0, Region: region.ID, Store: map[string]float64{econ.GoodBread: 10}})
	w.Spawn(&core.SettlementC{Name: "Перевал", X: 200, Y: 0, Region: region.ID})
	person := w.Spawn(
		&core.IdentC{Name: "Странник", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Hunger: 10, Energy: 80},
		&core.DrivesC{Ambition: 90},
		&core.MindC{Goal: "leave", GoalUntil: 10},
		&core.HomeC{Status: "owned", Settlement: from.ID},
		&core.PlaceC{Settlement: from.ID},
	)

	runner := core.NewRunner("migration-start-before-needs", MindSystem{}, NeedsSystem{}, MigrationSystem{})
	runner.Step(w)
	m := Migration(person)
	if m == nil || m.ProgressHours != 0 || core.Vitals(person).Hunger != 10 {
		t.Fatalf("a partial start tick must not advance travel or needs: migration=%+v hunger=%.1f", m, core.Vitals(person).Hunger)
	}

	runner.Step(w)
	if m.ProgressHours != 1 || core.Vitals(person).Hunger <= 10 {
		t.Fatalf("the next full hour must advance both travel and needs: progress=%.1f hunger=%.1f", m.ProgressHours, core.Vitals(person).Hunger)
	}
}

func TestMigrationRecoveryRestoresHomeToFallbackSettlement(t *testing.T) {
	w := core.NewWorld("migration-recovery-home")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain"})
	from := w.Spawn(&core.SettlementC{Name: "Исток", X: 0, Y: 0, Region: region.ID, Store: map[string]float64{econ.GoodBread: 10}})
	to := w.Spawn(&core.SettlementC{Name: "Перевал", X: 200, Y: 0, Region: region.ID})
	person := w.Spawn(
		&core.IdentC{Name: "Странник", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Hunger: 20, Energy: 80},
		&core.MindC{Goal: "leave", GoalUntil: 10},
		&core.HomeC{Status: "owned", Settlement: from.ID},
		&core.PlaceC{Settlement: from.ID},
	)
	if !startMigrationAt(w, person, to.ID, "test", 0) {
		t.Fatal("NPC with a connected destination must begin migration")
	}
	m := Migration(person)
	if m == nil || core.Home(person).Settlement != to.ID {
		t.Fatal("migration must remember the intended destination")
	}
	m.Route = []core.ID{from.ID} // simulate a corrupted saved itinerary
	w.Detach(person, "place") // recovery must also restore a missing resident-location component
	(MigrationSystem{}).Hour(w, core.Clock{TotalHours: 1}, nil)

	if Migration(person) != nil || core.Place(person) == nil || core.Place(person).Settlement != from.ID {
		t.Fatal("invalid route must recover the NPC to the original settlement")
	}
	if core.Home(person).Settlement != from.ID || core.Home(person).Status != "none" {
		t.Fatal("recovery must synchronize the relinquished home with the fallback settlement")
	}
	if core.Mind(person).Goal != "" {
		t.Fatal("recovered NPC must not immediately repeat the failed leave goal")
	}
	if got := core.Settle(from).Store[econ.GoodBread]; got != 10 {
		t.Fatalf("unused travel rations must return to the fallback settlement, got %.1f", got)
	}
}

func TestFoundingMigrationFoodUsesActualRoadDistance(t *testing.T) {
	w := core.NewWorld("founding-road-rations")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain"})
	points := make([]*core.Entity, 8)
	for i := range points {
		angle := -math.Pi/2 + float64(i)*math.Pi/4
		points[i] = w.Spawn(&core.SettlementC{
			Name: "Узел", X: 90 * math.Cos(angle), Y: 90 + 90*math.Sin(angle),
			Region: region.ID, Store: map[string]float64{econ.GoodBread: 0},
		})
	}
	from, to := points[0], points[4]
	band := make([]*core.Entity, 5)
	for i := range band {
		band[i] = w.Spawn(
			&core.IdentC{Name: "Переселенец", Role: "farmer"},
			&core.VitalsC{Age: 25, Health: 100, Hunger: 20, Energy: 80},
			&core.PlaceC{Settlement: from.ID},
		)
	}

	_, roadDistance := RoadRoute(RoadGraph(w, false), from.ID, to.ID)
	directUpperBound := math.Max(12, math.Hypot(180, 180))
	if roadDistance <= directUpperBound {
		t.Fatalf("fixture must force a road detour: road %.1f, direct bound %.1f", roadDistance, directUpperBound)
	}
	directFood := 0
	for _, traveller := range band {
		directFood += migrationMeals(traveller, directUpperBound)
	}
	roadFood, ok := migrationFoodNeededForGroup(w, from, to, band)
	if !ok {
		t.Fatal("a connected same-side road route must produce a group ration estimate")
	}
	if roadFood <= directFood {
		t.Fatalf("founding ration estimate must use the longer road route: direct=%d road=%d distance=%.1f", directFood, roadFood, roadDistance)
	}
}

