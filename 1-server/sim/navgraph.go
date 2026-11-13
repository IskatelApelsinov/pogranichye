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

func init() {
	for i, n := range navNodes {
		navIdx[n.name] = i
	}
}

func init() { core.Register("nav", func() core.Component { return &NavC{} }) }

// NavC — текущий маршрут НПС по графу: цель (куда шли) и следующий узел.
type NavC struct {
	TX, TY float64 `json:"-"` // для какой цели построен маршрут
	Route  []int   `json:"-"` // оставшиеся узлы
	L      *Layout `json:"-"` // в графе какой раскладки проложен (сменил село — перепрокладываем)
}

func (c *NavC) Kind() string { return "nav" }

func navOf(e *core.Entity) *NavC { c, _ := e.Get("nav").(*NavC); return c }

// nextWaypoint возвращает промежуточную точку, к которой НПС должен идти, чтобы по дорогам дойти до
// (tx,ty). Цель рядом и видна по прямой — ведёт прямо к ней. Маршрут кэшируется в NavC.
// Каждый житель держится своей стороны дороги (личное смещение до метра с небольшим), поэтому идущие
// одним путём не сливаются в одну точку.
func nextWaypoint(w *core.World, l *Layout, e *core.Entity, px, py, tx, ty float64) (float64, float64) {
	if math.Hypot(tx-px, ty-py) < 10 && l.segmentClear(px, py, tx, ty) {
		return tx, ty
	}
	nav := navOf(e)
	if nav == nil {
		nav = &NavC{}
		w.Attach(e, nav)
	}
	// перепроложить, если сменились село или цель, либо маршрут сброшен (застрял)
	if nav.L != l || math.Hypot(nav.TX-tx, nav.TY-ty) > 3 || nav.Route == nil {
		nav.Route = l.route(l.nearestNode(px, py), l.nearestNode(tx, ty))
		if nav.Route == nil {
			nav.Route = []int{}
		}
		nav.TX, nav.TY, nav.L = tx, ty, l
	}
	h := uint64(e.ID)*2654435761 + 97
	ox, oy := float64(h%240)/100-1.2, float64((h/240)%240)/100-1.2
	for len(nav.Route) > 0 {
		n := l.nodes[nav.Route[0]]
		wx, wy := n.x+ox, n.y+oy
		if l.Blocked(wx, wy) {
			wx, wy = n.x, n.y
		}
		if math.Hypot(wx-px, wy-py) < 2.5 {
			nav.Route = nav.Route[1:] // узел пройден
			continue
		}
		return wx, wy
	}
	return tx, ty // дошли до последнего узла — финальный отрезок к цели
}
