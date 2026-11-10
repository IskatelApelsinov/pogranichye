package sim

import (
	"math"
	"sort"

	"pogranichye/core"
	"pogranichye/dice"
)

// Выезды и физический выход на тракт (v1.15). У каждого поселения на краю его земли есть выезды — по
// одному на каждый тракт к соседу (в ту сторону, где сосед лежит на карте мира). Дошёл ногами до
// выезда — вышел на тракт к этому соседу; пришёл — стоишь у въезда С ТОЙ СТОРОНЫ, откуда пришёл, а не
// посреди площади. Так деревни связаны непрерывно: куда идёшь, туда и выходишь.
// Выход из «котла» — перевал: особый выезд у деревни, ближайшей к внешнему миру. Через него путь
// долгий и ведёт в ближайшее поселение за горами; обратно — тем же перевалом. Телепорт-врата
// (op:"travel") остаются только как служебный переход тонкого клиента.

// Exit — выезд из поселения.
type Exit struct {
	To   core.ID
	X, Y float64
	Pass bool // перевал через горы
}

const exitReach = 5.0 // м — дошёл до выезда

func exitPoint(from, to *core.SettlementC) (float64, float64) {
	ang := math.Atan2(to.Y-from.Y, to.X-from.X)
	r := WorldHalf - 6
	c, s := math.Cos(ang), math.Sin(ang)
	k := r / math.Max(math.Abs(c), math.Abs(s)) // точка на краю квадрата земли
	x, y := c*k, s*k
	for i := 0; i < 30 && Blocked(x, y); i++ { // не в реке и не в стене: сдвигаем вдоль края
		if math.Abs(c) > math.Abs(s) {
			y += 4
		} else {
			x += 4
		}
	}
	return x, y
}

// passPair — концы перевала: деревня котла и поселение внешнего мира, ближайшие друг к другу.
func passPair(w *core.World) (in, out core.ID) {
	best := math.MaxFloat64
	var ins, outs []*core.Entity
	w.Each("settlement", func(se *core.Entity) {
		if isOuter(w, core.Settle(se)) {
			outs = append(outs, se)
		} else {
			ins = append(ins, se)
		}
	})
	for _, a := range ins {
		for _, b := range outs {
			sa, sb := core.Settle(a), core.Settle(b)
			if d := math.Hypot(sa.X-sb.X, sa.Y-sb.Y); d < best || (d == best && a.ID < in) {
				best, in, out = d, a.ID, b.ID
			}
		}
	}
	return
}

// Exits — выезды поселения: тракты к соседям и, если есть, перевал.
func Exits(w *core.World, settlement core.ID) []Exit {
	se := w.Get(settlement)
	s := core.Settle(se)
	if s == nil {
		return nil
	}
	var out []Exit
	for _, r := range RoadGraph(w, isOuter(w, s)) {
		other := core.ID(0)
		if r.A == settlement {
			other = r.B
		} else if r.B == settlement {
			other = r.A
		}
		if other != 0 {
			x, y := exitPoint(s, core.Settle(w.Get(other)))
			out = append(out, Exit{To: other, X: x, Y: y})
		}
	}
	if in, o := passPair(w); in != 0 && (settlement == in || settlement == o) {
		to := o
		if settlement == o {
			to = in
		}
		x, y := exitPoint(s, core.Settle(w.Get(to)))
		out = append(out, Exit{To: to, X: x, Y: y, Pass: true})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].To < out[j].To })
	return out
}

// StepExits — игрок дошёл ногами до выезда: выходит на тракт (или на перевал) к соседу.
func StepExits(w *core.World, controlled func(core.ID) bool, hub func(core.ID) *dice.Stream) {
	var walkers []*core.Entity
	w.Each("player", func(e *core.Entity) {
		if controlled(e.ID) && !OnRoad(e) && ScenePos(e) != nil && core.Place(e) != nil {
			walkers = append(walkers, e)
		}
	})
	for _, e := range walkers {
		p := ScenePos(e)
		if math.Abs(p.X) < WorldHalf-14 && math.Abs(p.Y) < WorldHalf-14 {
			continue // не у края — выездов рядом нет
		}
		for _, ex := range Exits(w, core.Place(e).Settlement) {
			if math.Hypot(p.X-ex.X, p.Y-ex.Y) > exitReach {
				continue
			}
			if pc, _ := e.Get("party").(*PartyC); pc != nil && pc.Leader != 0 && pc.Leader != e.ID {
				break // отряд ведёт вожак
			}
			reqid := "exit:" + sid(ex.To) + ":" + sid(core.ID(EnsurePlayer(w, e).exitN))
			EnsurePlayer(w, e).exitN++
			if ex.Pass {
				startPass(w, e, ex.To)
			} else if ok, why := JourneyStart(w, e, ex.To, reqid, hub(e.ID)); !ok {
				p.X, p.Y = p.X*0.93, p.Y*0.93 // не пустили (нет сил) — отступил от выезда
				say(w, e, "road", "На тракт не выйти: "+why+".")
			}
			break
		}
	}
}

// startPass — долгий путь через перевал между котлом и внешним миром.
func startPass(w *core.World, e *core.Entity, to core.ID) {
	pl := core.Place(e)
	if v := core.Vitals(e); v == nil || v.Energy < 20 {
		p := ScenePos(e)
		p.X, p.Y = p.X*0.93, p.Y*0.93
		say(w, e, "road", "Перевал не одолеть без сил — отдохни и поешь.")
		return
	}
	total := 120.0
	if _, h := horseOf(w, e); h != nil && Mounted(e) {
		total, h.Stam = 80, math.Max(0, h.Stam-45)
	}
	ev := RoadEvent{At: 0.5, Kind: "camp", Text: "На седловине перевала ветер валит с ног. Под скалой — старое кострище.",
		Options: []string{"rest", "pass"}}
	w.Attach(e, &JourneyC{From: pl.Settlement, To: to, Total: total, Plan: []RoadEvent{ev}, Via: []string{"перевал"}})
	EndTalk(e)
	say(w, e, "road", "Тропа уходит вверх, в горы. Впереди перевал.")
}

// arriveSpot — где встать по приходе: у въезда со стороны, откуда пришёл, в десяти шагах внутрь.
func arriveSpot(w *core.World, to, from core.ID) (float64, float64, bool) {
	for _, ex := range Exits(w, to) {
		if ex.To == from {
			d := math.Hypot(ex.X, ex.Y)
			k := (d - 12) / d
			x, y := ex.X*k, ex.Y*k
			if !Blocked(x, y) {
				return x, y, true
			}
		}
	}
	return 0, 0, false
}

// ExitsView — выезды для кадра (place.exits): куда ведёт (имя — только если знаешь), где, тракт или перевал.
func ExitsView(w *core.World, e *core.Entity, settlement core.ID) []map[string]any {
	out := []map[string]any{}
	for _, ex := range Exits(w, settlement) {
		name := ""
		if knowsPlace(e, ex.To) {
			name = core.Settle(w.Get(ex.To)).Name
		}
		kind := "road"
		if ex.Pass {
			kind = "pass"
		}
		out = append(out, map[string]any{"to": eidStr(ex.To), "name": name, "x": round1x(ex.X), "y": round1x(ex.Y), "kind": kind})
	}
	return out
}
