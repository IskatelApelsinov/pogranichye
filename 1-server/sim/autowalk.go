package sim

import (
	"math"

	"pogranichye/core"
)

// Самоход к месту села (v1.22) — для клиента-менеджера (тонкого): игрок выбирает место, а тело
// ИДЁТ к нему само, по тем же дорогам раскладки, что и жители. Раньше op:"goto" мгновенно ставил
// тело в точку: на экране персонаж не ходил, а «прибытие» было лишь сменой вкладки в клиенте.
//
// Правила (task_ai 006, часть A):
//   - положение, путь, преграды и прибытие принадлежат серверу; клиент только называет цель;
//   - новая цель заменяет прежнюю, пустая — отменяет; ручной шаг (op:"move") тоже отменяет;
//   - дела у места по-прежнему проверяются обычной серверной дистанцией до POI;
//   - самоход хранится в мире: обрыв связи не теряет цель, после возвращения тело идёт дальше.

func init() { core.Register("autowalk", func() core.Component { return &AutoWalkC{} }) }

// AutoWalkC — «иду к месту»: ключ точки интереса и её координаты в сцене села.
type AutoWalkC struct {
	Reach float64 `json:"reach,omitempty"` // v1.30: радиус «пришёл» (находке — вплотную); 0 — как у урочища
	Loc   string  `json:"loc"`
	TX    float64 `json:"tx"`
	TY    float64 `json:"ty"`
	// Site — идём не к месту села, а к месту без жителей в поле (wildlands.go): его id и точка в мире.
	Site   string      `json:"site,omitempty"`
	Name   string      `json:"name,omitempty"`
	WE     float64     `json:"we,omitempty"`
	WN     float64     `json:"wn,omitempty"`
	exit   *[2]float64 // точка выхода из ядра села (выбирается один раз, чтобы путь не метался)
	exited bool        // из ядра села уже вышли
	Stuck  float64     `json:"-"` // сколько секунд без продвижения (сквозь стену не идём — обходим)
	Best   float64     `json:"-"` // наименьшее достигнутое расстояние до цели
}

func (c *AutoWalkC) Kind() string { return "autowalk" }

func autoWalkOf(e *core.Entity) *AutoWalkC {
	if e == nil {
		return nil
	}
	c, _ := e.Get("autowalk").(*AutoWalkC)
	return c
}

const (
	autoWalkSpeed  = 6.0  // м/с: бодрый шаг клиента-менеджера (самый дальний путь в селе — около 25 с)
	autoWalkArrive = 1.2  // м: ближе — пришли
	fieldWalkSpeed = 14.0 // м/с в поле: клиент-менеджер не ведёт тело шаг за шагом, поход к урочищу — минута-другая
	poiHere        = 10.0
)

// WalkTo — начать идти к месту села. Пустое место — остановиться.
func WalkTo(w *core.World, e *core.Entity, location string) (bool, string) {
	if location == "" {
		CancelWalk(w, e)
		return true, ""
	}
	if OnRoad(e) {
		return false, "ты в пути — сперва дойди"
	}
	p := EnsureScenePos(w, e)
	if p.Indoor != "" {
		return false, "ты в комнате — сперва выйди"
	}
	l := layoutFor(w, e)
	tx, ty, ok := walkTarget(l, location)
	if !ok {
		return false, "нет такого места"
	}
	if c := autoWalkOf(e); c != nil {
		c.Loc, c.TX, c.TY, c.Stuck, c.Best = location, tx, ty, 0, math.MaxFloat64
	} else {
		w.Attach(e, &AutoWalkC{Loc: location, TX: tx, TY: ty, Best: math.MaxFloat64})
	}
	if nav := navOf(e); nav != nil {
		nav.Route = nil
	}
	return true, ""
}

