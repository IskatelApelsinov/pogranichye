package sim

import (
	"encoding/json"
	"fmt"
	"math"
	"reflect"
	"testing"

	"pogranichye/core"
)

// Раскладки поселений (v1.19): у каждого села своя геометрия, и каждая обязана быть проходимой.

var needPOI = []string{"tavern", "smithy", "granary", "market", "well", "hearth", "garden", "river", "woods", "mine"}

// checkLayout — инварианты любой раскладки: дворы не пересекаются и не лезут на улицы, все точки
// интереса и пороги доступны, рёбра дорог не идут сквозь стены и воду, до всего есть маршрут.
func checkLayout(t *testing.T, name string, l *Layout) {
	t.Helper()
	for i, a := range l.Buildings {
		if a.X0 >= a.X1 || a.Y0 >= a.Y1 || a.X0 < -l.Half || a.X1 > l.Half || a.Y0 < -l.Half || a.Y1 > l.Half {
			t.Fatalf("%s: кривой двор %+v", name, a)
		}
		for j, b := range l.Buildings {
			if i < j && rectsOverlap(a, b, 0) {
				t.Fatalf("%s: дворы пересекаются: %+v и %+v", name, a, b)
			}
		}
	}
	for _, k := range needPOI {
		p, ok := l.POIs[k]
		if !ok {
			t.Fatalf("%s: нет точки %s", name, k)
		}
		if l.Blocked(p[0], p[1]) {
			t.Fatalf("%s: точка %s в стене или в воде: %v", name, k, p)
		}
		if math.Abs(p[0]) > l.Half-4 || math.Abs(p[1]) > l.Half-4 {
			t.Fatalf("%s: точка %s у самого края земли: %v", name, k, p)
		}
	}
	if len(l.Homes) < 2 {
		t.Fatalf("%s: изб меньше двух (%d)", name, len(l.Homes))
	}
	if x, y := l.Entry(); l.Blocked(x, y) {
		t.Fatalf("%s: место вселения занято", name)
	}
	for a, vs := range l.adj {
		for _, b := range vs {
			na, nb := l.nodes[a], l.nodes[b]
			if !l.segmentClear(na.x, na.y, nb.x, nb.y) {
				t.Fatalf("%s: дорога %s(%.0f,%.0f)–%s(%.0f,%.0f) идёт сквозь преграду", name, na.name, na.x, na.y, nb.name, nb.x, nb.y)
			}
		}
	}
	well := l.nearestNode(l.POI("well"))
	targets := [][2]float64{}
	for _, k := range needPOI {
		targets = append(targets, l.POIs[k])
	}
	targets = append(targets, l.Homes...)
	for _, p := range targets {
		g := l.nearestNode(p[0], p[1])
		if l.route(well, g) == nil {
			t.Fatalf("%s: от колодца нет дороги к (%.0f,%.0f)", name, p[0], p[1])
		}
		n := l.nodes[g]
		if math.Hypot(n.x-p[0], n.y-p[1]) > 0.6 && !l.segmentClear(n.x, n.y, p[0], p[1]) {
			t.Fatalf("%s: последний отрезок к (%.0f,%.0f) от узла %s закрыт", name, p[0], p[1], n.name)
		}
	}
	for sp, zs := range l.Wild {
		for _, z := range zs {
			if z.x0 > z.x1 || z.y0 > z.y1 {
				t.Fatalf("%s: кривая дикая зона %s %+v", name, sp, z)
			}
		}
	}
	// Угодья не должны вылетать из своей зоны (comms 0215 — гипотеза ChatGPT: общий фолбэк Free()
	// в конце generateLayout ищет свободную точку ГЛОБАЛЬНО в селе, не внутри исходной зоны, так что
	// точка, отведённая от воды/стены, может оказаться вне леса/поля/рудника). В раскладке всегда
	// РОВНО одна зона из {forest,copse} и одна из {field,meadow} (какая — зависит от feature), rock —
	// только когда есть рудник: достаточно проверить принадлежность без знания самих feature-флагов.
	if _, ok := zoneContaining(l, l.POIs["woods"], "forest", "copse"); !ok {
		t.Fatalf("%s: точка woods %v вне леса/рощицы", name, l.POIs["woods"])
	}
	if _, ok := zoneContaining(l, l.POIs["garden"], "field", "meadow"); !ok {
		t.Fatalf("%s: точка garden %v вне поля/выгона", name, l.POIs["garden"])
	}
	for _, z := range l.Zones {
		if z.Kind == "rock" {
			if _, ok := zoneContaining(l, l.POIs["mine"], "rock"); !ok {
				t.Fatalf("%s: точка mine %v вне рудной зоны %+v", name, l.POIs["mine"], z)
			}
			break
		}
	}
}

// zoneContaining — зона среди l.Zones с кодом из kinds, которая содержит точку p (если такая есть).
func zoneContaining(l *Layout, p [2]float64, kinds ...string) (Zone, bool) {
	for _, z := range l.Zones {
		for _, k := range kinds {
			if z.Kind == k && z.contains(p[0], p[1]) {
				return z, true
			}
		}
	}
	return Zone{}, false
}

