package sim

import (
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Глобальные координаты (v1.19): миллиметры, знаковые чанки, высота, якоря сёл, бесшовные земли.

func TestWorldPosChunksAndNegativeCoordinates(t *testing.T) {
	for _, c := range []struct {
		e, n     float64
		cx, cz   int64
		inx, inz float64
	}{
		{0, 0, 0, 0, 0, 0},
		{255.999, 0, 0, 0, 255.999, 0},
		{256, 256, 1, 1, 0, 0},
		{-0.001, -0.001, -1, -1, 255.999, 255.999},
		{-256, -256, -1, -1, 0, 0},
		{-256.001, -257, -2, -2, 255.999, 255},
		{1e7 + 12.345, -1e7 - 12.345, 39062, -39063, 140.345, 115.655},
	} {
		p := WorldPosMeters(0, c.e, c.n, 0)
		cx, cz := p.Chunk()
		x, z := p.InChunk()
		if cx != c.cx || cz != c.cz || math.Abs(x-c.inx) > 1e-9 || math.Abs(z-c.inz) > 1e-9 {
			t.Fatalf("(%v,%v): чанк (%d,%d)+(%v,%v), ждали (%d,%d)+(%v,%v)", c.e, c.n, cx, cz, x, z, c.cx, c.cz, c.inx, c.inz)
		}
		// обратно: угол чанка + смещение = исходная точка, до миллиметра
		oe, on := ChunkOrigin(cx, cz)
		if back := WorldPosMeters(0, oe+x, on+z, 0); back != p {
			t.Fatalf("(%v,%v): туда-обратно через чанк дало %+v вместо %+v", c.e, c.n, back, p)
		}
	}
	if ChunkID(0, -2, 39062) != "0:-2:39062" {
		t.Fatal("имя чанка должно быть устойчивым")
	}
	// слои по высоте: башня выше, подвал и подземелье ниже
	for _, c := range []struct {
		u     float64
		layer int64
	}{{0, 0}, {63.999, 0}, {64, 1}, {-0.001, -1}, {-64, -1}, {-64.001, -2}, {130, 2}} {
		if got := WorldPosMeters(0, 0, 0, c.u).Layer(); got != c.layer {
			t.Fatalf("высота %v: слой %d, ждали %d", c.u, got, c.layer)
		}
	}
	// миллиметровая точность не теряется вдали от начала координат
	far := WorldPosMeters(0, 4_000_000.001, -4_000_000.002, 1234.567)
	if e, n, u := far.Add(0.001, 0.001, -0.001).Meters(); e != 4_000_000.002 || n != -4_000_000.001 || u != 1234.566 {
		t.Fatalf("потеря точности вдали: %v %v %v", e, n, u)
	}
	if _, ok := WorldDist(WorldPos{Frame: 0}, WorldPos{Frame: 1}); ok {
		t.Fatal("расстояние между разными кадрами по плоскости не определено")
	}
	if d, _ := WorldDist(WorldPosMeters(0, -3, 0, 50), WorldPosMeters(0, 0, 4, -50)); d != 5 {
		t.Fatalf("расстояние по земле не зависит от высоты: %v", d)
	}
}

func TestAnchorsAgreeWithSiteGeodesy(t *testing.T) {
	w, _ := GenerateStarter("anchors")
	p := EnsurePlanet(w)
	proj := PlanetProjection{Origin: SurfacePoint{LatitudeDeg: p.CauldronLat, LongitudeDeg: p.CauldronLon}, RadiusMeters: p.RadiusKm * 1000}
	n := 0
	w.Each("settlement", func(se *core.Entity) {
		e, nn := Anchor(w, core.Settle(se))
		site := SiteOf(w, se)
		back, ok := proj.Forward(SurfacePoint{LatitudeDeg: site.Lat, LongitudeDeg: site.Lon})
		// SiteC хранит градусы с точностью 0.01° (около километра) — сверяем в этом допуске
		if !ok || math.Hypot(back.East-e, back.North-nn) > 1200 {
			t.Fatalf("%s: якорь (%.0f,%.0f) расходится с широтой/долготой места (%.0f,%.0f)", core.Settle(se).Name, e, nn, back.East, back.North)
		}
		lat, lon, ok := SurfaceOf(w, WorldPosMeters(0, e, nn, 0))
		if !ok || math.Abs(lat-site.Lat) > 0.006 || math.Abs(NormalizeLongitude(lon-site.Lon)) > 0.006 {
			t.Fatalf("%s: широта/долгота якоря %.4f,%.4f против места %.2f,%.2f", core.Settle(se).Name, lat, lon, site.Lat, site.Lon)
		}
		n++
	})
	if n < 20 {
		t.Fatal("мало поселений")
	}
	// сцены сёл не налезают друг на друга: между центрами больше двух полуширин земли
	var as [][2]float64
	w.Each("settlement", func(se *core.Entity) {
		e, nn := Anchor(w, core.Settle(se))
		for _, o := range as {
			if math.Hypot(o[0]-e, o[1]-nn) < 2*WorldHalf+landHysteresis {
				t.Fatalf("сёла стоят ближе %v м — их сцены пересекутся", 2*WorldHalf)
			}
		}
		as = append(as, [2]float64{e, nn})
	})
}

func TestTerrainIsDeterministicFlatInVillagesAndSeamless(t *testing.T) {
	w, _ := GenerateStarter("terrain")
	a, b := NewTerrain(w), NewTerrain(w)
	w2, _ := GenerateStarter("terrain")
	c := NewTerrain(w2)
	for _, p := range [][2]float64{{0, 0}, {1234.5, -987.25}, {-40000, 22000}, {3e5, 3e5}} {
		if a.Height(p[0], p[1]) != b.Height(p[0], p[1]) || a.Height(p[0], p[1]) != c.Height(p[0], p[1]) {
			t.Fatalf("высота в %v должна повторяться от семени", p)
		}
	}
	w3, _ := GenerateStarter("terrain-другое")
	if NewTerrain(w3).Height(1234.5, -987.25) == a.Height(1234.5, -987.25) {
		t.Fatal("у другого мира — другой рельеф")
	}
	// под селом — ровная площадка: вся сцена на высоте центра
	w.Each("settlement", func(se *core.Entity) {
		e, n := Anchor(w, core.Settle(se))
		h0 := a.Height(e, n)
		for _, d := range [][2]float64{{150, 150}, {-150, 150}, {0, -150}, {149, -3}} {
			if a.Height(e+d[0], n+d[1]) != h0 {
				t.Fatalf("%s: земля в сцене села должна быть ровной", core.Settle(se).Name)
			}
		}
	})
	// нет обрывов: на метр пути высота меняется плавно (в т.ч. на кромке площадки села и в горах)
	se := firstSettlement(w)
	e0, n0 := Anchor(w, core.Settle(se))
	prev := a.Height(e0, n0)
	for d := 1.0; d < 20000; d++ {
		h := a.Height(e0+d*0.8, n0+d*0.6)
		if math.Abs(h-prev) > 2.5 {
			t.Fatalf("обрыв рельефа на %v м от села: %.1f → %.1f", d, prev, h)
		}
		prev = h
	}
	// гребень гор вокруг котла выше дна
	if rim := a.Height(a.rimR, 0); rim < a.Height(0, 0)+300 {
		t.Fatalf("гребень котла должен быть горой: %.0f против дна %.0f", rim, a.Height(0, 0))
	}
	// соседние чанки делят общую кромку — шва нет
	left, right := TerrainChunk(w, 3, -2, 16), TerrainChunk(w, 4, -2, 16)
	lh, rh := left["heights"].([]float64), right["heights"].([]float64)
	side := left["side"].(int)
	if side != 17 || len(lh) != side*side {
		t.Fatalf("сетка высот чанка: сторона %d, отсчётов %d", side, len(lh))
	}
	for j := 0; j < side; j++ {
		if lh[j*side+side-1] != rh[j*side] {
			t.Fatalf("шов между чанками в строке %d: %.2f против %.2f", j, lh[j*side+side-1], rh[j*side])
		}
	}
	up := TerrainChunk(w, 3, -1, 16)["heights"].([]float64)
	for i := 0; i < side; i++ {
		if lh[(side-1)*side+i] != up[i] {
			t.Fatalf("шов между чанками в столбце %d", i)
		}
	}
	// comms 0218 п.5: то же самое и у ОТРИЦАТЕЛЬНЫХ индексов по X (выше были только отрицательные
	// по Z), и запрос в ОБРАТНОМ порядке (правый/верхний чанк раньше левого/нижнего) не должен менять
	// результат — высота чистая функция (x,y), не зависит от порядка/истории запросов.
	rNeg := TerrainChunk(w, -4, -2, 16)["heights"].([]float64) // запрошен ДО левого соседа
	lNeg := TerrainChunk(w, -5, -2, 16)["heights"].([]float64)
	for j := 0; j < side; j++ {
		if lNeg[j*side+side-1] != rNeg[j*side] {
			t.Fatalf("шов между отрицательными чанками X в строке %d: %.2f против %.2f", j, lNeg[j*side+side-1], rNeg[j*side])
		}
	}
	// повторный запрос того же чанка в другой момент/другом порядке — тот же результат
	left2 := TerrainChunk(w, 3, -2, 16)["heights"].([]float64)
	for i := range left2 {
		if left2[i] != lh[i] {
			t.Fatalf("повторный запрос чанка (3,-2) дал другую высоту в точке %d: %.2f против %.2f", i, left2[i], lh[i])
		}
	}
}

func firstSettlement(w *core.World) *core.Entity {
	var se *core.Entity
	w.Each("settlement", func(e *core.Entity) {
		if se == nil {
			se = e
		}
	})
	return se
}

func TestWorldPosOfBodyRoadAndHeight(t *testing.T) {
	w, e, se := starterPlayer(t)
	ter := NewTerrain(w)
	ae, an := Anchor(w, core.Settle(se))
	p := EnsureScenePos(w, e)
	p.X, p.Y, p.Z = 12.5, -30.25, 0
	wp, ok := WorldPosOf(w, ter, e)
	if !ok || wp != WorldPosMeters(0, ae+12.5, an-30.25, ter.Height(ae, an)) {
		t.Fatalf("мир = якорь + место в сцене: %+v", wp)
	}
	// башня и подвал: та же точка на земле, другая высота и, возможно, другой слой
	p.Z = 18
	up, _ := WorldPosOf(w, ter, e)
	p.Z = -7.5
	down, _ := WorldPosOf(w, ter, e)
	if up.E != wp.E || up.N != wp.N || up.U-wp.U != 18000 || wp.U-down.U != 7500 {
		t.Fatalf("высота над землёй должна складываться с высотой земли: %+v %+v %+v", wp, up, down)
	}
	v := WorldPosView(w, e).(map[string]any)
	if v["z"] != -7.5 || v["scheme"] != WorldScheme || len(v["chunk"].([]int64)) != 2 {
		t.Fatalf("me.wpos: %v", v)
	}
	p.Z = 0
	// в пути по тракту положение — на отрезке между сёлами
	var other core.ID
	for _, ex := range Exits(w, se.ID) {
		if !ex.Pass {
			other = ex.To
		}
	}
	w.Attach(e, &JourneyC{From: se.ID, To: other, Total: 100, Done: 25})
	be, bn := Anchor(w, core.Settle(w.Get(other)))
	road, ok := WorldPosOf(w, ter, e)
	x, y, _ := road.Meters()
	if !ok || math.Abs(x-(ae+(be-ae)*0.25)) > 0.001 || math.Abs(y-(an+(bn-an)*0.25)) > 0.001 {
		t.Fatalf("в пути: (%v,%v)", x, y)
	}
}

// Бесшовный мир: край сцены — не стена, а переход в земли соседа не сдвигает тело в мире.
func TestSeamlessLandsKeepWorldPosition(t *testing.T) {
	w, e, se := starterPlayer(t)
	ctl := func(id core.ID) bool { return id == e.ID }
	p := EnsureScenePos(w, e)
	l := LayoutOf(w, se.ID)

	// по умолчанию мир прежний: край земли — стена
	p.X, p.Y = 149, 0
	ApplyMove(l, p, 1, 0, false, 0, 0.25, 1)
	ApplyMove(l, p, 1, 0, false, 0, 0.25, 2)
	ApplyMove(l, p, 1, 0, false, 0, 0.25, 3)
	ApplyMove(l, p, 1, 0, false, 0, 0.25, 4)
	if p.X > WorldHalf {
		t.Fatalf("без бесшовного режима за край не выйти: x=%.1f", p.X)
	}

	SetSeamless(w, true)
	l = LayoutOf(w, se.ID)
	if !l.Open || StarterLayout().Open {
		t.Fatal("в бесшовном мире раскладка открыта, а общая стартовая не тронута")
	}
	// идём к соседнему селу по прямой — без дороги-перехода, просто ногами
	ae, an := Anchor(w, core.Settle(se))
	var other core.ID
	dist := math.MaxFloat64
	w.Each("settlement", func(o *core.Entity) { // ближайший сосед по котлу
		if o.ID == se.ID || isOuter(w, core.Settle(o)) {
			return
		}
		oe, on := Anchor(w, core.Settle(o))
		if d := math.Hypot(oe-ae, on-an); d < dist {
			dist, other = d, o.ID
		}
	})
	be, bn := Anchor(w, core.Settle(w.Get(other)))
	ter := NewTerrain(w)
	seq := int64(10)
	switched := false
	p.X, p.Y = l.Entry()
	before, _ := WorldPosOf(w, ter, e)
	for step := 0; step < 400000 && !switched; step++ {
		// цель в локальных координатах текущего села
		cur := core.Settle(w.Get(core.Place(e).Settlement))
		ce, cn := Anchor(w, cur)
		dx, dy := be-(ce+p.X), bn-(cn+p.Y)
		seq++
		ApplyMove(LayoutFor(w, e), p, dx, dy, false, 0, 0.25, seq)
		prev := before
		before, _ = WorldPosOf(w, ter, e)
		if d, _ := WorldDist(prev, before); d > walkSpeed*0.25+0.01 {
			t.Fatalf("скачок положения в мире на шаге %d: %.3f м", step, d)
		}
		wasIn := core.Place(e).Settlement
		pre := before
		StepLands(w, ctl)
		StepExits(w, ctl, nil) // выезд у края не должен увести на «тракт» — идём ногами
		if OnRoad(e) {
			t.Fatal("в бесшовном мире выход за околицу не запускает переход по тракту")
		}
		if core.Place(e).Settlement != wasIn {
			after, _ := WorldPosOf(w, ter, e)
			if after.E != pre.E || after.N != pre.N {
				t.Fatalf("переход в земли соседа сдвинул тело в мире: %+v → %+v", pre, after)
			}
			switched = true
		}
	}
	if !switched || core.Place(e).Settlement != other {
		t.Fatalf("игрок должен был дойти до земель соседа (%.0f м пути)", dist)
	}
	// межа — посередине: до нового центра ближе, чем до старого
	if math.Hypot(p.X, p.Y) > dist/2 {
		t.Fatalf("после перехода до центра нового села %.0f м при пути %.0f", math.Hypot(p.X, p.Y), dist)
	}
	// обратно сразу не перебрасывает (нет дребезга на меже)
	StepLands(w, ctl)
	if core.Place(e).Settlement != other {
		t.Fatal("на меже не должно быть дребезга")
	}
}

// TestSeamlessRoundTripPreservesIdentityAndState — приёмочный тест A→B→A (comms 0218 п.4): пешком
// туда и обратно без единого прыжка/телепорта — Entity ID не меняется (та же сущность всю дорогу,
// никакого respawn), монеты/инвентарь/провенанс задания (settlement_id — comms 0215/v1.23) не
// трогаются переходом (их компоненты не зависят от PlaceC.Settlement), а соседи по AOI видны и в A,
// и в B (не исчезают на границе).
func TestSeamlessRoundTripPreservesIdentityAndState(t *testing.T) {
	w, e, se := starterPlayer(t)
	ctl := func(id core.ID) bool { return id == e.ID }
	startID := e.ID
	p := EnsureScenePos(w, e)
	SetSeamless(w, true)
	p.X, p.Y = LayoutOf(w, se.ID).Entry()

	// снаряжаем игрока тем, что НЕ должно зависеть от перехода между сёлами
	core.Purse(e).Coins = 12.5
	ensureInv(w, e).Hand = "axe"
	var giver *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); giver == nil && r.ID != e.ID && v != nil && v.Age >= 18 {
			giver = r
		}
	}
	gp := EnsureScenePos(w, giver)
	gp.X, gp.Y = p.X+1, p.Y
	ensureStashOf(w, giver).Goods[econ.GoodWood] = 0
	StartTalk(w, e, giver.ID)
	Say(w, e, "greet", "rt1")
	if ok, why := Say(w, e, "work", "rt2"); !ok {
		t.Fatalf("взять поручение: %s", why)
	}
	Say(w, e, "endtalk", "rt3")

	ae, an := Anchor(w, core.Settle(se))
	var other core.ID
	dist := math.MaxFloat64
	w.Each("settlement", func(o *core.Entity) {
		if o.ID == se.ID || isOuter(w, core.Settle(o)) {
			return
		}
		if d := func() float64 { oe, on := Anchor(w, core.Settle(o)); return math.Hypot(oe-ae, on-an) }(); d < dist {
			dist, other = d, o.ID
		}
	})
	if other == 0 {
		t.Fatal("в стартовом котле должен быть хотя бы один соседний край")
	}
	ter := NewTerrain(w)
	seq := int64(100)
	walkToward := func(targetSettlement core.ID, maxSteps int) bool {
		te, tn := Anchor(w, core.Settle(w.Get(targetSettlement)))
		for step := 0; step < maxSteps; step++ {
			cur := core.Settle(w.Get(core.Place(e).Settlement))
			ce, cn := Anchor(w, cur)
			dx, dy := te-(ce+p.X), tn-(cn+p.Y)
			seq++
			ApplyMove(LayoutFor(w, e), p, dx, dy, false, 0, 0.25, seq)
			StepLands(w, ctl)
			StepExits(w, ctl, nil)
			if core.Place(e).Settlement == targetSettlement {
				return true
			}
		}
		return false
	}

	if !walkToward(other, 400000) {
		t.Fatalf("не дошли до соседнего края (%.0f м)", dist)
	}
	if e.ID != startID {
		t.Fatal("переход не должен создавать новую сущность")
	}
	if core.Purse(e).Coins != 12.5 || Inv(e).Hand != "axe" {
		t.Fatal("монеты/инвентарь не должны меняться переходом в B")
	}
	issuedB := OpeningTask(w, e)["issued"].(map[string]any)
	if issuedB == nil || issuedB["settlement_id"] != eidStr(se.ID) {
		t.Fatalf("провенанс поручения должен остаться A в селе B: %+v", issuedB)
	}
	if neigh := AOINeighbors(w, ter, e, core.Place(e).Settlement, 24); len(neigh) == 0 {
		t.Fatal("в B должны быть видны хоть какие-то соседи по AOI (своё же новое село, минимум)")
	}

	if !walkToward(se.ID, 400000) {
		t.Fatal("не дошли обратно до исходного села A")
	}
	if e.ID != startID {
		t.Fatal("обратный переход не должен создавать новую сущность")
	}
	if core.Purse(e).Coins != 12.5 || Inv(e).Hand != "axe" {
		t.Fatal("монеты/инвентарь не должны меняться обратным переходом в A")
	}
	issuedA := OpeningTask(w, e)["issued"].(map[string]any)
	if issuedA == nil || issuedA["settlement_id"] != eidStr(se.ID) {
		t.Fatalf("провенанс поручения должен остаться A и после возврата в A: %+v", issuedA)
	}
	if neigh := AOINeighbors(w, ter, e, se.ID, 24); len(neigh) == 0 {
		t.Fatal("после возврата в A соседи по AOI не должны исчезнуть")
	}
}

