package sim

import (
	"math"

	"pogranichye/core"
)

// Внутренний навигационный граф деревни (#39): НПС ходят по дорогам между POI, а не ломятся напрямую
// сквозь углы зданий. Узлы и рёбра — по предложению ChatGPT (0050), привязаны к текущей геометрии v2
// (300×300). Граф серверный и внутренний: wire-контракт не меняется, клиент рисует дороги сам.
//
// Через реку проходит ТОЛЬКО цепочка по мосту. Маршрут ищется A*/Dijkstra (граф мал), шаги — через
// существующий ResolveMove. При застревании/смене цели маршрут перепрокладывается.

type navNode struct {
	name string
	x, y float64
}

var navNodes = []navNode{
	{"well", 0, -4}, {"market", 1, 4}, {"west-junction", -16, 4}, {"tavern-gate", -33, 12},
	{"west-road", -60, 8}, {"woods", -95, 10}, {"northwest-lane", -14, 24}, {"granary-gate", -2, 28},
	{"east-junction", 44, 4}, {"smithy-gate", 29, 14}, {"hearth-south-gate", 44, -34}, {"garden", 70, -20},
	{"mine", 90, 40}, {"river-approach", 0, -60}, {"fishing-bank", 0, -74}, {"bridge-north", 0, -77},
	{"bridge-center", 0, -84}, {"bridge-south", 0, -91}, {"south-road", 0, -110},
}

var navEdgeNames = [][2]string{
	{"well", "market"}, {"market", "west-junction"}, {"west-junction", "tavern-gate"},
	{"west-junction", "west-road"}, {"west-road", "woods"},
	{"west-junction", "northwest-lane"}, {"northwest-lane", "granary-gate"},
	{"market", "east-junction"}, {"east-junction", "smithy-gate"},
	{"east-junction", "hearth-south-gate"}, {"hearth-south-gate", "garden"}, {"garden", "mine"},
	{"well", "river-approach"}, {"river-approach", "fishing-bank"}, {"fishing-bank", "bridge-north"},
	{"bridge-north", "bridge-center"}, {"bridge-center", "bridge-south"}, {"bridge-south", "south-road"},
}

var navIdx = map[string]int{}
var navAdj [][]int

func init() {
	for i, n := range navNodes {
		navIdx[n.name] = i
	}
	navAdj = make([][]int, len(navNodes))
	for _, e := range navEdgeNames {
		a, b := navIdx[e[0]], navIdx[e[1]]
		navAdj[a] = append(navAdj[a], b)
		navAdj[b] = append(navAdj[b], a)
	}
}

func navDist(a, b int) float64 {
	return math.Hypot(navNodes[a].x-navNodes[b].x, navNodes[a].y-navNodes[b].y)
}

// nearestNode — ближайший узел графа к точке.
func nearestNode(x, y float64) int {
	best, bd := 0, math.MaxFloat64
	for i, n := range navNodes {
		d := math.Hypot(n.x-x, n.y-y)
		if d < bd {
			bd, best = d, i
		}
	}
	return best
}

// navRoute — Dijkstra от start к goal; возвращает цепочку индексов узлов (включая goal) или nil.
func navRoute(start, goal int) []int {
	if start == goal {
		return []int{goal}
	}
	const inf = math.MaxFloat64
	dist := make([]float64, len(navNodes))
	prev := make([]int, len(navNodes))
	done := make([]bool, len(navNodes))
	for i := range dist {
		dist[i], prev[i] = inf, -1
	}
	dist[start] = 0
	for {
		u, ud := -1, inf
		for i := range navNodes {
			if !done[i] && dist[i] < ud {
				ud, u = dist[i], i
			}
		}
		if u == -1 {
			break
		}
		if u == goal {
			break
		}
		done[u] = true
		for _, v := range navAdj[u] {
			if nd := dist[u] + navDist(u, v); nd < dist[v] {
				dist[v], prev[v] = nd, u
			}
		}
	}
	if prev[goal] == -1 && start != goal {
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

func init() { core.Register("nav", func() core.Component { return &NavC{} }) }

// NavC — текущий маршрут НПС по графу: цель (куда шли) и следующий узел.
type NavC struct {
	TX, TY float64 `json:"-"` // для какой цели построен маршрут
	Route  []int   `json:"-"` // оставшиеся узлы
}

func (c *NavC) Kind() string { return "nav" }

func navOf(e *core.Entity) *NavC { c, _ := e.Get("nav").(*NavC); return c }

// nextWaypoint возвращает промежуточную точку, к которой НПС должен идти, чтобы по дорогам дойти до
// (tx,ty). Если цель близко/в прямой видимости узла — ведёт прямо к цели. Кэширует маршрут в NavC.
func nextWaypoint(w *core.World, e *core.Entity, px, py, tx, ty float64) (float64, float64) {
	// цель рядом — идём прямо
	if math.Hypot(tx-px, ty-py) < 10 {
		return tx, ty
	}
	nav := navOf(e)
	if nav == nil {
		nav = &NavC{}
		w.Attach(e, nav)
	}
	// перепроложить, если цель сменилась или маршрут пуст
	if math.Hypot(nav.TX-tx, nav.TY-ty) > 3 || len(nav.Route) == 0 {
		s := nearestNode(px, py)
		g := nearestNode(tx, ty)
		nav.Route = navRoute(s, g)
		nav.TX, nav.TY = tx, ty
	}
	// снять достигнутые узлы
	for len(nav.Route) > 0 {
		n := navNodes[nav.Route[0]]
		if math.Hypot(n.x-px, n.y-py) < 6 {
			nav.Route = nav.Route[1:]
			continue
		}
		break
	}
	if len(nav.Route) == 0 {
		return tx, ty // дошли до последнего узла — финальный рывок к цели
	}
	n := navNodes[nav.Route[0]]
	return n.x, n.y
}
