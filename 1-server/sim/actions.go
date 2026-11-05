package sim

import (
	"math"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Прямые действия игрока с миром (интерактивность v1.3): рубка леса, рыбалка, магический разряд.
// Все — серверно-авторитетные, с реальными условиями (место, силы, мана) и реальным исходом
// (товар в закрома, убыль зверя). Повтор того же reqid идемпотентен.

const (
	chopEnergy = 8.0  // сил на одну рубку
	fishEnergy = 7.0  // сил на один заброс
	chopYield  = 1.0  // брёвен за рубку
	fishYield  = 1.0  // рыбин за удачный лов
	actReach   = 6.0  // м — насколько близко к POI/зоне надо стоять для действия

	sparkCost  = 25.0 // маны на разряд
	sparkManaMax = 100.0
	sparkRegen = 6.0 // маны в секунду восстановления (реальное время)
	sparkWard  = 1.5 // на сколько разряд отпугивает/прореживает опасного зверя
)

func init() {
	core.Register("player", func() core.Component { return &PlayerC{Last: map[string]string{}} })
}

// PlayerC — интерактивное состояние игрока: мана врождённого «искрения» (демо-оружие) и память
// обработанных reqid по каждому действию (идемпотентность при ретрае).
type PlayerC struct {
	Mana float64           `json:"mana"`
	Last map[string]string `json:"last"`
}

func (c *PlayerC) Kind() string { return "player" }

func Player(e *core.Entity) *PlayerC { c, _ := e.Get("player").(*PlayerC); return c }

// EnsurePlayer вешает интерактивное состояние игрока (полная мана), если его ещё нет.
func EnsurePlayer(w *core.World, e *core.Entity) *PlayerC {
	if p := Player(e); p != nil {
		if p.Last == nil {
			p.Last = map[string]string{}
		}
		return p
	}
	p := &PlayerC{Mana: sparkManaMax, Last: map[string]string{}}
	w.Attach(e, p)
	return p
}

// RegenMana восстанавливает ману игроков в РЕАЛЬНОМ времени (вызывается из tick-цикла с dt секунд).
func RegenMana(w *core.World, dt float64) {
	if w == nil || dt <= 0 {
		return
	}
	w.Each("player", func(e *core.Entity) {
		p := Player(e)
		if p.Mana < sparkManaMax {
			p.Mana = math.Min(sparkManaMax, p.Mana+sparkRegen*dt)
		}
	})
}

// dedup проверяет повтор reqid: true — уже обработан (идемпотентно вернуть успех без повтора).
func dedup(p *PlayerC, action, reqid string) bool {
	if reqid != "" && p.Last[action] == reqid {
		return true
	}
	return false
}

func addStash(w *core.World, e *core.Entity, good string, n float64) {
	s := econ.Stash(e)
	if s == nil {
		s = &econ.StashC{Goods: map[string]float64{}}
		w.Attach(e, s)
	}
	if s.Goods == nil {
		s.Goods = map[string]float64{}
	}
	s.Goods[good] += n
}

// ChopWood — срубить дерево у леса: нужны силы и близость к опушке; даёт бревно в закрома.
func ChopWood(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if dedup(pc, "chop", reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil {
		return false, "нет тела в сцене"
	}
	wx, wy := poiByKind("woods")
	if !inWoods(p.X, p.Y) && math.Hypot(p.X-wx, p.Y-wy) > actReach {
		return false, "надо подойти к лесу"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < chopEnergy {
		return false, "нет сил рубить"
	}
	v.Energy -= chopEnergy
	addStash(w, e, econ.GoodWood, chopYield)
	pc.Last["chop"] = reqid
	return true, ""
}

// FishCatch — закинуть снасть у реки: нужны силы и берег; даёт рыбу в закрома.
func FishCatch(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if dedup(pc, "fish", reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil {
		return false, "нет тела в сцене"
	}
	rx, ry := poiByKind("river")
	if !nearRiver(p.X, p.Y) && math.Hypot(p.X-rx, p.Y-ry) > actReach {
		return false, "надо встать у реки"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < fishEnergy {
		return false, "нет сил на лов"
	}
	v.Energy -= fishEnergy
	addStash(w, e, econ.GoodFish, fishYield)
	pc.Last["fish"] = reqid
	return true, ""
}

// CastSpark — магический разряд (демо-оружие): тратит ману; если рядом в угодьях есть опасный зверь —
// отпугивает/прореживает его (убыль Pop). Нет зверя — просто искра (успех без эффекта).
func CastSpark(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if dedup(pc, "cast", reqid) {
		return true, ""
	}
	if pc.Mana < sparkCost {
		return false, "не хватает маны"
	}
	pc.Mana -= sparkCost
	pc.Last["cast"] = reqid
	// бьёт по опасному зверю в угодьях поселения (если есть)
	if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
		if se := w.Get(pl.Settlement); se != nil {
			if f := battle.Fauna(se); f != nil {
				for _, sp := range []string{"wolf", "bear", "boar"} {
					if f.Pop[sp] >= 1 {
						f.Pop[sp] = math.Max(0, f.Pop[sp]-sparkWard)
						break
					}
				}
			}
		}
	}
	return true, ""
}