func TestStarterLayoutIsLegacyV2(t *testing.T) {
	l := StarterLayout()
	checkLayout(t, "стартовая", l)
	j := l.JSON()
	if j["version"] != GeometryVersion || j["world_half"] != WorldHalf || j["village_half"] != VillageHalf {
		t.Fatal("поля v2 стартовой раскладки изменились")
	}
	if !reflect.DeepEqual(j["buildings"], Buildings) || len(Buildings) != 8 {
		t.Fatal("здания стартового села должны остаться прежними (клиент ждёт ровно восемь)")
	}
	r := j["river"].(map[string]float64)
	if r["y0"] != -90 || r["y1"] != -78 || r["bridge_x0"] != -6 || r["bridge_x1"] != 6 {
		t.Fatalf("река стартового села изменилась: %v", r)
	}
	for k, p := range POINamed {
		if l.POIs[k] != p {
			t.Fatalf("точка %s стартового села сдвинулась", k)
		}
	}
	// старое поведение геометрии сохранено точь-в-точь
	for _, c := range []struct {
		x, y float64
		want bool
	}{{0, 0, false}, {-33, 20, true}, {0, -84, false}, {30, -84, true}, {151, 0, true}, {0, -151, true}} {
		if l.Blocked(c.x, c.y) != c.want {
			t.Fatalf("Blocked(%v,%v) != %v", c.x, c.y, c.want)
		}
	}
}

func TestGeneratedLayoutsAreWalkable(t *testing.T) {
	terrains := []string{"river", "hills", "forest", "plain", "coast", "marsh"}
	n := 0
	for seed := 0; seed < 12; seed++ {
		for id := 3; id < 40; id += 3 {
			for ti, tier := range []string{"hamlet", "village", "town", "city"} {
				terrain := terrains[(seed+id+ti)%len(terrains)]
				site := &SiteC{Terrain: terrain, Features: map[string]bool{}}
				for _, f := range terrainFeatures[terrain] {
					site.Features[f] = true
				}
				plan := layoutPlans[(seed+id)%len(layoutPlans)]
				name := fmt.Sprintf("семя %d село %d %s %s %s", seed, id, tier, terrain, plan)
				l := generateLayout(fmt.Sprint("s", seed), core.ID(id), tier, terrain, site, plan)
				checkLayout(t, name, l)
				if len(l.Buildings) < 5+2 {
					t.Fatalf("%s: поставлено всего %d дворов", name, len(l.Buildings))
				}
				if site.Features["river"] != l.HasRiver {
					t.Fatalf("%s: река в раскладке расходится с угодьями места", name)
				}
				n++
			}
		}
	}
	if n < 500 {
		t.Fatalf("проверено мало раскладок: %d", n)
	}
}

// TestGeneratedLayoutPOIZonesContained — ответ на comms 0215 (гипотеза ChatGPT про фолбэк Free()):
// широкий детерминированный прогон (seed×terrain×tier×plan), только чтобы дать «сотни/тысячи seeds»
// числом, а не глубиной — сама проверка уже и так идёт в checkLayout на каждом вызове.
func TestGeneratedLayoutPOIZonesContained(t *testing.T) {
	terrains := []string{"river", "hills", "forest", "plain", "coast", "marsh"}
	n := 0
	for seed := 0; seed < 60; seed++ {
		for id := 1; id < 20; id++ {
			for ti, tier := range []string{"hamlet", "village", "town", "city"} {
				terrain := terrains[(seed*7+id*3+ti)%len(terrains)]
				site := &SiteC{Terrain: terrain, Features: map[string]bool{}}
				for _, f := range terrainFeatures[terrain] {
					site.Features[f] = true
				}
				plan := layoutPlans[(seed+id+ti)%len(layoutPlans)]
				name := fmt.Sprintf("семя %d село %d %s %s %s", seed, id, tier, terrain, plan)
				l := generateLayout(fmt.Sprint("zc", seed), core.ID(id), tier, terrain, site, plan)
				checkLayout(t, name, l)
				n++
			}
		}
	}
	if n < 1000 {
		t.Fatalf("проверено мало раскладок: %d (нужно хотя бы 1000)", n)
	}
}

func TestLayoutIsDeterministic(t *testing.T) {
	site := &SiteC{Terrain: "river", Features: map[string]bool{"river": true, "woods": true, "garden": true}}
	a, _ := json.Marshal(generateLayout("w", 77, "village", "river", site, "cross").JSON())
	b, _ := json.Marshal(generateLayout("w", 77, "village", "river", site, "cross").JSON())
	if string(a) != string(b) {
		t.Fatal("одна и та же раскладка должна получаться из одних входов")
	}
	c, _ := json.Marshal(generateLayout("w", 78, "village", "river", site, "cross").JSON())
	if string(a) == string(c) {
		t.Fatal("у другого села — другая раскладка")
	}
}

