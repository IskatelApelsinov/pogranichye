package sim

import (
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// --- Дипломатия держав: отношения складываются из дел, не из скрипта ---
// Война рушит «стояние» воюющих, торговля (дошедшие караваны, см. CaravanSystem) — растит. Со
// временем всё тянется к нейтралитету (старые обиды и старая дружба тускнеют). Из «стояния»
// выводятся союз (не воюют, дороги безопаснее) и вражда (к ней тянет война) — без отдельных флагов.
var DebugAlliances, DebugRivalries int

type DiplomacySystem struct{}

func (DiplomacySystem) Name() string { return "diplomacy" }

const (
	warEnmityPerDay = 2.0    // насколько падает стояние воюющих за день
	relationDecay   = 0.0015 // как стояние тянется к нейтралитету в день (обиды/дружба тускнеют)
	peaceWarmth     = 0.15   // соседи, что долго не воюют и не враги, потихоньку сближаются (к союзу)
)

func (DiplomacySystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	var realms []*core.Entity
	var rs []*econ.RealmC
	w.Each("realm", func(re *core.Entity) {
		realms = append(realms, re)
		rs = append(rs, econ.Realm(re))
	})

	// война точит вражду, а всё прочее тускнеет к нейтралитету (старые обиды/дружба)
	for _, r := range rs {
		if r == nil {
			continue
		}
		if r.WarWith != 0 {
			r.Nudge(r.WarWith, -warEnmityPerDay)
		}
		for other, v := range r.Relations {
			if other == r.WarWith {
				continue
			}
			r.Relations[other] = v * (1 - relationDecay)
		}
	}

	// долгий мир роднит: пары, что НЕ воюют друг с другом и не во вражде, потихоньку теплеют —
	// так из устойчивого мира сам собой вырастает союз, а враги остаются холодными до примирения.
	for i := 0; i < len(rs); i++ {
		for j := i + 1; j < len(rs); j++ {
			a, b := rs[i], rs[j]
			if a == nil || b == nil {
				continue
			}
			ai, bi := realms[i].ID, realms[j].ID
			if a.WarWith == bi || b.WarWith == ai {
				continue // воюют меж собой — не теплеют
			}
			if a.Stand(bi) <= econ.RivalAt || b.Stand(ai) <= econ.RivalAt {
				continue // пока враги — сперва должны остыть (через decay), потом теплеть
			}
			a.Nudge(bi, peaceWarmth)
			b.Nudge(ai, peaceWarmth)
		}
	}
}

// relationsSnapshot — сколько сейчас пар в союзе и во вражде (для отчёта/диагностики). Пара A-B
// считается один раз по взгляду A (отношения мы держим симметрично — обе стороны двигаем вместе).
func relationsSnapshot(w *core.World) (allies, rivals int) {
	var rs []*econ.RealmC
	var ids []core.ID
	w.Each("realm", func(re *core.Entity) {
		rs = append(rs, econ.Realm(re))
		ids = append(ids, re.ID)
	})
	for i := 0; i < len(rs); i++ {
		for j := i + 1; j < len(rs); j++ {
			if econ.Allied(rs[i], ids[j]) {
				allies++
			} else if econ.IsRival(rs[i], ids[j]) {
				rivals++
			}
		}
	}
	return
}
