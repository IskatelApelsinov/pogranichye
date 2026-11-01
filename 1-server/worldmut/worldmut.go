// Пакет worldmut — МУТАЦИЯ МИРА: земля физически меняется от того, как её используют, а не стоит
// декорацией. Рубят лес без удержу — он редеет, и зверю в нём жить негде (меньше кабанов/медведей);
// дают отдохнуть — зарастает. Шахта вырабатывается насовсем. Тяжёлое колдовство истощает ману
// места (мёртвые зоны), но поле медленно восстанавливается к норме края. Крепкое сытое село
// распахивает новину — прибавляет пашню. Всё медленно и в пределах — мир дышит, а не скачет.
package worldmut

import (
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

type System struct{}

func (System) Name() string { return "worldmut" }

const (
	forestCutDrift  = 0.997  // перерубленный лес редеет в день
	forestGrowDrift = 1.0012 // нетронутый лес прирастает
	forestMin       = 60.0
	forestMax       = 900.0
	habitatDrift    = 0.4   // насколько фауна крупного зверя следует за здоровьем леса
	manaRegen       = 0.004 // поле маны возвращается к норме края за день (мёртвые зоны оживают)
	fieldsMax       = 8     // потолок пашен у села (распашка новины)
	clearLandChance = 0.004 // редкое событие расчистки новины у крепкого села
)

func (System) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	s := hub.Stream("worldmut")

	// население и число пашен по сёлам — один проход (для распашки новины)
	pop := map[core.ID]int{}
	w.Each("place", func(e *core.Entity) {
		if core.Ident(e) == nil {
			return
		}
		if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
			pop[pl.Settlement]++
		}
	})
	fields := map[core.ID]int{}
	forest := map[core.ID]*econ.ResourceNodeC{}
	w.Each("resnode", func(ne *core.Entity) {
		n := econ.Node(ne)
		switch n.Node {
		case econ.NodeField:
			fields[n.Settlement]++
		case econ.NodeForest:
			forest[n.Settlement] = n
			// лес редеет при перерубе, зарастает в покое — в пределах
			ratio := 0.0
			if n.Cap > 0 {
				ratio = n.Stock / n.Cap
			}
			switch {
			case ratio < 0.25:
				n.Cap *= forestCutDrift
			case ratio > 0.85:
				n.Cap *= forestGrowDrift
			}
			if n.Cap < forestMin {
				n.Cap = forestMin
			}
			if n.Cap > forestMax {
				n.Cap = forestMax
			}
			if n.Stock > n.Cap {
				n.Stock = n.Cap
			}
		case econ.NodeMine:
			// выработанная шахта — насовсем: руда кончилась, жила мертва (помечаем нулевым пределом)
			if n.Stock < 1 && n.Cap > 0 {
				n.Cap = 0
				n.Stock = 0
			}
		}
	})

	// крупный зверь живёт лесом: редеет лес — редеет и зверь (habitat), зарастает — возвращается
	w.Each("settlement", func(se *core.Entity) {
		set := core.Settle(se)
		// мана поля тянется к норме края (восстановление после колдовского истощения)
		if reg := w.Get(set.Region); reg != nil {
			if r := core.Region(reg); r != nil && r.Mana > 0 {
				set.Mana += (r.Mana - set.Mana) * manaRegen
			}
		}
		f := battle.Fauna(se)
		fn := forest[se.ID]
		if f != nil && fn != nil {
			health := 0.0
			if fn.Cap > 0 {
				health = fn.Cap / 400.0 // 400 — ориентир здорового леса
			}
			// текущий предел тянется к ПРИРОДНОЙ опоре, умноженной на здоровье леса: вырубили —
			// опора та же, но крупному зверю жить негде (предел падает вдвое); зарос — чуть выше.
			hf := 1.0
			if health < 0.6 {
				hf = 0.5
			} else if health > 1.1 {
				hf = 1.15
			}
			for _, sp := range []string{"boar", "wolf", "bear"} {
				target := f.Base[sp] * hf
				f.Cap[sp] += (target - f.Cap[sp]) * habitatDrift * 0.02
				if f.Cap[sp] < 0 {
					f.Cap[sp] = 0
				}
				if f.Pop[sp] > f.Cap[sp] {
					f.Pop[sp] = f.Cap[sp] // жить столько уже негде
				}
			}
		}

		// распашка новины: крепкое сытое село изредка расчищает новое поле (пашни больше — хлеба больше)
		if pop[se.ID] >= 16 && fields[se.ID] < fieldsMax && econ.FeedableFood(se) > float64(pop[se.ID])*20 {
			if s.Chance(clearLandChance) {
				w.Spawn(&econ.ResourceNodeC{Node: econ.NodeField, Good: econ.GoodWheat, Settlement: se.ID})
				fields[se.ID]++
			}
		}
	})
}
