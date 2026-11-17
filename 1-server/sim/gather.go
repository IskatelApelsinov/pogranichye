package sim

import (
	"math"
	"sort"
	"strconv"
	"strings"

	"pogranichye/core"
	"pogranichye/econ"
)

// Находки под ногами (v1.30): травы, грибы, ягоды, дикие ульи с мёдом, сухостой, перья, дикий лён.
// Раньше собирать можно было только в урочищах (одно место на ~1,2 км). Теперь по всему полю и лесу
// лежат мелкие находки — каждая на своей клетке в 40 м, где она есть или нет по зерну мира и лесистости
// края. У каждой свой запас: взял — убыло, за сутки отрастает. Запас общий для всех игроков. Это
// настоящие серверные узлы с id (comms 0283: «gatherable nodes»), а не декоративная трава.

const (
	gatherCell   = 40.0 // м: клетка находок
	gatherReach  = 3.0  // м: столько до находки, чтобы её взять
	gatherNear   = 45.0 // м: находки в кадре игрока
	gatherRegen  = 1.0  // единиц в сутки отрастает
	gatherEnergy = 2.0  // сил на один сбор
	gatherClearR = 260  // м: у самого села находки уже обобраны жителями
)

type gatherKind struct {
	ID, Name, Good, GoodName string
	Max                      float64
	Forest                   float64 // насколько чаще в лесу (0 — всё равно где)
	Weight                   float64 // насколько часто вообще
}

var gatherKinds = []gatherKind{
	{"herb", "Зверобой", econ.GoodHerb, "травы", 3, 0.3, 1.0},
	{"mushroom", "Грибы", econ.GoodVeg, "грибы", 3, 0.9, 0.8},
	{"berries", "Ягодный куст", econ.GoodVeg, "ягоды", 3, 0.5, 0.9},
	{"hive", "Дикий улей", econ.GoodHoney, "мёд", 2, 0.8, 0.25},
	{"deadwood", "Сухостой", econ.GoodBrushwood, "хворост", 3, 0.9, 0.7},
	{"nest", "Гнездо", econ.GoodFeather, "перья", 2, 0.4, 0.3},
	{"flax", "Дикий лён", econ.GoodFlax, "лён", 2, -0.5, 0.4},
}

// GatherNode — одна находка: где лежит (мир, м) и что это.
type GatherNode struct {
	ID   string
	Kind *gatherKind
	E, N float64
}

// GatherC — общий запас находок мира (один на мир): сколько взято и в какой день.
type GatherC struct {
	Taken map[string]gatherTake `json:"taken"`
	Req   map[string]bool       `json:"-"`
}

type gatherTake struct {
	Left float64 `json:"left"`
	Day  int     `json:"day"`
}

func (c *GatherC) Kind() string { return "gather" }

func init() { core.Register("gather", func() core.Component { return &GatherC{} }) }

func gatherStore(w *core.World) *GatherC {
	var g *GatherC
	w.Each("gather", func(e *core.Entity) {
		if g == nil {
			g, _ = e.Get("gather").(*GatherC)
		}
	})
	if g == nil {
		g = &GatherC{Taken: map[string]gatherTake{}}
		w.Spawn(g)
	}
	if g.Taken == nil {
		g.Taken = map[string]gatherTake{}
	}
	return g
}

func peekGather(w *core.World) *GatherC {
	var g *GatherC
	w.Each("gather", func(e *core.Entity) {
		if g == nil {
			g, _ = e.Get("gather").(*GatherC)
		}
	})
	return g
}

func gatherLeft(g *GatherC, n *GatherNode, day int) float64 {
	if g == nil {
		return n.Kind.Max
	}
	t, ok := g.Taken[n.ID]
	if !ok {
		return n.Kind.Max
	}
	return math.Min(n.Kind.Max, t.Left+gatherRegen*float64(maxi(0, day-t.Day)))
}

func maxi(a, b int) int {
	if a > b {
		return a
	}
	return b
}

