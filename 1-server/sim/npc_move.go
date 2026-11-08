package sim

import (
	"math"

	"pogranichye/core"
)

// Пространственное движение НПС по стартовой деревне (первая вертикаль A-Life присутствия).
//
// Разделение времени (ключевое требование 0032):
//   - ИГРОВОЕ время (часы мира) — нужды, расписания, экономика, ВЫБОР цели: это делает существующий
//     mind/activity цикл, его НЕ трогаем.
//   - РЕАЛЬНОЕ время — физический шаг ходьбы. StepNPCMovement вызывается из серверного tick-цикла с
//     РЕАЛЬНЫМ dt (длительность тика), а не с игровыми часами. Поэтому житель идёт ~1 м/с по стене
//     часов и ходьба визуально читаема, сколь бы быстро ни бежал игровой календарь.
//
// Цель (MindC.Goal) переводится в POI текущего занятия (targetForGoal). Житель идёт к своей точке;
// при смене цели он НЕ телепортируется — просто перенаправляется и идёт к новой точке с текущего
// места. Дошёл — стоит у «своего» места возле POI (idOffset), лицом к точке.
//
// Пока без графа препятствий: движение по прямой в пределах круга площадки. Навигация по waypoint'ам
// и коллизии зданий/ручья — следующий слой (см. comms к 0031/0029).

const (
	npcWalk    = 1.1 // м/с — спокойный шаг жителя (реальное время)
	npcArrive  = 0.35 // м — радиус «дошёл» (у своего места возле POI)
	npcMaxStep = 1.0  // м — предохранитель: за один тик не больше (анти-рывок при залипшем dt)
)

// StepNPCMovement продвигает всех НЕ управляемых игроком жителей поселения на один РЕАЛЬНЫЙ шаг dt
// (секунды). controlled(id)==true — тело под управлением сессии, его ведёт игрок, не трогаем.
func StepNPCMovement(w *core.World, dt float64, controlled func(core.ID) bool) {
	if w == nil || dt <= 0 {
		return
	}
	w.Each("mind", func(e *core.Entity) {
		if controlled != nil && controlled(e.ID) {
			return // игрок сам ведёт это тело
		}
		pl := core.Place(e)
		if pl == nil || pl.Settlement == 0 {
			return // не в поселении — сцены нет
		}
		p := ScenePos(e)
		if p == nil {
			// впервые видим — ставим на детерминированное стартовое место (деревня населена с кадра 1)
			sx, sy := SpawnSpot(uint64(e.ID))
			p = &ScenePosC{X: sx, Y: sy, Facing: 90, Stam: 1}
			w.Attach(e, p)
		}
		id := core.Ident(e)
		m := core.Mind(e)
		if id == nil || m == nil {
			return
		}
		// куда идёт: POI текущей цели + личное смещение (чтобы не слипались). Если смещение завело бы
		// точку в стену/реку — отбрасываем смещение и целимся в саму доступную точку POI.
		bx, by := targetForGoal(id.Role, m.Goal)
		ox, oy := idOffset(uint64(e.ID))
		tx, ty := bx+ox, by+oy
		if Blocked(tx, ty) {
			tx, ty = bx, by
		}

		// арбитраж: если дошли до POI — стоим; иначе идём к цели по ДОРОГАМ (через узел графа)
		if math.Hypot(tx-p.X, ty-p.Y) <= npcArrive {
			p.Facing = facingDeg(tx-p.X, ty-p.Y, p.Facing)
			return
		}
		wx, wy := nextWaypoint(w, e, p.X, p.Y, tx, ty) // следующая точка маршрута по дорогам (#39)
		dx, dy := wx-p.X, wy-p.Y
		dist := math.Hypot(dx, dy)
		if dist < 1e-6 {
			return
		}
		step := npcWalk * dt
		if step > npcMaxStep {
			step = npcMaxStep
		}
		if step > dist {
			step = dist // не перешагиваем цель
		}
		// идём к цели, но обходим стены/реку/край мира (скольжение). Житель не проходит сквозь здания.
		nx, ny := ResolveMove(p.X, p.Y, p.X+dx/dist*step, p.Y+dy/dist*step)
		// если упёрлись (скольжение не сдвинуло) — лёгкий детерминированный объезд вдоль преграды
		if math.Hypot(nx-p.X, ny-p.Y) < step*0.2 {
			perp := 1.0
			if (uint64(e.ID) & 1) == 0 {
				perp = -1.0
			}
			nx, ny = ResolveMove(p.X, p.Y, p.X-dy/dist*step*perp, p.Y+dx/dist*step*perp)
		}
		p.X, p.Y = nx, ny
		p.Facing = facingDeg(dx, dy, p.Facing)
	})
}

// facingDeg — взгляд в градусах [0,360), 0=+X, по направлению (dx,dy). Нулевой вектор — прежний взгляд.
func facingDeg(dx, dy, prev float64) float64 {
	if dx == 0 && dy == 0 {
		return prev
	}
	a := math.Atan2(dy, dx) * 180 / math.Pi
	if a < 0 {
		a += 360
	}
	return a
}
