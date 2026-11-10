package sim

import (
	"sort"

	"pogranichye/core"
)

// «Температура» областей (A-Life LoD, v1.10). Канон: мир тикает ВСЕГДА, но чем дальше от игроков, тем
// реже и грубее. Три уровня поселения:
//   hot  (2) — здесь игрок: полная симуляция, у жителей есть тела в сцене, ходят в реальном времени;
//   warm (1) — тот же край: нужды считаются раз в 2 часа одним шагом;
//   cold (0) — далеко: нужды раз в 4 часа одним шагом, тел в сцене НЕТ.
// «Математическая архивация»: у жителя холодного поселения нет позиции и маршрута — только расписание
// (цель/занятие). Когда игрок приходит, положение каждого ВЫЧИСЛЯЕТСЯ из его текущего занятия (он уже
// «на месте», а не идёт от колодца), когда уходит — тела и враги сцены снимаются. Экономика, труд и
// решения (mind/econ) идут каждый час для всех — мир живёт одинаково честно, дешевеет только то, что
// можно точно проинтегрировать большим шагом, и то, чего никто не видит.

func init() {
	core.Register("heat", func() core.Component { return &HeatC{} })
}

// HeatC — температура поселения.
type HeatC struct {
	Level int `json:"level"`
}

func (c *HeatC) Kind() string { return "heat" }

func heatOf(se *core.Entity) *HeatC {
	if se == nil {
		return nil
	}
	c, _ := se.Get("heat").(*HeatC)
	return c
}

// isHot — горячо ли поселение. Без HeatC (тесты/инструменты без сервера) всё считается горячим.
func isHot(w *core.World, settlement core.ID) bool {
	h := heatOf(w.Get(settlement))
	return h == nil || h.Level >= 2
}

// lodStep — сколько часов представляет этот тик для жителя (0 — пропустить, его очередь не сейчас).
// Очередь разнесена по id, чтобы холодные не тикали все разом (ровная нагрузка).
func lodStep(w *core.World, e *core.Entity, totalHours int64) float64 {
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return 1
	}
	h := heatOf(w.Get(pl.Settlement))
	if h == nil || h.Level >= 2 || Player(e) != nil {
		return 1
	}
	n := int64(4)
	if h.Level == 1 {
		n = 2
	}
	if (totalHours+int64(e.ID))%n != 0 {
		return 0
	}
	return float64(n)
}

// UpdateHeat пересчитывает температуру поселений от положения живых игроков. Возвращает счётчики
// (hot, warm, cold) для диагностики. При нагреве жители получают вычисленные места, при остывании —
// сцена снимается.
func UpdateHeat(w *core.World, controlled func(core.ID) bool) (hot, warm, cold int) {
	hotSet := map[core.ID]bool{}
	warmRegion := map[core.ID]bool{}
	w.Each("player", func(e *core.Entity) {
		if controlled != nil && !controlled(e.ID) {
			return
		}
		mark := func(sid core.ID) {
			if s := core.Settle(w.Get(sid)); s != nil {
				hotSet[sid] = true
				warmRegion[s.Region] = true
			}
		}
		if j := journeyOf(e); j != nil {
			// Путник уже не в сцене исходного поселения: греем только то, КУДА он идёт (там его встретят
			// жители на местах), а откуда ушёл — остывает, если там больше никого.
			mark(j.To)
		} else if pl := core.Place(e); pl != nil {
			mark(pl.Settlement)
		}
	})
	var heated, cooled []core.ID
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		lvl := 0
		switch {
		case hotSet[se.ID]:
			lvl = 2
		case s != nil && warmRegion[s.Region]:
			lvl = 1
		}
		h := heatOf(se)
		if h == nil {
			h = &HeatC{Level: -1}
			w.Attach(se, h)
		}
		if lvl == 2 && h.Level != 2 {
			heated = append(heated, se.ID)
		}
		if lvl != 2 && h.Level == 2 {
			cooled = append(cooled, se.ID)
		}
		h.Level = lvl
		switch lvl {
		case 2:
			hot++
		case 1:
			warm++
		default:
			cold++
		}
	})
	for _, sid := range heated {
		materialize(w, sid, controlled)
	}
	for _, sid := range cooled {
		archive(w, sid)
	}
	return
}

// materialize — «разархивация»: жители поселения получают тела в сцене там, где им положено быть по
// текущему занятию (POI цели + личный слот), а не идут туда от точки появления.
func materialize(w *core.World, settlement core.ID, controlled func(core.ID) bool) {
	res := residentsOf(w, settlement)
	sort.Slice(res, func(i, j int) bool { return res[i].ID < res[j].ID })
	rank := map[[2]float64]int{}
	for _, e := range res {
		if Player(e) != nil && controlled != nil && controlled(e.ID) {
			continue
		}
		id, m := core.Ident(e), core.Mind(e)
		if id == nil || m == nil {
			continue
		}
		bx, by := targetForGoal(id.Role, m.Goal)
		key := [2]float64{bx, by}
		ox, oy := parkingSlot(rank[key])
		rank[key]++
		x, y := bx+ox, by+oy
		if Blocked(x, y) {
			x, y = bx, by
		}
		p := ScenePos(e)
		if p == nil {
			p = &ScenePosC{Stam: 1}
			w.Attach(e, p)
		}
		p.X, p.Y, p.Facing, p.Stuck = x, y, facingDeg(bx-x, by-y, 90), 0
		if nav := navOf(e); nav != nil {
			nav.Route = nil
		}
	}
}

// archive — «архивация»: поселение остыло, тела жителей и враги сцены снимаются (остаётся расписание
// и поголовье угодий — из них всё восстановится).
func archive(w *core.World, settlement core.ID) {
	for _, e := range residentsOf(w, settlement) {
		if Player(e) != nil {
			continue
		}
		if ScenePos(e) != nil {
			w.Detach(e, "scenepos")
		}
		if navOf(e) != nil {
			w.Detach(e, "nav")
		}
	}
	dropFoes(w, settlement)
}