// Главное требование: деревни котла РАЗНЫЕ — планом, застройкой и окрестностями.
func TestCauldronVillagesAllDiffer(t *testing.T) {
	for _, seed := range []string{"пограничье-1", "diag", "t-демо"} {
		w, _ := GenerateStarter(seed)
		var ls []*Layout
		var names []string
		w.Each("settlement", func(se *core.Entity) {
			if !isOuter(w, core.Settle(se)) {
				ls = append(ls, LayoutOf(w, se.ID))
				names = append(names, core.Settle(se).Name)
			}
		})
		if len(ls) < 4 {
			t.Fatalf("%s: в котле должно быть не меньше четырёх сёл, есть %d", seed, len(ls))
		}
		if ls[0] != StarterLayout() {
			t.Fatalf("%s: первое село котла должно сохранить стартовую раскладку", seed)
		}
		plans := map[string]string{}
		for i, l := range ls {
			checkLayout(t, seed+"/"+names[i], l)
			if other, dup := plans[l.Plan]; dup && i < 5 {
				t.Fatalf("%s: у сёл %s и %s один план застройки %q", seed, other, names[i], l.Plan)
			}
			plans[l.Plan] = names[i]
			for j := 0; j < i; j++ {
				if reflect.DeepEqual(l.Buildings, ls[j].Buildings) || reflect.DeepEqual(l.POIs, ls[j].POIs) {
					t.Fatalf("%s: сёла %s и %s совпали застройкой", seed, names[j], names[i])
				}
			}
		}
		// повторный запрос отдаёт ту же раскладку, а не строит заново
		w.Each("settlement", func(se *core.Entity) {
			if LayoutOf(w, se.ID) != LayoutOf(w, se.ID) {
				t.Fatal("раскладка должна запоминаться")
			}
		})
	}
}

func TestUniformLayoutsSwitch(t *testing.T) {
	w, _ := GenerateStarter("uniform")
	UniformLayouts = true
	defer func() { UniformLayouts = false }()
	w.Each("settlement", func(se *core.Entity) {
		if LayoutOf(w, se.ID) != StarterLayout() {
			t.Fatal("с предохранителем все сёла получают стартовую раскладку")
		}
	})
}

// Жители не стоят кучей (comms 0152): ночью расходятся по своим избам, днём — по делам, и никто не
// стоит в одной точке с другим. Проверяется на стартовом селе и на сгенерированном.
func TestResidentsDoNotClump(t *testing.T) {
	w, _ := GenerateStarter("clump")
	var sets []core.ID
	w.Each("settlement", func(se *core.Entity) {
		if !isOuter(w, core.Settle(se)) && len(sets) < 2 {
			sets = append(sets, se.ID)
		}
	})
	for _, sidv := range sets {
		l := LayoutOf(w, sidv)
		var res []*core.Entity
		w.Each("place", func(e *core.Entity) {
			if core.Place(e).Settlement == sidv && core.Mind(e) != nil && core.Ident(e) != nil {
				res = append(res, e)
			}
		})
		for _, phase := range []struct{ goal, act string }{{"rest", "sleep"}, {"wander", ""}, {"work", ""}} {
			for _, e := range res {
				m := core.Mind(e)
				m.Goal, m.ActCode = phase.goal, phase.act
				if ScenePos(e) == nil {
					x, y := l.SpawnSpot(uint64(e.ID))
					w.Attach(e, &ScenePosC{X: x, Y: y, Stam: 1})
				}
			}
			for i := 0; i < 2400; i++ { // четыре минуты реального времени
				StepNPCMovement(w, 0.1, nil)
			}
			spots := map[[2]int]int{}
			for i, a := range res {
				pa := ScenePos(a)
				if l.Blocked(pa.X, pa.Y) {
					t.Fatalf("село %d, %s: житель стоит в стене (%.1f,%.1f)", sidv, phase.goal, pa.X, pa.Y)
				}
				spots[[2]int{int(math.Round(pa.X / 12)), int(math.Round(pa.Y / 12))}]++
				for _, b := range res[i+1:] {
					pb := ScenePos(b)
					if d := math.Hypot(pa.X-pb.X, pa.Y-pb.Y); d < 0.6 {
						t.Fatalf("село %d, %s: двое стоят в одной точке (%.2f м)", sidv, phase.goal, d)
					}
				}
			}
			if phase.goal != "work" && len(spots) < 3 {
				t.Fatalf("село %d, %s: все %d жителей собрались в %d местах", sidv, phase.goal, len(res), len(spots))
			}
			big := 0
			for _, n := range spots {
				big = max(big, n)
			}
			if phase.act == "sleep" && big > (len(res)+1)/2+1 {
				t.Fatalf("село %d: ночью в одном месте %d из %d жителей — должны разойтись по избам", sidv, big, len(res))
			}
		}
	}
}
