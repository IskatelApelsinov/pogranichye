package sim

import (
	"math"

	"pogranichye/core"
)

// Межсельская область видимости (AOI, v1.22): этап 3 из RFC comms 0179 (этапы 1-2 — чанковая идея
// и физическое пересечение границы НПС — уже сделаны: scene-index не понадобился, т.к. основной
// сканер и так обходит ВСЕХ жителей мира, см. ниже; переход сделан в npc_walk.go/PR#473). Запрос
// владельца (comms 0182/0184/0196): у ограды соседнего села, стоящего близко (минимум 500 м —
// worldgen.go NewSettlement), житель должен быть виден через границу, а не только внутри своего
// же поселения — иначе бесшовный мир у самой межи выглядит дырявым.
//
// Протокол не меняется (без версии): neighbors[] и раньше несли и "pos" (локальная сцена), и
// "wpos" (мировые координаты, comms 0178) — здесь только РАСШИРЯЕТСЯ набор кандидатов и критерий
// расстояния, схема полей та же.

// aoiSettlementPrefilterM — предварительный отбор соседних сёл ПО ЯКОРЯМ: с каким запасом годится
// чужое село в кандидаты, прежде чем считать реальную мировую позицию каждого его жителя. Запас
// заведомо избыточен (сцена села ~300 м, минимум между сёлами 500 м — так что даже у самой межи
// ближний житель соседнего села редко дальше километра) — лишний кандидат просто не попадёт в
// итоговые N ближайших, настоящий отбор дальше идёт по реальному расстоянию при ранжировании.
const aoiSettlementPrefilterM = 1200.0

// AOICandidateSettlements — своё село смотрящего + любое другое, чей якорь не дальше
// aoiSettlementPrefilterM: дешёвый предварительный отбор (обход всех сёл мира, их редко больше
// нескольких сотен — тот же порядок, что и прочие обходы по миру за кадр), чтобы не считать
// мировую позицию (Terrain.Height) каждого жителя ВСЕГО мира ради пары соседей у границы.
func AOICandidateSettlements(w *core.World, origin core.ID) map[core.ID]bool {
	out := map[core.ID]bool{origin: true}
	os := settleOf(w, origin)
	if os == nil {
		return out
	}
	oe, on := Anchor(w, os)
	w.Each("settlement", func(se *core.Entity) {
		if se.ID == origin {
			return
		}
		s := core.Settle(se)
		e, n := Anchor(w, s)
		if math.Hypot(e-oe, n-on) <= aoiSettlementPrefilterM {
			out[se.ID] = true
		}
	})
	return out
}

// AOINeighbors — до n ближайших к смотрящему жителей/игроков по РЕАЛЬНОМУ расстоянию в мире
// (WorldPosOf), а не по локальной сцене одного села — поэтому сосед из соседнего (близкого) села
// не прячется только из-за того, что PlaceC.Settlement у него другой. Для мира без близких
// соседей (большинство сёл — обычная дистанция намного больше aoiSettlementPrefilterM) это даёт
// РОВНО тот же список и тот же порядок, что и старое «ближайшие в своём селе» (кандидат-сёл всего
// одно — своё, расстояние совпадает с локальной сценой, т.к. якорь общий). t — общий Terrain кадра
// (считать его на каждого соседа — O(сёл) обход мира ещё раз, см. предупреждение в
// WorldPosViewUsing). Переселенцы в пути проходят anchor-prefilter: их Place указывает ближайшее
// село маршрута, но тело уже может быть далеко от его якоря; для них решает только WorldPosOf.
// skip — сам смотрящий (игрок/НПС, чей кадр собирается).
func AOINeighbors(w *core.World, t *Terrain, skip *core.Entity, origin core.ID, n int) []*core.Entity {
	if n <= 0 || skip == nil {
		return nil
	}
	op, ok := WorldPosOf(w, t, skip)
	if !ok {
		return nil
	}
	cand := AOICandidateSettlements(w, origin)
	type row struct {
		e *core.Entity
		d float64
	}
	rows := make([]row, 0, n)
	w.Each("place", func(o *core.Entity) {
		if o.ID == skip.ID || OnRoad(o) {
			return
		}
		p := core.Place(o)
		if p == nil || (!cand[p.Settlement] && Migration(o) == nil) || core.Ident(o) == nil {
			return
		}
		np, ok := WorldPosOf(w, t, o)
		if !ok {
			return
		}
		d, ok := WorldDist(op, np)
		if !ok {
			return
		}
		at := len(rows)
		for i := range rows {
			if d < rows[i].d || (d == rows[i].d && o.ID < rows[i].e.ID) {
				at = i
				break
			}
		}
		if at >= n {
			return
		}
		if len(rows) < n {
			rows = append(rows, row{})
		}
		copy(rows[at+1:], rows[at:])
		rows[at] = row{o, d}
	})
	out := make([]*core.Entity, len(rows))
	for i := range rows {
		out[i] = rows[i].e
	}
	return out
}
