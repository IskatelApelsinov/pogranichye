package sim

import (
	"math"
	"sync"

	"pogranichye/core"
)

// Тропки и тракты между поселениями (v1.27). Сеть перегонов (RoadGraph) была только в счёте пути:
// на земле между сёлами лежала прямая черта. Теперь у каждого перегона есть настоящая нитка в мире —
// она петляет, как протоптанная людьми тропа, начинается и кончается в центрах сёл и одинакова для
// всех (чистая функция зерна мира и пары сёл). Между деревнями — тропка, к городку и городу — тракт.
// По нитке идти легче: вне площадки села шаг по тропке быстрее, по тракту — ещё быстрее.

const (
	trailStep   = 60.0  // м между точками нитки
	trailBend   = 0.09  // наибольший увод нитки в сторону — доля длины перегона
	trailBendM  = 220.0 // …но не больше стольких метров
	TrailWidth  = 1.6   // ширина тропки, м
	RoadWidth   = 4.0   // ширина тракта, м
	TrailBoost  = 1.15  // множитель шага на тропке
	RoadBoost   = 1.25  // множитель шага на тракте
	trailReachM = 1.5   // допуск: столько метров от края нитки ещё считается «на ней»

	// v1.28 (comms 0278/0279): первый шаг прокладки по уклону. Нитка — не геодезист, но на каждом
	// звене пробует отступить вбок, если прямой наезд на подъём хуже найденного рядом прохода.
	// Это ПОДСКАЗКА рельефу, а не настоящий поиск пути в обход горы: решает только локально, в
	// пределах доли собственного меандра звена, и может не найти проход там, где его нет совсем.
	// Порог калиброван по реальному рельефу между сёлами (замер: прямые перегоны почти везде ≤0.09
	// на 2 м и ещё мягче при усреднении по звену в 60 м) — настоящих гор/крутых склонов на прямой
	// между соседними сёлами сейчас не бывает (их выращивают иначе — рубеж «котла», хребты у края
	// карты), так что порог взят НИЗКИМ, чтобы звено хоть немного реагировало на обычную мягкую
	// холмистость, а не оставался мёртвым кодом до отдельного слоя настоящих гор.
	slopeAvoidGrade = 0.05 // уклон (подъём/м), с которого звено начинает искать более пологий проход
	slopeAvoidFrac  = 0.3  // доля амплитуды меандра звена, на которую можно отступить ради уклона
	slopeAvoidCapM  = 20.0 // …но не больше стольких метров
	slopeAvoidStepM = 4.0  // шаг перебора вбок при поиске
)

// Trail — нитка перегона между двумя сёлами в мировых метрах.
type Trail struct {
	ID    string
	A, B  core.ID
	Kind  string // trail — тропка, road — тракт
	Width float64
	Pts   [][2]float64
	LenM  float64
}

func bigTier(tier string) bool { return tier == "town" || tier == "city" }

func trailPoints(t *Terrain, a, b core.ID, ax, ay, bx, by float64) ([][2]float64, float64) {
	L := math.Hypot(bx-ax, by-ay)
	if L < 1 {
		return [][2]float64{{ax, ay}, {bx, by}}, L
	}
	n := int(math.Max(2, math.Ceil(L/trailStep)))
	px, py := -(by-ay)/L, (bx-ax)/L // вбок от прямой
	amp := math.Min(L*trailBend, trailBendM)
	salt := float64(a%9973)*811.0 + float64(b%9973)*37.0
	pts := make([][2]float64, 0, n+1)
	length := 0.0
	prevX, prevY := ax, ay
	for i := 0; i <= n; i++ {
		f := float64(i) / float64(n)
		env := math.Sin(math.Pi * f) // у сёл нитка сходится в центр
		d := f * L
		off := ((noise(t.seed+97, salt+d, salt*0.5, 700)-0.5)*2*amp + (noise(t.seed+131, salt+d, salt*0.5+50, 170)-0.5)*0.5*amp) * env
		x, y := ax+(bx-ax)*f+px*off, ay+(by-ay)*f+py*off
		if i > 0 && i < n {
			x, y = avoidSteep(t, prevX, prevY, x, y, px, py, amp)
		}
		if i > 0 {
			length += math.Hypot(x-prevX, y-prevY)
		}
		pts = append(pts, [2]float64{x, y})
		prevX, prevY = x, y
	}
	return pts, length
}

// segGrade — средний уклон (подъём на метр) напрямик от (fx,fy) до (x,y) по уже построенному рельефу.
func segGrade(t *Terrain, fx, fy, x, y float64) float64 {
	d := math.Hypot(x-fx, y-fy)
	if d < 1e-6 {
		return 0
	}
	return math.Abs(t.Height(x, y)-t.Height(fx, fy)) / d
}

// avoidSteep — если прямой шаг от (fx,fy) к (tx,ty) идёт по круче slopeAvoidGrade, звено пробует
// отступить вбок вдоль (px,py) в пределах доли своего меандра (amp) и взять самый пологий проход
// из найденных; не хуже исходной точки — если ничего ровнее нет, остаётся на месте.
func avoidSteep(t *Terrain, fx, fy, tx, ty, px, py, amp float64) (float64, float64) {
	bestX, bestY, bestG := tx, ty, segGrade(t, fx, fy, tx, ty)
	if bestG <= slopeAvoidGrade {
		return tx, ty
	}
	reach := math.Min(slopeAvoidCapM, amp*slopeAvoidFrac)
	for s := -reach; s <= reach+1e-9; s += slopeAvoidStepM {
		if s == 0 {
			continue
		}
		cx, cy := tx+px*s, ty+py*s
		if g := segGrade(t, fx, fy, cx, cy); g < bestG {
			bestX, bestY, bestG = cx, cy, g
		}
	}
	return bestX, bestY
}

