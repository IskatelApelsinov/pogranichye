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
	chopEnergy = 8.0 // сил на одну рубку
	fishEnergy = 7.0 // сил на один заброс
	chopYield  = 1.0 // брёвен за рубку
	fishYield  = 1.0 // рыбин за удачный лов
	actReach   = 6.0 // м — насколько близко к POI/зоне надо стоять для действия

	sparkCost    = 25.0 // маны на разряд
	sparkManaMax = 100.0
	sparkRegen   = 6.0 // маны в секунду восстановления (реальное время)
	sparkWard    = 1.5 // на сколько разряд отпугивает/прореживает опасного зверя
)

func init() {
	core.Register("player", func() core.Component { return &PlayerC{} })
}

const dedupCap = 64 // сколько недавних reqid помним на игрока (ограниченная история — без роста без предела)

// PlayerC — интерактивное состояние игрока: мана врождённого «искрения» (демо-оружие) и ОГРАНИЧЕННАЯ
// история обработанных reqid (кольцо) для идемпотентности при ретрае. reqid записывается ТОЛЬКО при
// успехе действия, поэтому повтор после неудачи (отошёл/нет сил) по тому же id корректно сработает.
type PlayerC struct {
	Mana float64  `json:"mana"`
	Done []string `json:"done"` // недавние успешно обработанные reqid (кольцо ≤ dedupCap)
	// коррелируемый результат последнего действия (#40 ack): клиент сверяет по reqid
	RReqid    string `json:"-"`
	ROk       bool   `json:"-"`
	RReason   string `json:"-"`
	DrankBeer bool
	exitN     int // счётчик выходов на тракт ногами (для уникальных reqid) `json:"-"` // пил ли пиво (для вводной задачи #80)
}

// SetResult записывает результат последнего действия игрока (для ack в кадре me.result).
func SetResult(w *core.World, e *core.Entity, reqid string, ok bool, reason string) {
	if reqid == "" {
		return
	}
	pc := EnsurePlayer(w, e)
	pc.RReqid, pc.ROk, pc.RReason = reqid, ok, reason
}

func (c *PlayerC) Kind() string { return "player" }

func Player(e *core.Entity) *PlayerC { c, _ := e.Get("player").(*PlayerC); return c }

// EnsurePlayer вешает интерактивное состояние игрока (полная мана), если его ещё нет.
func EnsurePlayer(w *core.World, e *core.Entity) *PlayerC {
	if p := Player(e); p != nil {
		return p
	}
	p := &PlayerC{Mana: sparkManaMax}
	w.Attach(e, p)
	return p
}

// alreadyDone — обрабатывался ли уже этот reqid (идемпотентный повтор успешного действия).
func (p *PlayerC) alreadyDone(reqid string) bool {
	for _, r := range p.Done {
		if r == reqid {
			return true
		}
	}
	return false
}

// markDone записывает успешно обработанный reqid в кольцо (вытесняя старые сверх лимита).
func (p *PlayerC) markDone(reqid string) {
	p.Done = append(p.Done, reqid)
	if len(p.Done) > dedupCap {
		p.Done = p.Done[len(p.Done)-dedupCap:]
	}
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

// trainSkill растит умение игрока от дела (прокачка с замедлением — см. SkillsC.Train).
func trainSkill(w *core.World, e *core.Entity, name string, amt float64) {
	sk := core.Skills(e)
	if sk == nil {
		sk = &core.SkillsC{Skills: map[string]float64{}}
		w.Attach(e, sk)
	}
	sk.Train(name, amt)
	trainStatsFor(w, e, name, amt) // дело растит и характеристики
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
	if why := siteLacks(w, e, "woods"); why != "" {
		return false, why
	}
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, "" // идемпотентный повтор успешного действия
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
	trainSkill(w, e, "woodcutter", 0.6) // прокачка дровосека
	pc.markDone(reqid)                  // запись только при успехе
	return true, ""
}

// FishCatch — закинуть снасть у реки: нужны силы и берег; даёт рыбу в закрома.
func FishCatch(w *core.World, e *core.Entity, reqid string) (bool, string) {
	if why := siteLacks(w, e, "river"); why != "" {
		return false, why
	}
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
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
	trainSkill(w, e, "fisher", 0.6) // прокачка рыбака
	pc.markDone(reqid)
	return true, ""
}

// CastSpark — магический разряд (демо-оружие): тратит ману; если рядом в угодьях есть опасный зверь —
// отпугивает/прореживает его. Требует быть в поселении на открытом месте (не в стене) — проверка зоны.
func CastSpark(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil || Blocked(p.X, p.Y) {
		return false, "негде творить разряд"
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return false, "ты не в угодьях поселения"
	}
	if pc.Mana < sparkCost {
		return false, "не хватает маны"
	}
	pc.Mana -= sparkCost
	trainSkill(w, e, "mage", 0.3) // прокачка дара
	pc.markDone(reqid)
	// бьёт по опасному зверю в угодьях поселения (если есть)
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
	return true, ""
}
