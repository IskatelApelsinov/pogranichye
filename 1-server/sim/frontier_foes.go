package sim

import (
	"math"
	"sort"

	"pogranichye/core"
)

// Звери и разбойники фронтира (v1.30). Раньше враги жили только у околицы села: в 14 м от своей
// точки, привязанные к его сцене. За околицей, в урочищах, было пусто. Теперь дикие места
// населены: в логове — волки, в чаще — кабан, у кургана и руин — лихие люди, на стоянке — медведь.
// Они бродят широко вокруг своего места, ночью дальше, разбойники выходят к тропам; переходят
// межу между землями сёл, как и игрок. Это обычные враги (FoeC) — бой, добыча, уворот работают
// как везде. Убитых место помнит: новые придут через два дня.

const (
	frontierFoeReach = 1500.0 // м: места не дальше этого от игрока населяются
	frontierRespawn  = 2      // дней: столько место пустует после того, как его зачистили
	frontierFoeCap   = 10     // зверей и разбойников фронтира на одни земли, не больше
)

type frontierPack struct {
	species string
	n       int
	roam    float64 // м: как далеко уходят от своего места
	night   bool    // только в тёмные часы (на стоянке медведь — ночью)
}

// frontierFauna — кто живёт в урочище какого вида.
var frontierFauna = map[string]frontierPack{
	"den":     {"wolf", 3, 260, false},
	"thicket": {"boar", 2, 160, false},
	// курганы и руины — станы разбойников-жителей (bandits.go), безымянных тут больше нет
	"camp":  {"bear", 1, 200, true},
	"berry": {"fox", 1, 140, false},
	"herbs": {"hare", 2, 120, false},
}

func frontierCleared(u *WildUseC, id string) bool {
	if u == nil || u.Cleared == nil {
		return false
	}
	d, ok := u.Cleared[id]
	return ok && u.Today < d+frontierRespawn
}

// populateFrontierFoes — населить дикие места вокруг игроков в землях этого села.
func populateFrontierFoes(w *core.World, se *core.Entity, ps []*core.Entity, hour int) {
	s := core.Settle(se)
	if s == nil || !Seamless(w) {
		return
	}
	ae, an := Anchor(w, s)
	have := map[string]int{}
	total := 0
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Wild != "" {
			have[f.Wild]++
			if f.Settlement == se.ID {
				total++
			}
		}
	})
	t := terrainFor(w)
	u := peekWildUse(w)
	night := hour >= 21 || hour < 5
	seen := map[string]bool{}
	var places []WildPlace
	for _, pe := range ps {
		x, y, ok := bodyWorldXY(w, pe)
		if !ok {
			continue
		}
		for _, p := range WildPlacesNear(w, t, x, y, frontierFoeReach) {
			if !seen[p.ID] {
				seen[p.ID] = true
				places = append(places, p)
			}
		}
	}
	sort.Slice(places, func(i, j int) bool { return places[i].ID < places[j].ID })
	for _, p := range places {
		pack, ok := frontierFauna[p.Kind]
		if !ok || have[p.ID] > 0 || frontierCleared(u, p.ID) || (pack.night && !night) {
			continue
		}
		if nearestSettlementTo(w, p.E, p.N) != se.ID {
			continue // место в чужих землях — его населят, когда туда придут
		}
		st := foeStats[pack.species]
		for k := 0; k < pack.n && total < frontierFoeCap; k++ {
			ang := float64(k) * 2.4
			x, y := p.E-ae+math.Cos(ang)*4, p.N-an+math.Sin(ang)*4
			w.Spawn(&FoeC{Species: pack.species, Settlement: se.ID, X: x, Y: y, HomeX: p.E - ae, HomeY: p.N - an,
				HP: st.hp, Max: st.hp, State: "roam", WX: x, WY: y, Wild: p.ID, Roam: pack.roam})
			total++
		}
	}
}

// nearestSettlementTo — чьи это земли: ближайшее село к мировой точке.
func nearestSettlementTo(w *core.World, e, n float64) core.ID {
	var best core.ID
	bd := math.Inf(1)
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil {
			return
		}
		ae, an := Anchor(w, s)
		if d := math.Hypot(e-ae, n-an); d < bd || (d == bd && se.ID < best) {
			best, bd = se.ID, d
		}
	})
	return best
}

