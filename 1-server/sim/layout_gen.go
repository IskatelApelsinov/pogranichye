package sim

import (
	"math"

	"pogranichye/core"
	"pogranichye/dice"
)

// Генератор раскладки поселения. Вход: семя мира, id поселения, его величина (Tier), рельеф и угодья
// места (SiteC) и план застройки. Выход: Layout — те же сущности, что в ручной стартовой раскладке.
//
// Что делает сёла разными:
//   - план застройки: «улица» (дворы в два ряда вдоль одной дороги), «крест» (две улицы),
//     «круг» (дворы вокруг площади), «хутора» (россыпь дворов);
//   - где вода: река с севера или с юга, ближе или дальше, мост в своём месте; нет реки — пруд;
//   - с какой стороны лес, поля и рудник — у каждого села своя;
//   - сколько изб: от величины села;
//   - размеры и места дворов — от семени.
//
// Что одинаково у всех (на этом держится навигация): две оси-улицы через центр и объездная дорога
// вокруг ядра всегда свободны от построек, от каждого порога есть прямая свободная тропа к улице.
// Поэтому житель из любой точки дойдёт до любой другой по графу дорог, не упираясь в стены.

const (
	laneHalf   = 4.5 // полуширина свободной полосы вдоль осей-улиц
	squareR    = 9.0 // радиус свободной площади у колодца
	lotMargin  = 3.0 // зазор между дворами
	doorOut    = 2.0 // порог — в двух метрах от стены
	ringMargin = 8.0 // объездная дорога — на столько дальше ядра
)

var layoutPlans = []string{"ring", "street", "scatter", "cross"}

// planFor — план застройки. В котле планы идут по кругу от ранга села, чтобы соседние деревни
// заведомо отличались; во внешнем мире — от семени, с поправкой на рельеф.
func planFor(seed string, id core.ID, rank int, terrain string) string {
	if rank > 0 {
		return layoutPlans[rank%len(layoutPlans)]
	}
	g := dice.NewStream(dice.Seed64(seed, "layout-plan", sid(id)))
	switch terrain {
	case "hills", "marsh":
		if g.Chance(0.6) {
			return "scatter"
		}
	case "river", "coast":
		if g.Chance(0.5) {
			return "street"
		}
	}
	return layoutPlans[g.Intn(len(layoutPlans))]
}

type lotKind struct {
	name, kind string
	w0, w1     float64 // ширина двора, от и до
	d0, d1     float64 // глубина
	poi        string
}

var publicLots = []lotKind{
	{"Корчма", "tavern", 13, 16, 11, 13, "tavern"},
	{"Лавка", "market", 12, 15, 9, 11, "market"},
	{"Кузница", "smithy", 12, 15, 10, 13, "smithy"},
	{"Амбар", "granary", 14, 17, 10, 13, "granary"},
	{"Изба старосты", "house", 12, 15, 10, 13, "hearth"},
}

var houseLot = lotKind{"Изба", "house", 9, 13, 8, 11, ""}

func housesFor(tier string, g *dice.Stream) int {
	switch tier {
	case "hamlet":
		return 2 + g.Intn(2)
	case "town":
		return 8 + g.Intn(4)
	case "city":
		return 12 + g.Intn(6)
	}
	return 4 + g.Intn(4)
}

func coreFor(tier string) float64 {
	switch tier {
	case "town":
		return 64
	case "city":
		return 78
	}
	return 50
}

type segment struct{ x0, y0, x1, y1 float64 }

func rectsOverlap(a, b Rect, m float64) bool {
	return a.X0-m < b.X1 && a.X1+m > b.X0 && a.Y0-m < b.Y1 && a.Y1+m > b.Y0
}

func rectHitsSegment(r Rect, s segment, m float64) bool {
	d := math.Hypot(s.x1-s.x0, s.y1-s.y0)
	n := int(d*2) + 1
	for i := 0; i <= n; i++ {
		t := float64(i) / float64(n)
		x, y := s.x0+(s.x1-s.x0)*t, s.y0+(s.y1-s.y0)*t
		if x >= r.X0-m && x <= r.X1+m && y >= r.Y0-m && y <= r.Y1+m {
			return true
		}
	}
	return false
}

