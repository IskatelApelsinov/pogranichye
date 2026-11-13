package sim

import (
	"testing"

	"pogranichye/core"
)

// neighborOf finds a settlement directly road-adjacent to se (a single-hop RoadRoute), using the
// same RoadGraph a real migration would route through — so the test exercises the same "direct,
// adjacent" case StepMigrants restricts itself to.
func neighborOf(t *testing.T, w *core.World, se core.ID) core.ID {
	t.Helper()
	roads := RoadGraph(w, isOuter(w, core.Settle(w.Get(se))))
	for _, r := range roads {
		if r.A == se {
			if route, _ := RoadRoute(roads, se, r.B); len(route) == 2 {
				return r.B
			}
		}
		if r.B == se {
			if route, _ := RoadRoute(roads, se, r.A); len(route) == 2 {
				return r.A
			}
		}
	}
	t.Fatal("стартовый мир должен иметь хотя бы один прямой дорожный перегон у Починка")
	return 0
}

func spawnMigrant(w *core.World, home core.ID) *core.Entity {
	return w.Spawn(
		&core.IdentC{Name: "Ходок", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Hunger: 10, Energy: 80},
		&core.DrivesC{Ambition: 10},
		&core.MindC{},
		&core.HomeC{Status: "owned", Settlement: home},
		&core.PlaceC{Settlement: home},
	)
}

// TestMigrantWalksPhysicallyAcrossSeamlessBorder — приоритет B (comms 0174/0179): в бесшовном мире
// прямое переселение между соседними сёлами перестаёт быть мгновенным исчезновением — у идущего
// есть настоящая ScenePosC, которая плавно движется от якоря одного села к другому, и межу он
// пересекает физически (как StepLands делает это для игрока), а не телепортом по истечении часов.
func TestMigrantWalksPhysicallyAcrossSeamlessBorder(t *testing.T) {
	w, _, se := starterPlayer(t)
	SetSeamless(w, true)
	to := neighborOf(t, w, se.ID)

	person := spawnMigrant(w, se.ID)
	if !StartMigration(w, person, to, "test", 0) {
		t.Fatal("переселение по прямому дорожному перегону должно начаться")
	}
	m := Migration(person)
	if m == nil || len(m.Route) != 2 {
		t.Fatalf("ожидался прямой маршрут из двух сёл: %+v", m)
	}

	// час за часом идущий должен иметь тело и смещаться к цели, не прыгая
	var prevWX, prevWY float64
	ter := NewTerrain(w)
	total := int(m.TotalHours) + 1
	crossed := false
	for hour := 1; hour <= total && Migration(person) != nil; hour++ {
		clock := core.Clock{TotalHours: int64(hour)}
		(MigrationSystem{}).Hour(w, clock, nil)
		if ScenePos(person) == nil {
			t.Fatalf("на часу %d у идущего должно быть тело в сцене", hour)
		}
		wp, ok := WorldPosOf(w, ter, person)
		if !ok {
			t.Fatalf("на часу %d должна читаться мировая позиция идущего", hour)
		}
		we, wn, _ := wp.Meters()
		if hour > 1 {
			dx, dy := we-prevWX, wn-prevWY
			if dx*dx+dy*dy > 20000*20000 { // щедрый предел — тут важно отсутствие телепорта, не скорость
				t.Fatalf("на часу %d позиция прыгнула: (%.0f,%.0f) → (%.0f,%.0f)", hour, prevWX, prevWY, we, wn)
			}
		}
		prevWX, prevWY = we, wn
		if core.Place(person).Settlement == to {
			crossed = true
		}
	}
	if !crossed {
		t.Fatal("идущий должен в какой-то момент оказаться в землях соседнего села")
	}
	if Migration(person) != nil {
		t.Fatal("по истечении маршрута переселение должно завершиться как и раньше")
	}
	if core.Place(person).Settlement != to {
		t.Fatalf("после прибытия житель должен принадлежать селу назначения, получили %v", core.Place(person).Settlement)
	}
	if len(residentsOf(w, to)) == 0 {
		t.Fatal("после прибытия идущий должен считаться жителем нового села")
	}
}

// TestMigrantInTransitExcludedFromResidentWork — переселенец физически виден (есть ScenePosC), но
// задания/нужды поселения его не трогают, пока MigrationC не снят — иначе система работ попыталась
// бы увести его ходьбу к рабочему месту поверх StepMigrants.
func TestMigrantInTransitExcludedFromResidentWork(t *testing.T) {
	w, _, se := starterPlayer(t)
	SetSeamless(w, true)
	to := neighborOf(t, w, se.ID)
	person := spawnMigrant(w, se.ID)
	if !StartMigration(w, person, to, "test", 0) {
		t.Fatal("переселение должно начаться")
	}
	(MigrationSystem{}).Hour(w, core.Clock{TotalHours: 1}, nil)
	if ScenePos(person) == nil {
		t.Fatal("после первого часа идущий должен иметь тело")
	}
	for _, r := range residentsOf(w, core.Place(person).Settlement) {
		if r.ID == person.ID {
			t.Fatal("идущий не должен попадать в жители текущей земли, пока он в пути")
		}
	}
	idx := residentIndex(w)
	for _, r := range idx[core.Place(person).Settlement] {
		if r.ID == person.ID {
			t.Fatal("индекс жителей тоже не должен включать идущего в пути")
		}
	}
}

// TestMigrantStaysAbstractWithoutSeamless — без бесшовного режима ничего не меняется: идущий,
// как и раньше, невидим и телепортируется по истечении часов (старые миры и флаг -seamless=off).
func TestMigrantStaysAbstractWithoutSeamless(t *testing.T) {
	w, _, se := starterPlayer(t)
	to := neighborOf(t, w, se.ID)
	person := spawnMigrant(w, se.ID)
	if !StartMigration(w, person, to, "test", 0) {
		t.Fatal("переселение должно начаться")
	}
	(MigrationSystem{}).Hour(w, core.Clock{TotalHours: 1}, nil)
	if ScenePos(person) != nil {
		t.Fatal("без бесшовного режима идущий должен оставаться без тела в сцене")
	}
	if core.Place(person).Settlement != 0 {
		t.Fatal("без бесшовного режима идущий должен оставаться вне населения обоих сёл")
	}
}
