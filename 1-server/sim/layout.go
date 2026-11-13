package sim

import (
	"math"
	"sort"
	"sync"

	"pogranichye/core"
)

// Раскладка поселения (Layout) — геометрия ОДНОГО села: здания, река с мостом, точки интереса,
// угодья, дворы жителей и граф дорог. Раньше геометрия была одна на весь мир (все деревни —
// копии стартовой). Теперь у каждого поселения своя раскладка, детерминированно выведенная из
// семени мира, id поселения, его рельефа и ранга (layout_gen.go). Стартовое село (Починок) хранит
// прежнюю ручную раскладку v2 без единого изменения — под неё настроены обучение и клиент.
//
// Координаты раскладки — локальные метры от центра села (+X восток, +Y север). Положение села в
// мире даёт якорь (worldpos.go): мир = якорь + локальная точка.

// Zone — угодье или примета местности: лес, поля, луг, скалы у рудника, пруд. Не препятствие.
type Zone struct {
	Kind string  `json:"kind"` // forest | copse | field | meadow | rock | pond
	X0   float64 `json:"x0"`
	Y0   float64 `json:"y0"`
	X1   float64 `json:"x1"`
	Y1   float64 `json:"y1"`
}

func (z Zone) contains(x, y float64) bool { return x >= z.X0 && x <= z.X1 && y >= z.Y0 && y <= z.Y1 }

// Layout — раскладка одного поселения.
type Layout struct {
	Plan        string // starter | ring | street | cross | scatter
	Half        float64
	VillageHalf float64
	Buildings   []Rect
	HasRiver    bool
	RiverY0     float64
	RiverY1     float64
	BridgeX0    float64
	BridgeX1    float64
	POIs        map[string][2]float64
	Zones       []Zone
	Homes       [][2]float64 // пороги изб: сюда жители идут спать и отдыхать
	Wild        map[string][]wildZone
	// Open — бесшовный мир: за краем земли села не стена, а открытая местность (worldpos.go).
	Open bool

	nodes []navNode
	adj   [][]int
}

// UniformLayouts — предохранитель: все поселения получают стартовую раскладку, как было до v1.19.
// Включается флагом сервера, если клиент ещё не умеет рисовать разные деревни.
var UniformLayouts bool

// Blocked — непроходима ли точка: за краем земли (если мир не бесшовный), в здании или в реке.
func (l *Layout) Blocked(x, y float64) bool {
	if !l.Open && (x < -l.Half || x > l.Half || y < -l.Half || y > l.Half) {
		return true
	}
	for i := range l.Buildings {
		if l.Buildings[i].contains(x, y) {
			return true
		}
	}
	if l.HasRiver && y >= l.RiverY0 && y <= l.RiverY1 && x >= -l.Half && x <= l.Half {
		if x < l.BridgeX0 || x > l.BridgeX1 { // мост — единственный проход
			return true
		}
	}
	return false
}

// ResolveMove — коллизия скольжением: цель свободна — туда; иначе вдоль одной из осей; иначе стоим.
func (l *Layout) ResolveMove(ox, oy, nx, ny float64) (float64, float64) {
	if !l.Blocked(nx, ny) {
		return nx, ny
	}
	if !l.Blocked(nx, oy) {
		return nx, oy
	}
	if !l.Blocked(ox, ny) {
		return ox, ny
	}
	return ox, oy
}

// POI — доступная точка интереса данного вида (или колодец по умолчанию).
func (l *Layout) POI(kind string) (x, y float64) {
	if p, ok := l.POIs[kind]; ok {
		return p[0], p[1]
	}
	p := l.POIs["well"]
	return p[0], p[1]
}

// InWoods — стоит ли точка в лесу (там рубят).
func (l *Layout) InWoods(x, y float64) bool {
	for _, z := range l.Zones {
		if (z.Kind == "forest" || z.Kind == "copse") && z.contains(x, y) {
			return true
		}
	}
	return false
}

// NearWater — стоит ли точка у воды (там удят): на берегу реки или у пруда.
func (l *Layout) NearWater(x, y float64) bool {
	if l.HasRiver && x >= -l.Half && x <= l.Half &&
		((y >= l.RiverY1-6 && y <= l.RiverY1+6) || (y >= l.RiverY0-6 && y <= l.RiverY0+6)) {
		return true
	}
	for _, z := range l.Zones {
		if z.Kind == "pond" && x >= z.X0-5 && x <= z.X1+5 && y >= z.Y0-5 && y <= z.Y1+5 {
			return true
		}
	}
	return false
}