// WalkToSite — идти к месту без жителей (урочищу, пещере, башне) через поле. Земли сёл по пути
// меняются сами (StepLands); тело идёт по прямой, обходя постройки села и реку через мост.
func WalkToSite(w *core.World, e *core.Entity, siteID string) (bool, string) {
	if OnRoad(e) {
		return false, "ты в пути — сперва дойди"
	}
	if !Seamless(w) {
		return false, "за околицу отсюда не выйти"
	}
	p := EnsureScenePos(w, e)
	if p.Indoor != "" {
		return false, "ты в комнате — сперва выйди"
	}
	site, ok := WildPlaceByID(w, NewTerrain(w), siteID)
	reach := 0.0
	if !ok { // v1.30: находка под ногами — к ней подходят вплотную
		if g, gok := GatherNodeByID(w, terrainFor(w), siteID); gok {
			site, ok, reach = WildPlace{ID: g.ID, Name: g.Kind.Name, E: g.E, N: g.N}, true, gatherReach*0.6
		}
	}
	if !ok {
		return false, "нет такого места"
	}
	x, y, has := bodyWorldXY(w, e)
	if !has {
		return false, "ты в пути"
	}
	if math.Hypot(site.E-x, site.N-y) > wildNearDist*1.5 {
		return false, "слишком далеко — сначала подойди ближе"
	}
	CancelWalk(w, e)
	w.Attach(e, &AutoWalkC{Loc: "", Site: site.ID, Name: site.Name, WE: site.E, WN: site.N, Best: math.MaxFloat64, Reach: reach})
	if nav := navOf(e); nav != nil {
		nav.Route = nil
	}
	p.At = ""
	return true, ""
}

// stepFieldWalk — шаг похода к месту в поле. Возвращает true, когда пришли.
func stepFieldWalk(w *core.World, e *core.Entity, c *AutoWalkC, p *ScenePosC, dt float64) bool {
	s := settleOf(w, core.Place(e).Settlement)
	if s == nil {
		return true
	}
	ax, ay := Anchor(w, s)
	tx, ty := c.WE-ax, c.WN-ay // цель в координатах текущих земель
	d := math.Hypot(tx-p.X, ty-p.Y)
	arrive := wildReach * 0.4
	if c.Reach > 0 {
		arrive = c.Reach
	}
	if d <= arrive {
		return true
	}
	l := layoutFor(w, e)
	gx, gy, speed := tx, ty, fieldWalkSpeed
	inScene := math.Abs(p.X) <= l.Half && math.Abs(p.Y) <= l.Half
	if inScene {
		speed = autoWalkSpeed
		bx := (l.BridgeX0 + l.BridgeX1) / 2
		near, far := l.RiverY1+1.5, l.RiverY0-1.5
		if p.Y < l.RiverY0 {
			near, far = l.RiverY0-1.5, l.RiverY1+1.5
		}
		if !c.exited {
			// из ядра села выходим по его дорогам. Точка выхода выбирается ОДИН раз (иначе путь мечется):
			// если на пути река — к началу моста, иначе — за объездную в сторону цели.
			if c.exit == nil {
				if math.Abs(p.X) > l.VillageHalf+5 || math.Abs(p.Y) > l.VillageHalf+5 {
					c.exited = true
				} else if riverOnPath(l, p.X, p.Y, tx, ty) {
					c.exit = &[2]float64{bx, near}
				} else {
					r := l.VillageHalf + 12
					dx, dy := tx-p.X, ty-p.Y
					ex, ey := 0.0, 0.0
					if math.Abs(dx) >= math.Abs(dy) {
						ex, ey = math.Copysign(r, dx), clampf(p.Y+dy/math.Abs(dx)*(r-math.Copysign(p.X, dx)), -r, r)
					} else {
						ey, ex = math.Copysign(r, dy), clampf(p.X+dx/math.Abs(dy)*(r-math.Copysign(p.Y, dy)), -r, r)
					}
					ex, ey = l.Free(ex, ey)
					c.exit = &[2]float64{ex, ey}
				}
			}
			if c.exit != nil {
				if math.Hypot(c.exit[0]-p.X, c.exit[1]-p.Y) > 2 {
					gx, gy = nextWaypoint(w, l, e, p.X, p.Y, c.exit[0], c.exit[1])
				} else {
					c.exited = true
				}
			}
		}
		if c.exited && riverOnPath(l, p.X, p.Y, tx, ty) {
			// река между телом и целью — только через мост
			if p.Y >= l.RiverY0 && p.Y <= l.RiverY1 || math.Abs(p.X-bx) <= 1.5 && math.Abs(p.Y-near) <= 3 {
				gx, gy = bx, far // на мосту или у его начала — на тот берег
			} else {
				gx, gy = bx, near
			}
		}
	}
	dx, dy := gx-p.X, gy-p.Y
	n := math.Hypot(dx, dy)
	if n < 1e-6 {
		return false
	}
	if !inScene { // в поле шаг зависит от склона; кручу клиент-менеджер одолевает ползком, а не встаёт
		speed *= math.Max(0.2, TerrainMult(w, terrainFor(w), e, dx, dy))
	}
	step := math.Min(speed*dt, n)
	p.X, p.Y = l.ResolveMove(p.X, p.Y, p.X+dx/n*step, p.Y+dy/n*step)
	p.Facing = math.Mod(math.Atan2(dy, dx)*180/math.Pi+360, 360)
	if d < c.Best-0.05 {
		c.Best, c.Stuck = d, 0
	} else if c.Stuck += dt; c.Stuck > 3 {
		if nav := navOf(e); nav != nil {
			nav.Route = nil
		}
		// упёрлись (угол дома, берег) — шаг в сторону от преграды, без скачков
		p.X, p.Y = l.Free(p.X+dy/n*2, p.Y-dx/n*2)
		c.Stuck = 1
		c.Best = math.MaxFloat64
	}
	return false
}

