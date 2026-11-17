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
	npcWalk    = 1.1  // м/с — спокойный шаг жителя (реальное время)
	npcArrive  = 0.35 // м — радиус «дошёл» (у своего места возле POI)
	npcMaxStep = 1.0  // м — предохранитель: за один тик не больше (анти-рывок при залипшем dt)

	stuckMild = 1.2 // c — лёгкое застревание: усилить объезд и сменить сторону
	stuckHard = 3.5 // c — глухое застревание: перепроложить маршрут к ближайшему узлу дороги
)

// StepNPCMovement продвигает всех НЕ управляемых игроком жителей поселения на один РЕАЛЬНЫЙ шаг dt
// (секунды). controlled(id)==true — тело под управлением сессии, его ведёт игрок, не трогаем.
//
// Пре-проход распределяет жителей по НЕПЕРЕСЕКАЮЩИМСЯ парковочным слотам вокруг каждого POI: ранг в
// очереди к точке (по возрастанию ID — детерминирован независимо от порядка обхода) → отдельный слот
// (parkingSlot). Это убирает «слипание» НПС в одной точке у POI (P0-навигация 0050/0053).
func StepNPCMovement(w *core.World, dt float64, controlled func(core.ID) bool) {
	if w == nil || dt <= 0 {
		return
	}
	type mover struct {
		e      *core.Entity
		l      *Layout
		bx, by float64 // центр POI текущей цели
		rank   int
	}
	var movers []mover
	// Each iterates IDs in ascending order, so this counter assigns the same
	// deterministic per-POI rank as the former collect→sort→rank multi-pass.
	type spot struct {
		s    core.ID
		x, y float64
	}
	nextRank := make(map[spot]int)
	layouts := map[core.ID]*Layout{}
	collect := func(e *core.Entity) {
		if controlled != nil && controlled(e.ID) {
			return // игрок сам ведёт это тело
		}
		pl := core.Place(e)
		if pl == nil || pl.Settlement == 0 {
			return // не в поселении — сцены нет
		}
		if !isHot(w, pl.Settlement) || OnRoad(e) {
			return // остывшее поселение: тел в сцене нет (heat.go); путник — на тракте, не в сцене
		}
		id := core.Ident(e)
		m := core.Mind(e)
		if id == nil || m == nil {
			return
		}
		l := layouts[pl.Settlement]
		if l == nil {
			l = LayoutOf(w, pl.Settlement)
			layouts[pl.Settlement] = l
		}
		bx, by := targetFor(l, e, id.Role, m.Goal, m.ActCode)
		key := spot{pl.Settlement, bx, by}
		rank := nextRank[key]
		nextRank[key] = rank + 1
		movers = append(movers, mover{e: e, l: l, bx: bx, by: by, rank: rank})
	}
	// In a server world HeatC exists for every settlement. Only residents with a
	// materialized scene body can move, so iterate that compact index rather than
	// scanning every MindC in the planet. Worlds without heat (small unit fixtures
	// and tools) retain the legacy behavior and lazily create their first body.
	if w.HasKind("heat") {
		w.Each("scenepos", collect)
	} else {
		w.Each("mind", collect)
	}
	for _, mv := range movers {
		stepMover(w, mv.l, dt, mv.e, mv.bx, mv.by, mv.rank)
	}
}

// stepMover продвигает одного жителя к его парковочному слоту у POI (bx,by) за реальный шаг dt.
func stepMover(w *core.World, l *Layout, dt float64, e *core.Entity, bx, by float64, rank int) {
	p := ScenePos(e)
	if p == nil {
		// впервые видим — ставим на детерминированное стартовое место (деревня населена с кадра 1)
		sx, sy := l.SpawnSpot(uint64(e.ID))
		p = &ScenePosC{X: sx, Y: sy, Facing: 90, Stam: 1}
		w.Attach(e, p)
	}
	// цель: POI + личный слот по рангу (жители занимают разные места). Если слот в стене/реке —
	// отбрасываем смещение и целимся в саму доступную точку POI.
	ox, oy := parkingSlot(rank)
	tx, ty := bx+ox, by+oy
	if l.Blocked(tx, ty) || !l.segmentClear(bx, by, tx, ty) {
		tx, ty = bx, by // место за стеной от точки — встаём у самой точки
	}
	// арбитраж: дошли до своего слота — стоим; иначе идём по ДОРОГАМ (через узел графа)
	if math.Hypot(tx-p.X, ty-p.Y) <= npcArrive {
		p.Facing = facingDeg(tx-p.X, ty-p.Y, p.Facing)
		p.Stuck = 0
		return
	}
	wx, wy := nextWaypoint(w, l, e, p.X, p.Y, tx, ty) // следующая точка маршрута по дорогам (#39)
	dx, dy := wx-p.X, wy-p.Y
	dist := math.Hypot(dx, dy)
	if dist < 1e-6 {
		return
	}
	step := npcWalk * (0.8 + float64(uint64(e.ID)*40503%21)/100) * dt // у каждого свой шаг: 0.8–1.0 от полного
	if step > npcMaxStep {
		step = npcMaxStep
	}
	if step > dist {
		step = dist // не перешагиваем цель
	}
	// идём к цели, но обходим стены/реку/край мира (скольжение). Житель не проходит сквозь здания.
	nx, ny := l.ResolveMove(p.X, p.Y, p.X+dx/dist*step, p.Y+dy/dist*step)
	if math.Hypot(nx-p.X, ny-p.Y) < step*0.2 {
		// упёрлись. Measured recovery: сила/сторона объезда растут с ВРЕМЕНЕМ застревания (p.Stuck),
		// а не действуют одинаково на один тик.
		perp := 1.0
		if (uint64(e.ID) & 1) == 0 {
			perp = -1.0
		}
		mag := step
		if p.Stuck > stuckMild { // дольше стоим — толкаемся сильнее и в другую сторону
			perp = -perp
			mag = step * 1.8
		}
		ax, ay := l.ResolveMove(p.X, p.Y, p.X-dy/dist*mag*perp, p.Y+dx/dist*mag*perp)
		if math.Hypot(ax-p.X, ay-p.Y) < step*0.2 { // и эта сторона глухая — пробуем противоположную
			ax, ay = l.ResolveMove(p.X, p.Y, p.X+dy/dist*mag*perp, p.Y-dx/dist*mag*perp)
		}
		nx, ny = ax, ay
		if p.Stuck > stuckHard { // долго заперты — сбросить маршрут, перепроложим от ближайшего узла
			if nav := navOf(e); nav != nil {
				nav.Route = nil
			}
		}
	}
	if math.Hypot(nx-p.X, ny-p.Y) < step*0.25 {
		p.Stuck += dt // measured: копим РЕАЛЬНОЕ время застревания, а не считаем «застрял за тик»
	} else {
		p.Stuck = 0
	}
	p.X, p.Y = nx, ny
	p.Facing = facingDeg(dx, dy, p.Facing)
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
