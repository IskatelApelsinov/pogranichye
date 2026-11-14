package sim

import (
	"testing"

	"pogranichye/core"
)

// TestFoesViewAOICrossBorderBandit — остаток comms 0219 п.3: враг соседнего (тоже горячего) села у
// самой межи должен попадать в place.foes смотрящего, как это уже делают жители через AOINeighbors.
// Геометрия — как в TestAOINeighborsCrossBorder: якорь A = (0,0) м, якорь B = (500,0) м (минимум 500 м).
// Проверяем сразу все критерии 0219: свой враг виден всегда; чужой у межи — виден и выражен от якоря
// смотрящего + несёт wpos; чужой в глубине соседнего села (за радиусом) — не течёт в кадр; враг
// дальнего села не попадает; id стабильны и без дублей.
func TestFoesViewAOICrossBorderBandit(t *testing.T) {
	w := core.NewWorld("seed-foes-aoi")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain", X: 500, Y: 500, Mana: 1.0})

	a := w.Spawn(&core.SettlementC{Name: "А", X: 500, Y: 500, Region: region.ID})
	b := w.Spawn(&core.SettlementC{Name: "Б", X: 502, Y: 500, Region: region.ID})         // 500 м от A
	far := w.Spawn(&core.SettlementC{Name: "Далёкое", X: 700, Y: 500, Region: region.ID}) // 50 км

	viewer := w.Spawn(&core.IdentC{Name: "Смотрящий"}, &core.PlaceC{Settlement: a.ID},
		&ScenePosC{X: 200, Y: 0, Facing: 0}) // мир (200,0)

	ownFoe := w.Spawn(&FoeC{Species: "wolf", Settlement: a.ID, X: 50, Y: 0, HP: 30, Max: 30, State: "roam"})
	nearForeign := w.Spawn(&FoeC{Species: "bandit", Settlement: b.ID, X: -200, Y: 0, HP: 45, Max: 45, State: "roam"}) // мир (300,0): 100 м от смотрящего
	w.Spawn(&FoeC{Species: "bandit", Settlement: b.ID, X: 250, Y: 0, HP: 45, Max: 45, State: "roam"})                 // мир (750,0): 550 м — за радиусом
	w.Spawn(&FoeC{Species: "bandit", Settlement: far.ID, X: 0, Y: 0, HP: 45, Max: 45, State: "roam"})                 // дальнее село — не кандидат

	terr := NewTerrain(w)
	vanchorE, vanchorN := Anchor(w, core.Settle(a))

	got := FoesViewAOI(w, terr, viewer, a.ID, vanchorE, vanchorN)

	ids := map[string]map[string]any{}
	for _, m := range got {
		id := m["id"].(string)
		if _, dup := ids[id]; dup {
			t.Fatalf("дубль врага с id %q в кадре", id)
		}
		ids[id] = m
	}
	if len(got) != 2 {
		t.Fatalf("ожидались ровно 2 врага (свой + чужой у межи), получено %d: %v", len(got), ids)
	}

	own, ok := ids[eidStr(ownFoe.ID)]
	if !ok {
		t.Fatalf("свой враг должен быть в кадре всегда")
	}
	if own["x"].(float64) != 50 {
		t.Fatalf("свой враг — локальные x как раньше, ожидалось 50, получено %v", own["x"])
	}

	foreign, ok := ids[eidStr(nearForeign.ID)]
	if !ok {
		t.Fatalf("чужой враг у межи (100 м) должен быть виден через границу")
	}
	// выражен от якоря смотрящего: мир (300,0) − якорь A (0,0) = (300,0)
	if foreign["x"].(float64) != 300 {
		t.Fatalf("чужой враг должен выражаться от якоря смотрящего, ожидалось x=300, получено %v", foreign["x"])
	}
	wp, ok := foreign["wpos"].(map[string]any)
	if !ok {
		t.Fatalf("у врага должно быть аддитивное поле wpos (v1.23)")
	}
	if wp["e"].(float64) != 300 {
		t.Fatalf("wpos.e чужого врага должно быть мировым 300, получено %v", wp["e"])
	}

	// стабильность id между соседними кадрами
	got2 := FoesViewAOI(w, terr, viewer, a.ID, vanchorE, vanchorN)
	if len(got2) != len(got) {
		t.Fatalf("набор врагов не стабилен между кадрами: %d против %d", len(got2), len(got))
	}
	for i := range got {
		if got[i]["id"] != got2[i]["id"] {
			t.Fatalf("порядок/id врагов не стабилен между кадрами")
		}
	}
}

// TestFoesViewAOINoNearbySettlementsMatchesPlain — для мира без близких горячих соседей (обычное
// дело) FoesViewAOI должен давать тот же набор и порядок, что старый FoesView (кандидат-село одно —
// своё), отличаясь только новым аддитивным полем wpos. Гарантия обратной совместимости кадра.
func TestFoesViewAOINoNearbySettlementsMatchesPlain(t *testing.T) {
	w := core.NewWorld("seed-foes-aoi-lonely")
	region := w.Spawn(&core.RegionC{Name: "Край", Biome: "plain", X: 500, Y: 500, Mana: 1.0})
	a := w.Spawn(&core.SettlementC{Name: "А", X: 500, Y: 500, Region: region.ID})
	far := w.Spawn(&core.SettlementC{Name: "Далёкое", X: 700, Y: 500, Region: region.ID})

	viewer := w.Spawn(&core.IdentC{Name: "Смотрящий"}, &core.PlaceC{Settlement: a.ID}, &ScenePosC{})
	w.Spawn(&FoeC{Species: "boar", Settlement: a.ID, X: 10, Y: 0, HP: 40, Max: 40, State: "roam"})
	w.Spawn(&FoeC{Species: "bandit", Settlement: far.ID, X: 0, Y: 0, HP: 45, Max: 45, State: "roam"})

	terr := NewTerrain(w)
	vanchorE, vanchorN := Anchor(w, core.Settle(a))

	plain := FoesView(w, a.ID)
	aoi := FoesViewAOI(w, terr, viewer, a.ID, vanchorE, vanchorN)

	if len(aoi) != len(plain) {
		t.Fatalf("без близких соседей набор должен совпадать с FoesView: %d против %d", len(aoi), len(plain))
	}
	for i := range plain {
		if plain[i]["id"] != aoi[i]["id"] || plain[i]["x"] != aoi[i]["x"] {
			t.Fatalf("без близких соседей id/x должны совпадать с FoesView")
		}
		if _, ok := aoi[i]["wpos"]; !ok {
			t.Fatalf("FoesViewAOI должен добавлять wpos даже в одиночном селе")
		}
	}
}
