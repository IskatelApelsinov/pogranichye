package sim

import (
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Окрестности (v1.22): куда ни пойди — куда-нибудь придёшь; запас мест не бесконечен.

func TestWildPlacesAreEverywhereAndDeterministic(t *testing.T) {
	w, _ := GenerateStarter("wild")
	ter := NewTerrain(w)
	// в любой стороне от котла, близко и за тысячи километров, в радиусе путника есть место
	kinds := map[string]int{}
	for _, at := range [][2]float64{{0, 0}, {40000, -15000}, {-90000, 60000}, {250000, 250000}, {-3_000_000, 1_200_000}, {7_500_000, -7_500_000}} {
		near := WildPlacesNear(w, ter, at[0], at[1], wildNearDist)
		if len(near) < 3 {
			t.Fatalf("у точки %v в радиусе %v м всего %d мест — путник пришёл бы в пустоту", at, wildNearDist, len(near))
		}
		for i, p := range near {
			kinds[p.Kind]++
			if i > 0 && math.Hypot(p.E-at[0], p.N-at[1]) < math.Hypot(near[i-1].E-at[0], near[i-1].N-at[1]) {
				t.Fatal("места должны идти от ближнего к дальнему")
			}
			back, ok := WildPlaceByID(w, ter, p.ID)
			if !ok || back != p {
				t.Fatalf("место %s должно находиться по своему id таким же", p.ID)
			}
			if len(wildKinds[p.Kind].acts) == 0 || p.Name == "" {
				t.Fatalf("у места %s нет имени или дел", p.ID)
			}
		}
	}
	if len(kinds) < 7 {
		t.Fatalf("мест должно быть много разных видов, а нашлось %v", kinds)
	}
	// то же семя — те же места; другое семя — другие
	w2, _ := GenerateStarter("wild")
	a, b := WildPlacesNear(w, ter, 40000, -15000, 3000), WildPlacesNear(w2, NewTerrain(w2), 40000, -15000, 3000)
	if len(a) != len(b) || a[0] != b[0] {
		t.Fatal("урочища выводятся из семени мира и должны совпадать")
	}
	w3, _ := GenerateStarter("wild-другое")
	if c := WildPlacesNear(w3, NewTerrain(w3), 40000, -15000, 3000); len(c) > 0 && c[0] == a[0] {
		t.Fatal("у другого мира — другие урочища")
	}
	// в самих сёлах урочищ нет
	w.Each("settlement", func(se *core.Entity) {
		e, n := Anchor(w, core.Settle(se))
		for _, p := range WildPlacesNear(w, ter, e, n, 300) {
			if p.ID[0] == 'w' {
				t.Fatalf("урочище %s стоит прямо в селе %s", p.ID, core.Settle(se).Name)
			}
		}
	})
	for _, bad := range []string{"", "w:1", "w:x:y", "w:99999999999999:1", "abc", "0"} {
		if _, ok := WildPlaceByID(w, ter, bad); ok {
			t.Fatalf("кривой id %q не должен находить место", bad)
		}
	}
}

// placeBodyAt ставит тело в точку мира относительно его нынешнего села.
func placeBodyAt(w *core.World, e *core.Entity, we, wn float64) {
	ax, ay := Anchor(w, core.Settle(w.Get(core.Place(e).Settlement)))
	p := EnsureScenePos(w, e)
	p.X, p.Y = we-ax, wn-ay
}

func nearestOfKind(t *testing.T, w *core.World, e *core.Entity, act string) WildPlace {
	t.Helper()
	x, y, _ := bodyWorldXY(w, e)
	for _, p := range WildPlacesNear(w, NewTerrain(w), x, y, 40000) {
		for _, a := range wildKinds[p.Kind].acts {
			if a == act {
				return p
			}
		}
	}
	t.Fatalf("в 40 км нет места с делом %s", act)
	return WildPlace{}
}

func TestSiteActsForageRestExploreWithConservation(t *testing.T) {
	w, e, _ := starterPlayer(t)
	SetSeamless(w, true)
	g := dice.NewStream(7)
	v := core.Vitals(e)

	herbs := nearestOfKind(t, w, e, "forage:herb")
	if ok, why := SiteAct(w, e, herbs.ID, "forage", 10, "far", g); ok || why == "" {
		t.Fatal("издалека травы не набрать")
	}
	placeBodyAt(w, e, herbs.E+3, herbs.N-2)
	v.Energy = 100
	got := 0
	for i := 0; i < 12; i++ {
		v.Energy = 100
		if ok, _ := SiteAct(w, e, herbs.ID, "forage", 10, "h"+string(rune('a'+i)), g); ok {
			got++
		}
	}
	if got != int(wildStockCap) {
		t.Fatalf("запас места конечен: взято %d, а в нём %v", got, wildStockCap)
	}
	if stashOf(e, "herb") != float64(got) {
		t.Fatalf("в закромах должно прибавиться ровно взятое: %v", stashOf(e, "herb"))
	}
	if ok, _ := SiteAct(w, e, herbs.ID, "forage", 10, "ha", g); !ok {
		t.Fatal("повтор принятого reqid идемпотентен")
	}
	if stashOf(e, "herb") != float64(got) {
		t.Fatal("повтор reqid не даёт травы второй раз")
	}
	// через день место немного отошло — но не всё сразу
	v.Energy = 100
	n := 0
	for i := 0; i < 6; i++ {
		v.Energy = 100
		if ok, _ := SiteAct(w, e, herbs.ID, "forage", 11, "n"+string(rune('a'+i)), g); ok {
			n++
		}
	}
	if n != int(wildStockRegen) {
		t.Fatalf("за сутки восполняется %v, а взять удалось %d", wildStockRegen, n)
	}
	// в месте можно только то, что ему свойственно
	if ok, _ := SiteAct(w, e, herbs.ID, "explore", 11, "x1", g); ok && herbs.Kind == "herbs" {
		t.Fatal("травяной луг не обыскивают")
	}

	// привал: раз в день, с постелью — лучше
	camp := nearestOfKind(t, w, e, "rest")
	placeBodyAt(w, e, camp.E, camp.N)
	v.Energy = 20
	if ok, why := SiteAct(w, e, camp.ID, "rest", 20, "r1", g); !ok {
		t.Fatalf("привал: %s", why)
	}
	plain := v.Energy - 20
	if ok, _ := SiteAct(w, e, camp.ID, "rest", 20, "r2", g); ok {
		t.Fatal("второй привал в тот же день сил не прибавляет")
	}
	ensureInv(w, e).Items["bedroll"] = 1
	v.Energy = 20
	if ok, _ := SiteAct(w, e, camp.ID, "rest", 21, "r3", g); !ok || v.Energy-20 <= plain {
		t.Fatalf("с постелью отдых лучше: %v против %v", v.Energy-20, plain)
	}

	// обыск: что-то происходит, и сразу второй раз обыскать нельзя
	ruin := nearestOfKind(t, w, e, "explore")
	placeBodyAt(w, e, ruin.E, ruin.N)
	v.Energy, v.Health = 100, 100
	if ok, why := SiteAct(w, e, ruin.ID, "explore", 30, "e1", g); !ok {
		t.Fatalf("обыск: %s", why)
	}
	if v.Energy >= 100 || v.Health < 6 {
		t.Fatal("обыск стоит сил и не убивает")
	}
	if ok, _ := SiteAct(w, e, ruin.ID, "explore", 31, "e2", g); ok {
		t.Fatal("только что обысканное место пусто")
	}
	if ok, _ := SiteAct(w, e, ruin.ID, "explore", 30+exploreEvery, "e3", g); !ok {
		t.Fatal("через несколько дней место можно обыскать снова")
	}

	// кадр: место рядом помечено here, запас виден
	placeBodyAt(w, e, herbs.E, herbs.N)
	view := SitesView(w, NewTerrain(w), e, 11)
	if len(view) == 0 || view[0]["id"] != herbs.ID || view[0]["here"] != true || view[0]["left"] != 0.0 {
		t.Fatalf("place.sites: %v", view[0])
	}
}

func TestWalkToSiteCrossesFieldWithoutJumps(t *testing.T) {
	w, e, _ := starterPlayer(t)
	ctl := func(id core.ID) bool { return id == e.ID }
	if ok, _ := WalkToSite(w, e, "w:0:0"); ok {
		t.Fatal("без бесшовного мира за околицу не выйти")
	}
	SetSeamless(w, true)
	p := EnsureScenePos(w, e)
	type trip struct {
		from core.ID
		site WildPlace
	}
	var trips []trip
	w.Each("settlement", func(se *core.Entity) { // из каждого села котла: и стартового, и сгенерированных
		if isOuter(w, core.Settle(se)) {
			return
		}
		ax, ay := Anchor(w, core.Settle(se))
		near := WildPlacesNear(w, NewTerrain(w), ax, ay, wildNearDist)
		if len(near) == 0 {
			t.Fatalf("рядом с селом %s должны быть места", core.Settle(se).Name)
		}
		for _, site := range near[:min(3, len(near))] {
			trips = append(trips, trip{se.ID, site})
		}
	})
	for _, tr := range trips {
		site := tr.site
		// каждый раз выходим из корчмы — из самого ядра села
		core.Place(e).Settlement = tr.from
		tx, ty, _ := walkTarget(LayoutFor(w, e), "tavern")
		p.X, p.Y = tx, ty
		if ok, why := WalkToSite(w, e, site.ID); !ok {
			t.Fatalf("к %s не пошёл: %s", site.Name, why)
		}
		if v := WalkView(w, e).(map[string]any); v["site"] != site.ID || v["left"].(float64) <= 0 {
			t.Fatalf("me.walk в походе: %v", v)
		}
		px, py, _ := bodyWorldXY(w, e)
		sec := 0.0
		for ; sec < 900 && autoWalkOf(e) != nil; sec += 0.1 {
			StepAutoWalk(w, 0.1, ctl)
			StepLands(w, ctl)
			x, y, _ := bodyWorldXY(w, e)
			if d := math.Hypot(x-px, y-py); d > fieldWalkSpeed*0.1+2.1 {
				t.Fatalf("к %s: скачок на %.1f м за шаг", site.Name, d)
			}
			px, py = x, y
		}
		if d := math.Hypot(site.E-px, site.N-py); autoWalkOf(e) != nil || d > wildReach {
			t.Fatalf("до %s (%s) не дошёл за %.0f с: осталось %.0f м", site.Name, site.Kind, sec, d)
		}
	}
}

// Через гребень гор котла можно перевалить ногами: мир за горами не отрезан.
func TestWalkerCrossingTheRimEntersOuterLands(t *testing.T) {
	w, e, _ := starterPlayer(t)
	SetSeamless(w, true)
	ctl := func(id core.ID) bool { return id == e.ID }
	rim := NewTerrain(w).rimR
	x, y, _ := bodyWorldXY(w, e)
	k := (rim - 400) / math.Hypot(x, y)
	placeBodyAt(w, e, x*k, y*k) // у подножия гребня изнутри
	StepLands(w, ctl)
	if isOuter(w, core.Settle(w.Get(core.Place(e).Settlement))) {
		t.Fatal("внутри гребня путник в землях котла")
	}
	before := core.Place(e).Settlement
	wx, wy, _ := bodyWorldXY(w, e)
	k = (rim + rimMargin + 50) / math.Hypot(wx, wy)
	placeBodyAt(w, e, wx*k, wy*k) // перевалил
	px, py, _ := bodyWorldXY(w, e)
	StepLands(w, ctl)
	after := core.Place(e).Settlement
	if after == before || !isOuter(w, core.Settle(w.Get(after))) {
		t.Fatal("за гребнем путник должен оказаться в землях внешнего мира")
	}
	ax, ay, _ := bodyWorldXY(w, e)
	if math.Abs(ax-px) > 1e-6 || math.Abs(ay-py) > 1e-6 {
		t.Fatal("смена земель не сдвигает тело в мире")
	}
	// и там тоже есть куда прийти
	if len(SitesView(w, NewTerrain(w), e, 1)) == 0 {
		t.Fatal("за горами тоже должны быть места вокруг")
	}
}

func stashOf(e *core.Entity, good string) float64 {
	if st := econ.Stash(e); st != nil {
		return st.Goods[good]
	}
	return 0
}
