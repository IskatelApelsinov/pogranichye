package sim

import (
	"math"
	"sync"

	"pogranichye/core"
)

// Склон в движении (v1.23, comms 0254). Горы — не стена: по склону идут, только медленнее, и лишь
// кручу не одолеть. Сервер считает шаг по ТОЙ ЖЕ функции высоты, что отдаёт клиенту (/api/terrain),
// поэтому клиенту достаточно повторить формулу ниже — параметры опубликованы в /api/world.movement.
//
//	уклон g = подъём на метр пути в сторону шага (по высоте земли на 2 м вперёд)
//	в гору:   множитель = 1 / (1 + SlopeUpK·g)
//	под гору: пологий спуск (до SlopeEasyDown) чуть быстрее, крутой — осторожнее:
//	          множитель = 1 / (1 + SlopeDownK·(|g| − SlopeEasyDown))
//	круча:    g > SlopeCliff — в эту сторону шага нет (вдоль склона и вниз — можно)
//
// Под сёлами земля ровная (площадка радиусом siteFlatR), так что в селе склон не действует вовсе
// и рельеф не считается — обычная игра не платит за горы.

const (
	SlopeUpK       = 3.5
	SlopeDownK     = 2.0
	SlopeEasyDown  = 0.25
	SlopeEasyBoost = 1.08
	SlopeCliff     = 1.1 // подъём круче ~48° — скала
	slopeProbe     = 2.0 // м: на таком шаге меряем уклон
)

// SlopeMult — множитель скорости для уклона g (0 — круча, шага нет).
func SlopeMult(g float64) float64 {
	switch {
	case g > SlopeCliff:
		return 0
	case g > 0:
		return 1 / (1 + SlopeUpK*g)
	case g > -SlopeEasyDown:
		return 1 + (SlopeEasyBoost-1)*(-g/SlopeEasyDown)
	default:
		return SlopeEasyBoost / (1 + SlopeDownK*(-g-SlopeEasyDown))
	}
}

// Grade — уклон земли в точке мира в сторону (de, dn): подъём на метр пути.
func (t *Terrain) Grade(e, n, de, dn float64) float64 {
	d := math.Hypot(de, dn)
	if d < 1e-9 {
		return 0
	}
	return (t.Height(e+de/d*slopeProbe, n+dn/d*slopeProbe) - t.Height(e, n)) / slopeProbe
}

// TerrainMult — множитель скорости тела при шаге в сторону (mx, my) с учётом склона под ногами.
// 1 — ровно (и всегда 1 в пределах площадки села); 0 — круча, в эту сторону не пройти.
func TerrainMult(w *core.World, t *Terrain, e *core.Entity, mx, my float64) float64 {
	p, pl := ScenePos(e), core.Place(e)
	if p == nil || pl == nil || t == nil || (mx == 0 && my == 0) {
		return 1
	}
	if math.Hypot(p.X, p.Y) < siteFlatR-slopeProbe {
		return 1 // площадка села ровная
	}
	s := settleOf(w, pl.Settlement)
	if s == nil {
		return 1
	}
	ax, ay := Anchor(w, s)
	return SlopeMult(t.Grade(ax+p.X, ay+p.Y, mx, my))
}

var terrCache struct {
	mu sync.Mutex
	w  *core.World
	n  int
	t  *Terrain
}

// terrainFor — рельеф мира с кэшем на число поселений (якоря меняются, только когда их число растёт).
func terrainFor(w *core.World) *Terrain {
	terrCache.mu.Lock()
	defer terrCache.mu.Unlock()
	if n := w.CountKind("settlement"); terrCache.t == nil || terrCache.w != w || terrCache.n != n {
		terrCache.w, terrCache.n, terrCache.t = w, n, NewTerrain(w)
	}
	return terrCache.t
}

// MovementView — правила движения по рельефу для клиента (/api/world.movement).
func MovementView() map[string]any {
	return map[string]any{"walk_mps": walkSpeed, "jog_mps": jogSpeed, "run_mps": runSpeed,
		"slope": map[string]any{"probe_m": slopeProbe, "up_k": SlopeUpK, "down_k": SlopeDownK,
			"easy_down": SlopeEasyDown, "easy_boost": SlopeEasyBoost, "cliff": SlopeCliff, "flat_radius_m": siteFlatR}}
}