// riverOnPath — пересекает ли прямой путь от тела к цели реку села (полоса воды лежит только в
// пределах земли села: дальше её нет, и обходить нечего).
func riverOnPath(l *Layout, px, py, tx, ty float64) bool {
	if !l.HasRiver {
		return false
	}
	if py >= l.RiverY0 && py <= l.RiverY1 {
		return true // уже на мосту
	}
	mid := (l.RiverY0 + l.RiverY1) / 2
	if (py-mid)*(ty-mid) >= 0 {
		return false // цель на том же берегу
	}
	for _, y := range []float64{l.RiverY0, l.RiverY1} {
		k := (y - py) / (ty - py)
		if x := px + (tx-px)*k; math.Abs(x) <= l.Half+2 {
			return true
		}
	}
	return false
}

// walkTarget — куда именно встать у места. Для корчмы это СТОЙКА внутри здания: пиво продают только
// у неё (atTavernCounter), а порог снаружи для клиента-менеджера был бы тупиком.
func walkTarget(l *Layout, location string) (x, y float64, ok bool) {
	if location == "tavern" {
		if cx, cy, _, has := l.TavernCounter(); has && !l.Blocked(cx, cy) {
			return cx, cy, true
		}
	}
	t, has := l.POIs[location]
	if !has {
		return 0, 0, false
	}
	x, y = l.Free(t[0], t[1])
	return x, y, true
}

// CancelWalk — прекратить самоход (ручной шаг, дорога, смерть).
func CancelWalk(w *core.World, e *core.Entity) {
	if autoWalkOf(e) != nil {
		w.Detach(e, "autowalk")
	}
}