// gatherCellNode — находка клетки (cx,cy) или ничего. Чистая функция зерна мира.
func gatherCellNode(w *core.World, t *Terrain, cx, cy int64) (GatherNode, bool) {
	roll := lattice(t.seed+301, cx, cy)
	cxm, cym := (float64(cx)+0.5)*gatherCell, (float64(cy)+0.5)*gatherCell
	forest := coverProfile(t.Biome(cxm, cym))[0]
	if roll > 0.18+0.22*forest { // в лесу находок больше, в степи — реже
		return GatherNode{}, false
	}
	e := (float64(cx) + 0.15 + 0.7*lattice(t.seed+302, cx, cy)) * gatherCell
	n := (float64(cy) + 0.15 + 0.7*lattice(t.seed+303, cx, cy)) * gatherCell
	if d := nearestAnchorDist(t, e, n); d < gatherClearR {
		return GatherNode{}, false
	}
	if TrailAt(w, e, n) != nil {
		return GatherNode{}, false // на тропе всё давно подобрано
	}
	total := 0.0
	weights := make([]float64, len(gatherKinds))
	for i, k := range gatherKinds {
		weights[i] = math.Max(0.02, k.Weight*(1+k.Forest*(forest-0.3)*2))
		total += weights[i]
	}
	pick := lattice(t.seed+304, cx, cy) * total
	for i := range gatherKinds {
		if pick -= weights[i]; pick <= 0 || i == len(gatherKinds)-1 {
			return GatherNode{ID: "g:" + i64(cx) + ":" + i64(cy), Kind: &gatherKinds[i], E: e, N: n}, true
		}
	}
	return GatherNode{}, false
}

func nearestAnchorDist(t *Terrain, e, n float64) float64 {
	best := math.Inf(1)
	for _, a := range t.anchors {
		best = math.Min(best, math.Hypot(e-a[0], n-a[1]))
	}
	return best
}

// GatherNodesNear — находки не дальше r от мировой точки, ближние первыми.
func GatherNodesNear(w *core.World, t *Terrain, e, n, r float64) []GatherNode {
	var out []GatherNode
	c0x, c1x := int64(math.Floor((e-r)/gatherCell)), int64(math.Floor((e+r)/gatherCell))
	c0y, c1y := int64(math.Floor((n-r)/gatherCell)), int64(math.Floor((n+r)/gatherCell))
	for cx := c0x; cx <= c1x; cx++ {
		for cy := c0y; cy <= c1y; cy++ {
			if g, ok := gatherCellNode(w, t, cx, cy); ok && math.Hypot(g.E-e, g.N-n) <= r {
				out = append(out, g)
			}
		}
	}
	sort.Slice(out, func(i, j int) bool {
		return math.Hypot(out[i].E-e, out[i].N-n) < math.Hypot(out[j].E-e, out[j].N-n)
	})
	return out
}

// GatherNodeByID — находка по id ("g:<cx>:<cy>").
func GatherNodeByID(w *core.World, t *Terrain, id string) (GatherNode, bool) {
	parts := strings.Split(id, ":")
	if len(parts) != 3 || parts[0] != "g" {
		return GatherNode{}, false
	}
	cx, err1 := strconv.ParseInt(parts[1], 10, 64)
	cy, err2 := strconv.ParseInt(parts[2], 10, 64)
	if err1 != nil || err2 != nil || cx < -1<<40 || cx > 1<<40 || cy < -1<<40 || cy > 1<<40 {
		return GatherNode{}, false
	}
	return gatherCellNode(w, t, cx, cy)
}

func gatherDay(w *core.World) int {
	if u := peekWildUse(w); u != nil {
		return u.Today
	}
	return 0
}

