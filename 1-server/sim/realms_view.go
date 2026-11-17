package sim

import (
	"math"
	"sort"

	"pogranichye/core"
	"pogranichye/econ"
)

// Державы и караваны на виду (v1.24). Мир уже давно жил державами: они собирают десятину, дружат,
// воюют, шлют обозы между столицами (econ.RealmC, econ.CaravanC). Но игрок об этом не знал: ни в
// чьих землях стоит село, ни с кем его держава воюет, ни что по тракту идёт обоз. Здесь только
// ПОКАЗ того, что уже есть в мире: ни одно правило держав и торговли не меняется.

// realmOf — держава, которой подвластно поселение (nil — вольная земля).
func realmOf(w *core.World, settlement core.ID) (*core.Entity, *econ.RealmC) {
	var re *core.Entity
	var rc *econ.RealmC
	w.Each("realm", func(e *core.Entity) {
		r := econ.Realm(e)
		if r == nil || rc != nil {
			return
		}
		for _, sid := range r.Territory {
			if sid == settlement {
				re, rc = e, r
				return
			}
		}
	})
	return re, rc
}

func settleName(w2 *core.World, id core.ID) string {
	if s := settleOf(w2, id); s != nil {
		return s.Name
	}
	return ""
}

func realmTitle(w *core.World, id core.ID) string {
	if e := w.Get(id); e != nil {
		if r := econ.Realm(e); r != nil {
			return r.Name
		}
	}
	return ""
}

// PlaceRealmView — чьи это земли (place.realm): держава, её столица, десятина, с кем она воюет,
// с кем дружит и с кем во вражде. nil — вольная земля, над селом нет державы.
func PlaceRealmView(w *core.World, settlement core.ID) any {
	re, r := realmOf(w, settlement)
	if r == nil {
		return nil
	}
	allies, rivals := []string{}, []string{}
	ids := make([]core.ID, 0, len(r.Relations))
	for id := range r.Relations {
		ids = append(ids, id)
	}
	sort.Slice(ids, func(i, j int) bool { return ids[i] < ids[j] })
	for _, id := range ids {
		n := realmTitle(w, id)
		if n == "" {
			continue
		}
		if econ.Allied(r, id) {
			allies = append(allies, n)
		} else if econ.IsRival(r, id) {
			rivals = append(rivals, n)
		}
	}
	return map[string]any{"id": eidStr(re.ID), "name": r.Name, "capital": settleName(w, r.Capital),
		"is_capital": r.Capital == settlement, "tax": math.Round(r.TaxRate*100) / 100, "size": len(r.Territory),
		"war_with": realmTitle(w, r.WarWith), "allies": allies, "rivals": rivals}
}

// caravanAt — где сейчас обоз: точка на прямой между столицами по доле пройденного. Обоз движется
// раз в сутки рывком (econ.CaravanSystem); чтобы на карте он шёл, а не прыгал, внутри суток
// добавляется доля дневного перехода по часу дня.
func caravanAt(w *core.World, c *econ.CaravanC, hour int) (e, n, frac float64, ok bool) {
	a, b := settleOf(w, c.From), settleOf(w, c.To)
	if a == nil || b == nil || c.Dist <= 0 {
		return 0, 0, 0, false
	}
	frac = clampf((c.Progress+econ.CaravanSpeed*float64(hour)/float64(core.HoursPerDay))/c.Dist, 0, 1)
	ae, an := Anchor(w, a)
	be, bn := Anchor(w, b)
	return ae + (be-ae)*frac, an + (bn-an)*frac, frac, true
}

// CaravansView — все обозы в пути (для /api/world): что везут, откуда и куда, где сейчас.
func CaravansView(w *core.World, t *Terrain, hour int) []map[string]any {
	out := []map[string]any{}
	w.Each("caravan", func(ce *core.Entity) {
		c := econ.Caravan(ce)
		if c == nil {
			return
		}
		e, n, frac, ok := caravanAt(w, c, hour)
		if !ok {
			return
		}
		out = append(out, map[string]any{"id": eidStr(ce.ID), "good": c.Good, "amount": math.Round(c.Amount),
			"from": eidStr(c.From), "to": eidStr(c.To), "seller": realmTitle(w, c.Seller),
			"frame": 0, "e": r3(e), "n": r3(n), "u": r3(t.Height(e, n)), "progress": math.Round(frac*100) / 100})
	})
	return out
}

const caravanSight = 8000.0 // м: с такого расстояния путник знает об обозе на тракте

// NearCaravansView — обозы неподалёку от игрока (place.caravans): что везут, куда идут, где они.
func NearCaravansView(w *core.World, t *Terrain, e *core.Entity, hour int) []map[string]any {
	out := []map[string]any{}
	x, y, ok := bodyWorldXY(w, e)
	if !ok || !w.HasKind("caravan") {
		return out
	}
	w.Each("caravan", func(ce *core.Entity) {
		c := econ.Caravan(ce)
		if c == nil {
			return
		}
		ce2, cn, _, has := caravanAt(w, c, hour)
		if !has {
			return
		}
		d := math.Hypot(ce2-x, cn-y)
		if d > caravanSight {
			return
		}
		out = append(out, map[string]any{"id": eidStr(ce.ID), "good": c.Good, "good_name": ItemName(c.Good),
			"amount": math.Round(c.Amount), "from": settleName(w, c.From), "to": settleName(w, c.To),
			"seller": realmTitle(w, c.Seller), "e": r3(ce2), "n": r3(cn), "u": r3(t.Height(ce2, cn)),
			"dist": math.Round(d), "dir": compass(ce2-x, cn-y)})
	})
	sort.Slice(out, func(i, j int) bool { return out[i]["dist"].(float64) < out[j]["dist"].(float64) })
	return out
}

// RealmsView — державы мира (для /api/world): имя, столица, размер, война.
func RealmsView(w *core.World) []map[string]any {
	out := []map[string]any{}
	w.Each("realm", func(re *core.Entity) {
		if r := econ.Realm(re); r != nil {
			terr := make([]string, 0, len(r.Territory))
			for _, sid := range r.Territory {
				terr = append(terr, eidStr(sid))
			}
			war := ""
			if r.WarWith != 0 {
				war = eidStr(r.WarWith)
			}
			out = append(out, map[string]any{"id": eidStr(re.ID), "name": r.Name, "capital": eidStr(r.Capital),
				"size": len(r.Territory), "territory": terr, "war_with": war})
		}
	})
	return out
}