// TestWorldPosViewUsingMatchesPerEntityView — общий Terrain (для кадра из me + соседей, comms 0178)
// должен давать ТОЧНО тот же результат, что отдельный WorldPosView на ту же сущность. Важно: один
// Terrain на кадр — это экономия (NewTerrain обходит все поселения), а не смена формулы.
func TestWorldPosViewUsingMatchesPerEntityView(t *testing.T) {
	w, e, se := starterPlayer(t)
	var neighbor *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if r.ID != e.ID {
			neighbor = r
			break
		}
	}
	if neighbor == nil {
		t.Fatal("нужен хоть один сосед для теста")
	}
	EnsureScenePos(w, neighbor)

	shared := NewTerrain(w)
	for _, ent := range []*core.Entity{e, neighbor} {
		got := WorldPosViewUsing(shared, w, ent)
		want := WorldPosView(w, ent)
		gm, wm := got.(map[string]any), want.(map[string]any)
		if gm == nil || wm == nil {
			t.Fatalf("оба вызова должны вернуть карту: got=%v want=%v", got, want)
		}
		for _, k := range []string{"scheme", "v", "frame", "e", "n", "u", "ground", "z", "layer"} {
			if gm[k] != wm[k] {
				t.Fatalf("%s: общий Terrain дал %v, отдельный %v", k, gm[k], wm[k])
			}
		}
	}
}