// InCore — внутри ли ядра села (зверьё туда не заходит).
func (l *Layout) InCore(x, y float64) bool {
	return math.Abs(x) <= l.VillageHalf && math.Abs(y) <= l.VillageHalf
}

// Entry — где встаёт вселившийся игрок: у колодца, на свободном месте.
func (l *Layout) Entry() (float64, float64) {
	wx, wy := l.POI("well")
	for _, d := range [][2]float64{{0, -4}, {0, 4}, {4, 0}, {-4, 0}, {0, 0}} {
		if !l.Blocked(wx+d[0], wy+d[1]) {
			return wx + d[0], wy + d[1]
		}
	}
	return wx, wy
}

// Free — ближайшая к (x,y) проходимая точка (спираль). Нужна, чтобы вытащить тело из стены после
// смены раскладки (старое сохранение, село выросло в город).
func (l *Layout) Free(x, y float64) (float64, float64) {
	if !l.Blocked(x, y) {
		return x, y
	}
	for r := 1.0; r <= 60; r += 1 {
		for a := 0; a < 16; a++ {
			ang := float64(a) * math.Pi / 8
			if nx, ny := x+r*math.Cos(ang), y+r*math.Sin(ang); !l.Blocked(nx, ny) {
				return nx, ny
			}
		}
	}
	return l.Entry()
}

// HomeOf — порог избы жителя: домочадцы распределены по избам села устойчиво (по id).
func (l *Layout) HomeOf(id uint64) (float64, float64) {
	if len(l.Homes) == 0 {
		return l.POI("hearth")
	}
	h := l.Homes[int((id*2654435761>>7)%uint64(len(l.Homes)))]
	return h[0], h[1]
}

// SpawnSpot — детерминированное начальное место жителя в ядре села, не в стене.
func (l *Layout) SpawnSpot(id uint64) (x, y float64) {
	h := id*40503 + 12345
	ang := float64(h%360) * math.Pi / 180
	rad := 8 + float64((h/360)%380)/10.0*(l.VillageHalf/50)
	x, y = rad*math.Cos(ang), rad*math.Sin(ang)
	for i := 0; i < 8 && l.Blocked(x, y); i++ {
		rad -= 5
		if rad < 2 {
			return l.Entry()
		}
		x, y = rad*math.Cos(ang), rad*math.Sin(ang)
	}
	return x, y
}

// segmentClear — свободен ли прямой отрезок (проверка шагом в метр).
func (l *Layout) segmentClear(x0, y0, x1, y1 float64) bool {
	d := math.Hypot(x1-x0, y1-y0)
	n := int(d) + 1
	for i := 0; i <= n; i++ {
		t := float64(i) / float64(n)
		if l.Blocked(x0+(x1-x0)*t, y0+(y1-y0)*t) {
			return false
		}
	}
	return true
}

// --- граф дорог раскладки ---

func (l *Layout) addNode(name string, x, y float64) int {
	l.nodes = append(l.nodes, navNode{name, x, y})
	l.adj = append(l.adj, nil)
	return len(l.nodes) - 1
}

func (l *Layout) addEdge(a, b int) {
	if a == b {
		return
	}
	for _, v := range l.adj[a] {
		if v == b {
			return
		}
	}
	l.adj[a] = append(l.adj[a], b)
	l.adj[b] = append(l.adj[b], a)
}

// addSpur — узел в точке (дверь, POI) и тропа от него к ближайшему узлу, до которого отрезок свободен.
func (l *Layout) addSpur(name string, x, y float64) int {
	best, bd, any, ad := -1, math.MaxFloat64, -1, math.MaxFloat64
	for i, n := range l.nodes {
		d := math.Hypot(n.x-x, n.y-y)
		if d < ad {
			ad, any = d, i
		}
		if d < bd && l.segmentClear(n.x, n.y, x, y) {
			bd, best = d, i
		}
	}
	if best == -1 {
		best = any
	}
	if best != -1 && math.Hypot(l.nodes[best].x-x, l.nodes[best].y-y) < 0.5 {
		return best
	}
	id := l.addNode(name, x, y)
	if best != -1 {
		l.addEdge(id, best)
	}
	return id
}