// StepFoeLands — зверь или разбойник фронтира, перешедший межу, попадает в земли соседнего села
// (как и игрок, StepLands): мировая точка та же, меняется только, от чьего центра считать.
func StepFoeLands(w *core.World) {
	if !Seamless(w) {
		return
	}
	w.Each("foe", func(fe *core.Entity) {
		f := Foe(fe)
		if f == nil || f.Wild == "" || f.State == "dead" {
			return
		}
		cur := settleOf(w, f.Settlement)
		if cur == nil {
			return
		}
		ae, an := Anchor(w, cur)
		we, wn := ae+f.X, an+f.Y
		nid := nearestSettlementTo(w, we, wn)
		if nid == f.Settlement || nid == 0 {
			return
		}
		ns := settleOf(w, nid)
		be, bn := Anchor(w, ns)
		if math.Hypot(we-be, wn-bn)+40 > math.Hypot(f.X, f.Y) {
			return // межа с запасом: у самой границы туда-сюда не прыгаем
		}
		dx, dy := ae-be, an-bn
		f.Settlement = nid
		f.X, f.Y = f.X+dx, f.Y+dy
		f.HomeX, f.HomeY = f.HomeX+dx, f.HomeY+dy
		f.WX, f.WY = f.WX+dx, f.WY+dy
	})
}

// frontierWanderTarget — куда идёт бродящий зверь фронтира: широко вокруг логова, ночью дальше;
// разбойник — к ближней тропе, где ходят люди.
func frontierWanderTarget(w *core.World, f *FoeC, r1, r2 float64, night bool) (float64, float64) {
	roam := f.Roam
	if night && f.Species != "bandit" {
		roam *= 1.5
	}
	ang := r1 * 2 * math.Pi
	d := roam * math.Sqrt(r2)
	x, y := f.HomeX+math.Cos(ang)*d, f.HomeY+math.Sin(ang)*d
	if f.Species == "bandit" && r2 < 0.6 {
		if s := settleOf(w, f.Settlement); s != nil {
			ae, an := Anchor(w, s)
			if tx, ty, ok := nearestTrailPoint(w, ae+f.HomeX, an+f.HomeY, roam*1.5); ok {
				return tx - ae, ty - an
			}
		}
	}
	return x, y
}

// nearestTrailPoint — ближайшая точка нитки тропы/тракта не дальше r.
func nearestTrailPoint(w *core.World, x, y, r float64) (float64, float64, bool) {
	list, _ := Trails(w)
	bx, by, bd := 0.0, 0.0, r
	for i := range list {
		for _, p := range list[i].Pts {
			if d := math.Hypot(p[0]-x, p[1]-y); d < bd {
				bx, by, bd = p[0], p[1], d
			}
		}
	}
	return bx, by, bd < r
}

// --- жители на промысле за околицей (v1.30) ---

// forayKinds — в какие урочища выходит житель с каким занятием.
var forayKinds = map[string][]string{
	"forage_herbs": {"herbs", "berry"},
	"chop_wood":    {"thicket"},
	"mine_ore":     {"outcrop"},
	"hunt_wild":    {"thicket", "berry"},
	"fish_river":   {"pond"},
}

const forayWalkReach = 1600.0 // м: дальше этого житель за день пешком на промысел не ходит

// forayTarget — куда идёт житель на промысел за околицу: в ближнее урочище своего ремесла в землях
// своего села. Ходит половина промысловиков (по чётности id) — остальные работают у села, чтобы
// село не пустело. ok=false — работает, как прежде, в угодьях раскладки.
func forayTarget(w *core.World, e *core.Entity, settlement core.ID, act string) (float64, float64, bool) {
	kinds := forayKinds[act]
	if len(kinds) == 0 || !Seamless(w) || uint64(e.ID)%2 == 1 {
		return 0, 0, false
	}
	s := settleOf(w, settlement)
	if s == nil {
		return 0, 0, false
	}
	ae, an := Anchor(w, s)
	var best *WildPlace
	bd := math.Inf(1)
	for _, p := range WildPlacesNear(w, terrainFor(w), ae, an, forayWalkReach) {
		ok := false
		for _, k := range kinds {
			ok = ok || p.Kind == k
		}
		if !ok || nearestSettlementTo(w, p.E, p.N) != settlement {
			continue
		}
		// у каждого своё место из ближних: не все травники села на одном лугу
		d := math.Hypot(p.E-ae, p.N-an) * (1 + float64((uint64(e.ID)*2654435761+hashReq(p.ID))%7)/10)
		if d < bd {
			pp := p
			best, bd = &pp, d
		}
	}
	if best == nil {
		return 0, 0, false
	}
	return best.E - ae, best.N - an, true
}