// PickUp — взять находку (op:"gather"): рядом (3 м), есть запас, есть силы. Дикий улей без огня в
// руке жалит.
func PickUp(w *core.World, e *core.Entity, nodeID, reqid string) (bool, string) {
	g := gatherStore(w)
	if reqid != "" {
		if g.Req == nil {
			g.Req = map[string]bool{}
		}
		if g.Req[reqid] {
			return true, "уже собрано"
		}
	}
	t := terrainFor(w)
	node, ok := GatherNodeByID(w, t, nodeID)
	if !ok {
		return false, "здесь нечего собрать"
	}
	x, y, has := bodyWorldXY(w, e)
	if !has || OnRoad(e) {
		return false, "ты в пути"
	}
	if math.Hypot(node.E-x, node.N-y) > gatherReach {
		return false, "слишком далеко — подойди ближе"
	}
	v := core.Vitals(e)
	if v != nil && v.Energy < gatherEnergy {
		return false, "нет сил — сначала отдохни"
	}
	day := gatherDay(w)
	left := gatherLeft(g, &node, day)
	if left < 1 {
		return false, node.Kind.Name + ": всё уже собрано, отрастёт за сутки"
	}
	take := 1.0
	if node.Kind.ID != "hive" && left >= 2 && hashReq(nodeID+reqid)%3 == 0 {
		take = 2
	}
	g.Taken[node.ID] = gatherTake{Left: left - take, Day: day}
	addStash(w, e, node.Kind.Good, take)
	if v != nil {
		v.Energy -= gatherEnergy
	}
	msg := node.Kind.Name + ": +" + strconv.Itoa(int(take)) + " " + node.Kind.GoodName
	if node.Kind.ID == "hive" && !hasHeldLight(e) && v != nil {
		v.Health = math.Max(1, v.Health-6)
		msg += " — пчёлы покусали (с огнём в руке они бы не тронули)"
	}
	trainSkill(w, e, "herbalist", 0.1)
	if reqid != "" {
		if len(g.Req) > 2048 {
			g.Req = map[string]bool{}
		}
		g.Req[reqid] = true
	}
	Emit(w, e, FeedItem{Kind: "gather", Text: msg})
	return true, msg
}

// GatherView — place.gather: находки рядом с игроком (до 45 м), что даёт, сколько осталось.
func GatherView(w *core.World, t *Terrain, e *core.Entity) []map[string]any {
	out := []map[string]any{}
	x, y, ok := bodyWorldXY(w, e)
	pl := core.Place(e)
	if !ok || pl == nil || OnRoad(e) {
		return out
	}
	s := settleOf(w, pl.Settlement)
	if s == nil {
		return out
	}
	ae, an := Anchor(w, s)
	g, day := peekGather(w), gatherDay(w)
	for _, n := range GatherNodesNear(w, t, x, y, gatherNear) {
		d := math.Hypot(n.E-x, n.N-y)
		out = append(out, map[string]any{"id": n.ID, "kind": n.Kind.ID, "name": n.Kind.Name, "good": n.Kind.Good,
			"good_name": n.Kind.GoodName, "left": math.Floor(gatherLeft(g, &n, day)), "max": n.Kind.Max,
			"e": r3(n.E), "n": r3(n.N), "u": r3(t.Height(n.E, n.N)), "x": r3(n.E - ae), "y": r3(n.N - an),
			"dist": math.Round(d*10) / 10, "dir": compass(n.E-x, n.N-y), "here": d <= gatherReach})
	}
	return out
}

// chunkGather — находки чанка для /api/terrain (метры от угла чанка, как у roads/resources).
func chunkGather(w *core.World, t *Terrain, oe, on float64) []map[string]any {
	out := []map[string]any{}
	g, day := peekGather(w), gatherDay(w)
	c0x, c0y := int64(math.Floor(oe/gatherCell)), int64(math.Floor(on/gatherCell))
	c1x, c1y := int64(math.Floor((oe+ChunkMeters-1e-9)/gatherCell)), int64(math.Floor((on+ChunkMeters-1e-9)/gatherCell))
	for cx := c0x; cx <= c1x; cx++ {
		for cy := c0y; cy <= c1y; cy++ {
			n, ok := gatherCellNode(w, t, cx, cy)
			if !ok || n.E < oe || n.E >= oe+ChunkMeters || n.N < on || n.N >= on+ChunkMeters {
				continue
			}
			out = append(out, map[string]any{"id": n.ID, "kind": n.Kind.ID, "good": n.Kind.Good,
				"x": r3(n.E - oe), "z": r3(n.N - on), "left": math.Floor(gatherLeft(g, &n, day))})
		}
	}
	return out
}