// nearestNode — ближайший узел, до которого можно дойти по прямой (иначе просто ближайший).
func (l *Layout) nearestNode(x, y float64) int {
	best, bd, any, ad := -1, math.MaxFloat64, 0, math.MaxFloat64
	for i, n := range l.nodes {
		d := math.Hypot(n.x-x, n.y-y)
		if d < ad {
			ad, any = d, i
		}
		if d < bd && l.segmentClear(x, y, n.x, n.y) {
			bd, best = d, i
		}
	}
	if best == -1 {
		return any
	}
	return best
}

// route — Dijkstra по графу дорог; цепочка индексов узлов (включая goal) или nil.
func (l *Layout) route(start, goal int) []int {
	if start == goal {
		return []int{goal}
	}
	const inf = math.MaxFloat64
	n := len(l.nodes)
	dist, prev, done := make([]float64, n), make([]int, n), make([]bool, n)
	for i := range dist {
		dist[i], prev[i] = inf, -1
	}
	dist[start] = 0
	for {
		u, ud := -1, inf
		for i := 0; i < n; i++ {
			if !done[i] && dist[i] < ud {
				ud, u = dist[i], i
			}
		}
		if u == -1 || u == goal {
			break
		}
		done[u] = true
		for _, v := range l.adj[u] {
			if nd := dist[u] + math.Hypot(l.nodes[u].x-l.nodes[v].x, l.nodes[u].y-l.nodes[v].y); nd < dist[v] {
				dist[v], prev[v] = nd, u
			}
		}
	}
	if prev[goal] == -1 {
		return nil
	}
	var path []int
	for at := goal; at != -1; at = prev[at] {
		path = append([]int{at}, path...)
		if at == start {
			break
		}
	}
	return path
}

// JSON — геометрия поселения для клиента (/api/layout). Поля v2 (version, world_half, village_half,
// buildings, river, pois) сохранены как были; новые — добавлены рядом.
func (l *Layout) JSON() map[string]any {
	pois := map[string]map[string]float64{}
	for k, p := range l.POIs {
		pois[k] = map[string]float64{"x": p[0], "y": p[1]}
	}
	roads := [][4]float64{}
	for a, vs := range l.adj {
		for _, b := range vs {
			if a < b {
				roads = append(roads, [4]float64{l.nodes[a].x, l.nodes[a].y, l.nodes[b].x, l.nodes[b].y})
			}
		}
	}
	homes := []map[string]float64{}
	for _, h := range l.Homes {
		homes = append(homes, map[string]float64{"x": h[0], "y": h[1]})
	}
	zones := l.Zones
	if zones == nil {
		zones = []Zone{}
	}
	ex, ey := l.Entry()
	return map[string]any{
		"version":      GeometryVersion,
		"world_half":   l.Half,
		"village_half": l.VillageHalf,
		"buildings":    l.Buildings,
		"river":        map[string]float64{"y0": l.RiverY0, "y1": l.RiverY1, "bridge_x0": l.BridgeX0, "bridge_x1": l.BridgeX1},
		"pois":         pois,
		// v1.19, аддитивно:
		"plan":      l.Plan,
		"has_river": l.HasRiver,
		"zones":     zones,
		"roads":     roads,
		"homes":     homes,
		"entry":     map[string]float64{"x": ex, "y": ey},
		"open":      l.Open,
	}
}

// --- стартовая раскладка: прежняя геометрия v2 без изменений ---

var (
	starterOnce sync.Once
	starterL    *Layout
)

// StarterLayout — раскладка стартового села (Починок): ровно геометрия v2.
func StarterLayout() *Layout {
	starterOnce.Do(func() {
		l := &Layout{Plan: "starter", Half: WorldHalf, VillageHalf: VillageHalf,
			Buildings: Buildings, HasRiver: true,
			RiverY0: riverY0, RiverY1: riverY1, BridgeX0: bridgeX0, BridgeX1: bridgeX1,
			POIs: POINamed,
			Zones: []Zone{
				{"forest", -WorldHalf, -WorldHalf, -70, WorldHalf},
				{"field", 59, -120, 147, 140},
				{"meadow", -50, 80, 66, 144},
				{"rock", 78, 28, 102, 52},
			},
			Homes: [][2]float64{{33, -32}, {-34, -32}, {-8, -36}, {12, -38}},
			Wild:  wildZones,
		}
		for _, n := range navNodes {
			l.addNode(n.name, n.x, n.y)
		}
		for _, e := range navEdgeNames {
			l.addEdge(navIdx[e[0]], navIdx[e[1]])
		}
		// южный проулок вдоль изб: к порогам подходят по нему, а не сквозь углы домов
		lane := l.addNode("south-lane", 0, -42)
		l.addEdge(navIdx["well"], lane)
		l.addEdge(lane, navIdx["river-approach"])
		w := l.addNode("south-lane-west", -34, -42)
		e := l.addNode("south-lane-east", 33, -42)
		l.addEdge(lane, w)
		l.addEdge(lane, e)
		l.addEdge(e, navIdx["hearth-south-gate"])
		for i, h := range l.Homes {
			l.addSpur("home-"+sid(core.ID(i)), h[0], h[1])
		}
		starterL = l
	})
	return starterL
}