// generateLayout строит раскладку. Детерминирована: одни и те же входы — та же раскладка.
func generateLayout(seed string, id core.ID, tier, terrain string, site *SiteC, plan string) *Layout {
	gb := dice.NewStream(dice.Seed64(seed, "layout-build", sid(id))) // дворы
	ge := dice.NewStream(dice.Seed64(seed, "layout-land", sid(id)))  // окрестности
	has := func(f string) bool { return site != nil && site.Features[f] }

	V := coreFor(tier)
	R := V + ringMargin
	l := &Layout{Plan: plan, Half: WorldHalf, VillageHalf: V, POIs: map[string][2]float64{}}

	// --- дворы ---
	lots := append([]lotKind{}, publicLots...)
	for i, n := 0, housesFor(tier, gb); i < n; i++ {
		lots = append(lots, houseLot)
	}
	var keep []segment // тропы от порогов к улице: их нельзя застраивать
	type door struct {
		x, y, px, py float64 // порог и его проекция на улицу
		axisX        bool    // проекция лежит на оси X (улица вдоль X)
	}
	var doors []door

	sample := func(i int, w, d float64) (cx, cy float64) {
		span := V
		if i < len(publicLots) {
			span = V * 0.6 // общие дома — ближе к центру
		}
		p := plan
		if p == "cross" {
			p = "street"
			if gb.Chance(0.5) {
				p = "street-y"
			}
		}
		switch p {
		case "street":
			side := 1.0
			if gb.Chance(0.5) {
				side = -1
			}
			return gb.Uniform(-span+w/2, span-w/2), side * (gb.Uniform(5.5, 7.5) + d/2)
		case "street-y":
			side := 1.0
			if gb.Chance(0.5) {
				side = -1
			}
			return side * (gb.Uniform(5.5, 7.5) + w/2), gb.Uniform(-span+d/2, span-d/2)
		case "ring":
			rad := gb.Uniform(19, 27)
			if i >= 8 {
				rad = gb.Uniform(34, V-8)
			}
			a := gb.Uniform(0, 2*math.Pi)
			return rad * math.Cos(a), rad * math.Sin(a)
		}
		return gb.Uniform(-span+w/2, span-w/2), gb.Uniform(-span+d/2, span-d/2) // хутора
	}

	for i, lk := range lots {
		for try := 0; try < 400; try++ {
			w, d := gb.Uniform(lk.w0, lk.w1), gb.Uniform(lk.d0, lk.d1)
			cx, cy := sample(i, w, d)
			if try >= 200 { // на плане тесно — ставим где свободно
				cx, cy = gb.Uniform(-V+w/2, V-w/2), gb.Uniform(-V+d/2, V-d/2)
			}
			r := Rect{Name: lk.name, Kind: lk.kind, X0: math.Round(cx - w/2), Y0: math.Round(cy - d/2), X1: math.Round(cx + w/2), Y1: math.Round(cy + d/2)}
			if r.X0 < -V || r.X1 > V || r.Y0 < -V || r.Y1 > V {
				continue
			}
			// оси-улицы и площадь свободны
			if (r.X0 < laneHalf && r.X1 > -laneHalf) || (r.Y0 < laneHalf && r.Y1 > -laneHalf) {
				continue
			}
			nx, ny := math.Max(r.X0, math.Min(0, r.X1)), math.Max(r.Y0, math.Min(0, r.Y1))
			if math.Hypot(nx, ny) < squareR {
				continue
			}
			ok := true
			for _, b := range l.Buildings {
				if rectsOverlap(r, b, lotMargin) {
					ok = false
					break
				}
			}
			for _, s := range keep {
				if ok && rectHitsSegment(r, s, 1.5) {
					ok = false
				}
			}
			if !ok {
				continue
			}
			// порог смотрит на ближнюю улицу; тропа от него к улице должна быть свободна
			var dr door
			if math.Min(math.Abs(r.Y0), math.Abs(r.Y1)) <= math.Min(math.Abs(r.X0), math.Abs(r.X1)) {
				dr.x, dr.px, dr.py, dr.axisX = r.cx(), r.cx(), 0, true
				if r.Y0 > 0 {
					dr.y = r.Y0 - doorOut
				} else {
					dr.y = r.Y1 + doorOut
				}
			} else {
				dr.y, dr.px, dr.py = r.cy(), 0, r.cy()
				if r.X0 > 0 {
					dr.x = r.X0 - doorOut
				} else {
					dr.x = r.X1 + doorOut
				}
			}
			path := segment{dr.x, dr.y, dr.px, dr.py}
			for _, b := range l.Buildings {
				if rectHitsSegment(b, path, 1.5) {
					ok = false
					break
				}
			}
			if !ok {
				continue
			}
			l.Buildings = append(l.Buildings, r)
			keep = append(keep, path)
			doors = append(doors, dr)
			if lk.poi != "" {
				l.POIs[lk.poi] = [2]float64{dr.x, dr.y}
			} else {
				l.Homes = append(l.Homes, [2]float64{dr.x, dr.y})
			}
			// comms 0217/0218 п.3: первый рабочий проход — только корчма. building_id/interior_id
			// стабильны для этого села (один тавern на раскладку), door — на самой стене, не на
			// пороге-подходе снаружи (см. doorOnWall в world_geometry.go).
			if lk.kind == "tavern" {
				bi := len(l.Buildings) - 1
				d := doorOnWall(r, dr.x, dr.y, dr.axisX, tavernDoorWidth)
				l.Buildings[bi].BuildingID = "tavern"
				l.Buildings[bi].Room = "tavern"
				l.Buildings[bi].InteriorID = "tavern:" + sid(id)
				l.Buildings[bi].Door = &d
			}
			break
		}
	}
	if hp, ok := l.POIs["hearth"]; ok { // дом старосты — тоже жильё
		l.Homes = append(l.Homes, hp)
	}
	l.POIs["well"] = [2]float64{0, 0}

	// --- окрестности: вода, лес, поля, рудник — каждому своя сторона ---
	type side struct{ dx, dy float64 }
	sides := []side{{0, 1}, {1, 0}, {0, -1}, {-1, 0}} // север, восток, юг, запад
	riverSide := side{0, -1}
	if ge.Chance(0.5) {
		riverSide = side{0, 1}
	}
	var rest []side
	for _, s := range sides {
		if s != riverSide {
			rest = append(rest, s)
		}
	}
	ge.Shuffle(len(rest), func(i, j int) { rest[i], rest[j] = rest[j], rest[i] })
	woodsSide, fieldSide, mineSide := rest[0], rest[1], rest[2]

	bandNear := R + ge.Uniform(12, 30)
	bandW := ge.Uniform(9, 15)
	bridgeX := math.Round(ge.Uniform(-34, 34))
	if has("river") {
		l.HasRiver = true
		if riverSide.dy < 0 {
			l.RiverY1, l.RiverY0 = -bandNear, -bandNear-bandW
		} else {
			l.RiverY0, l.RiverY1 = bandNear, bandNear+bandW
		}
		l.RiverY0, l.RiverY1 = math.Round(l.RiverY0), math.Round(l.RiverY1)
		l.BridgeX0, l.BridgeX1 = bridgeX-6, bridgeX+6
		bank := l.RiverY1 + 4
		if riverSide.dy > 0 {
			bank = l.RiverY0 - 4
		}
		l.POIs["river"] = [2]float64{bridgeX, bank}
	} else {
		// реки нет: полоса вынесена за край земли (поля v2 остаются осмысленными), а воду даёт пруд
		l.RiverY0, l.RiverY1, l.BridgeX0, l.BridgeX1 = -WorldHalf-40, -WorldHalf-30, -6, 6
		px, py := riverSide.dx*(R+22), riverSide.dy*(R+22)
		px += ge.Uniform(-30, 30)
		l.Zones = append(l.Zones, Zone{"pond", math.Round(px - 9), math.Round(py - 6), math.Round(px + 9), math.Round(py + 6)})
		l.POIs["river"] = [2]float64{math.Round(px), math.Round(py - riverSide.dy*10)}
	}

	// прямоугольник угодья на стороне s: от inner до outer от центра, поперёк — от a до b
	strip := func(s side, inner, outer, a, b float64) (x0, y0, x1, y1 float64) {
		if s.dx != 0 {
			x0, x1 = s.dx*inner, s.dx*outer
			y0, y1 = a, b
		} else {
			y0, y1 = s.dy*inner, s.dy*outer
			x0, x1 = a, b
		}
		return math.Round(math.Min(x0, x1)), math.Round(math.Min(y0, y1)), math.Round(math.Max(x0, x1)), math.Round(math.Max(y0, y1))
	}
	H := WorldHalf
	// лес
	wInner := R + ge.Uniform(10, 24)
	if has("woods") {
		x0, y0, x1, y1 := strip(woodsSide, wInner, H, -H, H)
		l.Zones = append(l.Zones, Zone{"forest", x0, y0, x1, y1})
	} else {
		c := ge.Uniform(-50, 50)
		x0, y0, x1, y1 := strip(woodsSide, wInner, wInner+26, c-16, c+16)
		l.Zones = append(l.Zones, Zone{"copse", x0, y0, x1, y1})
	}
	wz := l.Zones[len(l.Zones)-1]
	l.POIs["woods"] = [2]float64{math.Round((wz.X0+wz.X1)/2*math.Abs(woodsSide.dy) + woodsSide.dx*(wInner+5)),
		math.Round((wz.Y0+wz.Y1)/2*math.Abs(woodsSide.dx) + woodsSide.dy*(wInner+5))}
	if has("woods") { // у сплошного леса опушка напротив села, со сдвигом
		c := ge.Uniform(-40, 40)
		l.POIs["woods"] = [2]float64{math.Round(woodsSide.dx*(wInner+5) + math.Abs(woodsSide.dy)*c),
			math.Round(woodsSide.dy*(wInner+5) + math.Abs(woodsSide.dx)*c)}
	}
	// поля или выгон
	fInner := R + ge.Uniform(6, 14)
	fa := ge.Uniform(-70, -20)
	fb := fa + ge.Uniform(80, 130)
	fx0, fy0, fx1, fy1 := strip(fieldSide, fInner, math.Min(H-4, fInner+ge.Uniform(50, 80)), fa, math.Min(fb, H-4))
	fk := "meadow"
	if has("garden") {
		fk = "field"
	}
	l.Zones = append(l.Zones, Zone{fk, fx0, fy0, fx1, fy1})
	l.POIs["garden"] = [2]float64{math.Round(fieldSide.dx*(fInner+12) + math.Abs(fieldSide.dy)*(fa+fb)/2*0.5),
		math.Round(fieldSide.dy*(fInner+12) + math.Abs(fieldSide.dx)*(fa+fb)/2*0.5)}
	// рудник: в холмах близко, иначе дальше; смещён к углу
	mDist := R + ge.Uniform(40, 62)
	if terrain == "hills" {
		mDist = R + ge.Uniform(18, 30)
	}
	mOff := ge.Uniform(30, 60)
	if ge.Chance(0.5) {
		mOff = -mOff
	}
	mx := math.Round(mineSide.dx*mDist + math.Abs(mineSide.dy)*mOff)
	my := math.Round(mineSide.dy*mDist + math.Abs(mineSide.dx)*mOff)
	mx, my = clampf(mx, -H+14, H-14), clampf(my, -H+14, H-14)
	l.POIs["mine"] = [2]float64{mx, my}
	if has("mine") {
		l.Zones = append(l.Zones, Zone{"rock", mx - 12, my - 12, mx + 12, my + 12})
	}
	// точки угодий не должны оказаться в воде
	for _, k := range []string{"woods", "garden", "mine"} {
		p := l.POIs[k]
		if l.Blocked(p[0], p[1]) {
			x, y := l.Free(p[0], p[1])
			l.POIs[k] = [2]float64{math.Round(x), math.Round(y)}
		}
	}

	// --- дороги: две оси-улицы, объездная, тропы к порогам и угодьям ---
	well := l.addNode("well", 0, 0)
	axis := func(alongX bool) {
		// точки на оси: через каждые 16 м и проекции порогов; затем цепочка по порядку
		pts := []float64{-R, R}
		for v := -48.0; v <= 48; v += 16 {
			if v != 0 && math.Abs(v) < R-4 {
				pts = append(pts, v)
			}
		}
		for _, d := range doors {
			if d.axisX == alongX {
				if alongX {
					pts = append(pts, d.px)
				} else {
					pts = append(pts, d.py)
				}
			}
		}
		pts = append(pts, 0)
		for i := 1; i < len(pts); i++ { // сортировка вставками: точек немного
			for j := i; j > 0 && pts[j] < pts[j-1]; j-- {
				pts[j], pts[j-1] = pts[j-1], pts[j]
			}
		}
		prev, last := -1, math.Inf(-1)
		for _, v := range pts {
			if v != 0 && (math.Abs(v-last) < 0.7 || math.Abs(v) < 0.7) {
				continue // почти та же точка, что предыдущий узел (или колодец)
			}
			if v == 0 && math.Abs(v-last) < 0.7 && prev != -1 {
				// узел рядом с колодцем уже стоит — соединим его с колодцем коротким ребром
				l.addEdge(prev, well)
				prev, last = well, 0
				continue
			}
			last = v
			var n int
			switch {
			case v == 0:
				n = well
			case alongX:
				n = l.addNode("street", v, 0)
			default:
				n = l.addNode("street", 0, v)
			}
			if prev != -1 {
				l.addEdge(prev, n)
			}
			prev = n
		}
	}
	axis(true)
	axis(false)
	// объездная: углы; середины сторон — это концы осей (уже есть)
	find := func(x, y float64) int {
		for i, n := range l.nodes {
			if math.Hypot(n.x-x, n.y-y) < 0.75 {
				return i
			}
		}
		return l.addNode("ring", x, y)
	}
	ringPts := [][2]float64{{R, 0}, {R, R}, {0, R}, {-R, R}, {-R, 0}, {-R, -R}, {0, -R}, {R, -R}}
	for i := range ringPts {
		a, b := ringPts[i], ringPts[(i+1)%len(ringPts)]
		l.addEdge(find(a[0], a[1]), find(b[0], b[1]))
	}
	// пороги — к своей проекции на улице
	for _, d := range doors {
		l.addEdge(l.addNode("door", d.x, d.y), find(d.px, d.py))
	}
	// угодья и берег — к ближайшему узлу, до которого отрезок свободен
	for _, k := range []string{"woods", "garden", "mine", "river"} {
		p := l.POIs[k]
		l.addSpur(k, p[0], p[1])
	}
	if l.HasRiver { // через реку — только по мосту
		bank := l.POIs["river"]
		mid := (l.RiverY0 + l.RiverY1) / 2
		far := l.RiverY0 - 2
		farther := l.RiverY0 - 22
		if riverSide.dy > 0 {
			far, farther = l.RiverY1+2, l.RiverY1+22
		}
		a := find(bank[0], bank[1])
		b := l.addNode("bridge-center", bridgeX, mid)
		c := l.addNode("bridge-far", bridgeX, far)
		l.addEdge(a, b)
		l.addEdge(b, c)
		if math.Abs(farther) < H-4 {
			l.addEdge(c, l.addNode("far-road", bridgeX, farther))
		}
	}

	// --- дикие зоны (откуда подсеваются звери и лихие люди): лес и глухие углы земли ---
	corner := func(sx, sy float64) wildZone {
		return wildZone{math.Min(sx*(R+14), sx*(H-8)), math.Min(sy*(R+14), sy*(H-8)), math.Max(sx*(R+14), sx*(H-8)), math.Max(sy*(R+14), sy*(H-8))}
	}
	forest := wildZone{wz.X0, wz.Y0, wz.X1, wz.Y1}
	if has("woods") { // глубина леса, не самая опушка
		forest = wildZone{math.Max(wz.X0, -H+6), math.Max(wz.Y0, -H+6), math.Min(wz.X1, H-6), math.Min(wz.Y1, H-6)}
	}
	cs := []wildZone{corner(1, 1), corner(-1, 1), corner(-1, -1), corner(1, -1)}
	ge.Shuffle(len(cs), func(i, j int) { cs[i], cs[j] = cs[j], cs[i] })
	l.Wild = map[string][]wildZone{
		"hare":   {forest},
		"fox":    {forest},
		"boar":   {forest, cs[0]},
		"wolf":   {forest, cs[1]},
		"bear":   {cs[2], cs[0]},
		"bandit": {cs[3]},
	}
	return l
}