// StepAutoWalk — шаг самохода для тел под управлением игроков.
func StepAutoWalk(w *core.World, dt float64, controlled func(core.ID) bool) {
	if dt <= 0 || !w.HasKind("autowalk") {
		return
	}
	var walkers []*core.Entity
	w.Each("autowalk", func(e *core.Entity) {
		if controlled == nil || controlled(e.ID) {
			walkers = append(walkers, e)
		}
	})
	for _, e := range walkers {
		c, p := autoWalkOf(e), ScenePos(e)
		v := core.Vitals(e)
		if p == nil || OnRoad(e) || p.Indoor != "" || (v != nil && v.Health <= 0) {
			CancelWalk(w, e)
			continue
		}
		if c.Site != "" {
			if stepFieldWalk(w, e, c, p, dt) {
				CancelWalk(w, e)
			}
			continue
		}
		l := layoutFor(w, e)
		if fx, fy, ok := walkTarget(l, c.Loc); !ok { // земли сменились (бесшовный мир) — такого места тут нет
			CancelWalk(w, e)
			continue
		} else if fx != c.TX || fy != c.TY {
			c.TX, c.TY = fx, fy
		}
		d := math.Hypot(c.TX-p.X, c.TY-p.Y)
		if d <= autoWalkArrive {
			p.At = c.Loc // пришёл именно сюда (а не к соседнему порогу, что случайно ближе)
			CancelWalk(w, e)
			continue
		}
		wx, wy := nextWaypoint(w, l, e, p.X, p.Y, c.TX, c.TY)
		dx, dy := wx-p.X, wy-p.Y
		n := math.Hypot(dx, dy)
		if n < 1e-6 {
			continue
		}
		step := math.Min(autoWalkSpeed*dt, n)
		nx, ny := l.ResolveMove(p.X, p.Y, p.X+dx/n*step, p.Y+dy/n*step)
		p.X, p.Y = nx, ny
		p.Facing = math.Mod(math.Atan2(dy, dx)*180/math.Pi+360, 360)
		// застряли (угол дома, чужая постройка) — перепрокладываем путь; совсем никак — встаём у цели,
		// чтобы игрок-менеджер не остался запертым без единого доступного дела
		if d < c.Best-0.05 {
			c.Best, c.Stuck = d, 0
		} else if c.Stuck += dt; c.Stuck > 2 {
			if nav := navOf(e); nav != nil {
				nav.Route = nil
			}
			if c.Stuck > 8 {
				p.X, p.Y, p.At = c.TX, c.TY, c.Loc
				CancelWalk(w, e)
			}
		}
	}
}

// WalkView — куда идёт тело (me.walk): место и сколько осталось по прямой. nil — стоит.
func WalkView(w *core.World, e *core.Entity) any {
	c, p := autoWalkOf(e), ScenePos(e)
	if c == nil || p == nil {
		return nil
	}
	if c.Site != "" { // поход к месту в поле: расстояние по миру
		left := 0.0
		if x, y, ok := bodyWorldXY(w, e); ok {
			left = math.Hypot(c.WE-x, c.WN-y)
		}
		return map[string]any{"to": "", "site": c.Site, "name": c.Name, "left": math.Round(left)}
	}
	return map[string]any{"to": c.Loc, "left": math.Round(math.Hypot(c.TX-p.X, c.TY-p.Y)*10) / 10}
}

// AtPOI — у какого места села сейчас стоит тело (me.at): ближайшая точка интереса в пределах
// обычной серверной дистанции дел. "" — ни у одного (в пути, в поле, на дороге).
func AtPOI(w *core.World, e *core.Entity) string {
	p := ScenePos(e)
	if p == nil || OnRoad(e) {
		return ""
	}
	l := layoutFor(w, e)
	if p.At != "" { // место, куда тело дошло самоходом, держится, пока оно не отошло
		if x, y, ok := walkTarget(l, p.At); ok && math.Hypot(p.X-x, p.Y-y) <= poiHere {
			return p.At
		}
		p.At = ""
	}
	if l.insideTavernBuilding(p.X, p.Y) {
		return "tavern"
	}
	best, bd := "", poiHere
	for k, q := range l.POIs {
		if d := math.Hypot(p.X-q[0], p.Y-q[1]); d < bd || (d == bd && k < best) {
			best, bd = k, d
		}
	}
	return best
}
