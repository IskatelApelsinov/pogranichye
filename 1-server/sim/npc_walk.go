package sim

import (
	"math"

	"pogranichye/core"
)

// StepMigrants gives every same-side road migration a physical body, including routes that pass
// through intermediate settlements. The route's cumulative road distance is the canonical path;
// ProgressHours / TotalHours selects a point along that polyline. No teleport or arrival timing
// changes are introduced, and the entity remains visible from whichever settlement currently owns
// the nearest half of the active road segment.
func StepMigrants(w *core.World) {
	if w == nil || !Seamless(w) {
		return
	}
	type routePoint struct {
		id core.ID
		x  float64
		y  float64
	}
	w.Each("migration", func(e *core.Entity) {
		m := Migration(e)
		if m == nil || Player(e) != nil || len(m.Route) < 2 || m.TotalHours <= 0 {
			return
		}

		points := make([]routePoint, 0, len(m.Route))
		outer := false
		for i, id := range m.Route {
			settlement := settleOf(w, id)
			if settlement == nil {
				return
			}
			isOuterSettlement := isOuter(w, settlement)
			if i == 0 {
				outer = isOuterSettlement
			} else if isOuterSettlement != outer {
				return // do not render a physical route through the mountain gateway
			}
			x, y := Anchor(w, settlement)
			points = append(points, routePoint{id: id, x: x, y: y})
		}
		if points[0].id != m.From || points[len(points)-1].id != m.To {
			return
		}

		segmentLengths := make([]float64, len(points)-1)
		totalDistance := 0.0
		for i := 0; i < len(points)-1; i++ {
			segmentLengths[i] = math.Hypot(points[i+1].x-points[i].x, points[i+1].y-points[i].y)
			if segmentLengths[i] <= 0 || math.IsNaN(segmentLengths[i]) || math.IsInf(segmentLengths[i], 0) {
				return
			}
			totalDistance += segmentLengths[i]
		}
		if totalDistance <= 0 || math.IsNaN(totalDistance) || math.IsInf(totalDistance, 0) {
			return
		}

		frac := m.ProgressHours / m.TotalHours
		frac = math.Max(0, math.Min(1, frac))
		distanceAlong := frac * totalDistance
		segment := len(segmentLengths) - 1
		localFrac := 1.0
		traversed := 0.0
		for i, length := range segmentLengths {
			if distanceAlong <= traversed+length || i == len(segmentLengths)-1 {
				segment = i
				localFrac = math.Max(0, math.Min(1, (distanceAlong-traversed)/length))
				break
			}
			traversed += length
		}

		from, to := points[segment], points[segment+1]
		wx := from.x + (to.x-from.x)*localFrac
		wy := from.y + (to.y-from.y)*localFrac
		owner, ox, oy := from.id, from.x, from.y
		if math.Hypot(wx-to.x, wy-to.y) < math.Hypot(wx-from.x, wy-from.y) {
			owner, ox, oy = to.id, to.x, to.y
		}

		pl := core.Place(e)
		if pl == nil {
			pl = &core.PlaceC{}
			w.Attach(e, pl)
		}
		pl.Settlement = owner
		p := ScenePos(e)
		if p == nil {
			p = &ScenePosC{Stam: 1}
			w.Attach(e, p)
		}
		p.X, p.Y, p.Facing = wx-ox, wy-oy, facingDeg(to.x-from.x, to.y-from.y, p.Facing)
	})
}