// --- раскладка поселения мира ---

type layoutKey struct {
	seed string
	id   core.ID
}

type layoutEntry struct {
	rank                 int
	tier, terrain, feats string
	l                    *Layout
}

var (
	layoutMu    sync.Mutex
	layoutCache = map[layoutKey]*layoutEntry{}
)

// cauldronRank — порядковый номер поселения среди сёл «котла» (по id); -1 — не в котле.
// Новые сёла получают большие id, поэтому ранг уже существующих не меняется.
func cauldronRank(w *core.World, id core.ID) int {
	var ids []core.ID
	w.Each("settlement", func(o *core.Entity) {
		if s := core.Settle(o); s != nil && !isOuter(w, s) {
			ids = append(ids, o.ID)
		}
	})
	sort.Slice(ids, func(i, j int) bool { return ids[i] < ids[j] })
	for i, x := range ids {
		if x == id {
			return i
		}
	}
	return -1
}

// LayoutOf — раскладка поселения. Первая деревня котла — стартовая (ручная v2); прочие сгенерированы
// и запомнены. Раскладка пересобирается, только если село сменило величину, рельеф или угодья.
func LayoutOf(w *core.World, settlement core.ID) *Layout {
	if w == nil || settlement == 0 || UniformLayouts {
		return StarterLayout()
	}
	se := w.Get(settlement)
	if se == nil || core.Settle(se) == nil {
		return StarterLayout()
	}
	s := core.Settle(se)
	site := SiteOf(w, se)
	feats, terrain := "", ""
	if site != nil {
		terrain = site.Terrain
		for _, f := range []string{"garden", "mine", "river", "woods"} {
			if site.Features[f] {
				feats += f[:1]
			}
		}
	}
	key := layoutKey{w.Seed, settlement}
	layoutMu.Lock()
	defer layoutMu.Unlock()
	en := layoutCache[key]
	if en == nil {
		en = &layoutEntry{rank: cauldronRank(w, settlement)}
		layoutCache[key] = en
	}
	if en.l == nil || en.tier != s.Tier || en.terrain != terrain || en.feats != feats {
		en.tier, en.terrain, en.feats = s.Tier, terrain, feats
		if en.rank == 0 {
			en.l = StarterLayout()
		} else {
			en.l = generateLayout(w.Seed, settlement, s.Tier, terrain, site, planFor(w.Seed, settlement, en.rank, terrain))
		}
	}
	if open := Seamless(w); en.l.Open != open {
		if en.rank == 0 { // стартовая раскладка общая для всех миров процесса — открытой делаем копию
			c := *StarterLayout()
			c.Open = open
			en.l = &c
		} else {
			en.l.Open = open
		}
	}
	return en.l
}

// layoutFor — раскладка поселения, где сейчас находится тело (в пути — стартовая как нейтральная).
func layoutFor(w *core.World, e *core.Entity) *Layout {
	if e != nil {
		if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
			return LayoutOf(w, pl.Settlement)
		}
	}
	return LayoutOf(w, 0)
}

// LayoutFor — то же для сервера (кадр, обработчики).
func LayoutFor(w *core.World, e *core.Entity) *Layout { return layoutFor(w, e) }

// Seamless — бесшовный ли мир: земли сёл не огорожены краем сцены (см. worldpos.go).
func Seamless(w *core.World) bool {
	var p *PlanetC
	w.Each("planet", func(e *core.Entity) { p, _ = e.Get("planet").(*PlanetC) })
	return p != nil && p.Seamless
}