var trailCache struct {
	mu   sync.Mutex
	w    *core.World
	n    int
	list []Trail
	grid map[[2]int64][]int // чанк → нитки, которые через него идут
}

// Trails — все нитки мира и их раскладка по чанкам. Считается один раз на состав сёл.
func Trails(w *core.World) ([]Trail, map[[2]int64][]int) {
	t := terrainFor(w)
	trailCache.mu.Lock()
	defer trailCache.mu.Unlock()
	if n := w.CountKind("settlement"); trailCache.w != w || trailCache.n != n || trailCache.grid == nil {
		list := []Trail{}
		grid := map[[2]int64][]int{}
		for _, outer := range [2]bool{false, true} {
			for _, rd := range RoadGraph(w, outer) {
				sa, sb := settleOf(w, rd.A), settleOf(w, rd.B)
				if sa == nil || sb == nil {
					continue
				}
				ax, ay := Anchor(w, sa)
				bx, by := Anchor(w, sb)
				tr := Trail{ID: "road-" + eidStr(rd.A) + "-" + eidStr(rd.B), A: rd.A, B: rd.B, Kind: "trail", Width: TrailWidth}
				if bigTier(sa.Tier) || bigTier(sb.Tier) {
					tr.Kind, tr.Width = "road", RoadWidth
				}
				tr.Pts, tr.LenM = trailPoints(t, rd.A, rd.B, ax, ay, bx, by)
				idx := len(list)
				list = append(list, tr)
				mark := func(x, y float64) {
					k := [2]int64{int64(math.Floor(x / ChunkMeters)), int64(math.Floor(y / ChunkMeters))}
					if c := grid[k]; len(c) == 0 || c[len(c)-1] != idx {
						grid[k] = append(c, idx)
					}
				}
				for i, p := range tr.Pts {
					mark(p[0], p[1])
					if i > 0 {
						mark((p[0]+tr.Pts[i-1][0])/2, (p[1]+tr.Pts[i-1][1])/2)
					}
				}
			}
		}
		trailCache.w, trailCache.n, trailCache.list, trailCache.grid = w, n, list, grid
	}
	return trailCache.list, trailCache.grid
}

func segDist(px, py, ax, ay, bx, by float64) float64 {
	dx, dy := bx-ax, by-ay
	l2 := dx*dx + dy*dy
	if l2 == 0 {
		return math.Hypot(px-ax, py-ay)
	}
	f := clampf(((px-ax)*dx+(py-ay)*dy)/l2, 0, 1)
	return math.Hypot(px-ax-dx*f, py-ay-dy*f)
}

// TrailAt — нитка, на которой стоит мировая точка (nil — чистое поле). Тракт важнее тропки.
func TrailAt(w *core.World, x, y float64) *Trail {
	list, grid := Trails(w)
	var best *Trail
	for _, idx := range grid[[2]int64{int64(math.Floor(x / ChunkMeters)), int64(math.Floor(y / ChunkMeters))}] {
		tr := &list[idx]
		if best != nil && (best.Kind == "road" || tr.Kind != "road") {
			continue
		}
		for i := 1; i < len(tr.Pts); i++ {
			a, b := tr.Pts[i-1], tr.Pts[i]
			if math.Abs(a[0]-x) > trailStep*2 && math.Abs(b[0]-x) > trailStep*2 {
				continue
			}
			if segDist(x, y, a[0], a[1], b[0], b[1]) <= tr.Width/2+trailReachM {
				best = tr
				break
			}
		}
	}
	return best
}

// trailBoost — во сколько раз легче шаг в этой мировой точке (1 — поле).
func trailBoost(w *core.World, x, y float64) float64 {
	switch tr := TrailAt(w, x, y); {
	case tr == nil:
		return 1
	case tr.Kind == "road":
		return RoadBoost
	default:
		return TrailBoost
	}
}

// TrailView — на какой нитке стоит тело (me.trail): вид и между какими сёлами она идёт. nil — не на
// нитке или на площадке села (там свои улицы).
func TrailView(w *core.World, e *core.Entity) any {
	p := ScenePos(e)
	x, y, ok := bodyWorldXY(w, e)
	if p == nil || !ok || OnRoad(e) || math.Hypot(p.X, p.Y) < siteFlatR-slopeProbe {
		return nil
	}
	tr := TrailAt(w, x, y)
	if tr == nil {
		return nil
	}
	boost := TrailBoost
	if tr.Kind == "road" {
		boost = RoadBoost
	}
	return map[string]any{"id": tr.ID, "kind": tr.Kind, "a": settleName(w, tr.A), "b": settleName(w, tr.B), "boost": boost}
}

// chunkTrailRuns — куски нитки внутри чанка: подрезаны его границей и сшиты в ломаные.
func chunkTrailRuns(tr *Trail, oe, on float64) [][]map[string]float64 {
	var runs [][]map[string]float64
	var cur []map[string]float64
	var lx, ly float64
	flush := func() {
		if len(cur) >= 2 {
			runs = append(runs, cur)
		}
		cur = nil
	}
	for i := 1; i < len(tr.Pts); i++ {
		a, b := tr.Pts[i-1], tr.Pts[i]
		x0, y0, x1, y1, ok := clipSegmentToBox(a[0], a[1], b[0], b[1], oe, on, ChunkMeters)
		if !ok {
			flush()
			continue
		}
		if cur == nil || math.Hypot(x0-lx, y0-ly) > 0.01 {
			flush()
			cur = []map[string]float64{{"x": r3(x0 - oe), "z": r3(y0 - on)}}
		}
		cur = append(cur, map[string]float64{"x": r3(x1 - oe), "z": r3(y1 - on)})
		lx, ly = x1, y1
	}
	flush()
	return runs
}
