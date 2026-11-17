package sim

import (
	"math"

	"pogranichye/core"
)

// Характеристики (v1.12). Канон владельца: «одна из основ игры — разнообразные характеристики, которые
// сама игра всегда даёт качать упорным — НПС или игрокам». Характеристика растёт ОТ ДЕЛА (любой труд,
// бой, плетение её тренирует), всё медленнее с ростом, без жёсткого потолка. Навык — «что умею»,
// характеристика — «каков я сам»; обе — числа одного и того же персонажа в обоих клиентах.

func init() { core.Register("stats", func() core.Component { return &StatsC{} }) }

// StatsC — характеристики тела и ума. База у всех 10.
type StatsC struct {
	S map[string]float64 `json:"s"`
}

func (c *StatsC) Kind() string { return "stats" }

// StatNames — порядок показа: сила, ловкость, выносливость, ум, восприятие, воля.
var StatNames = []string{"strength", "agility", "endurance", "intellect", "perception", "will"}

const statBase = 10.0

func statsOf(e *core.Entity) *StatsC { c, _ := e.Get("stats").(*StatsC); return c }

// Stat — значение характеристики (10, если ещё не тренировалась).
func Stat(e *core.Entity, name string) float64 {
	if c := statsOf(e); c != nil {
		if v, ok := c.S[name]; ok {
			return v
		}
	}
	return statBase
}

// statMod — множитель от характеристики: 10 → 1.0, каждые +10 дают +per.
func statMod(e *core.Entity, name string, per float64) float64 {
	return 1 + (Stat(e, name)-statBase)/10*per
}

// TrainStat растит характеристику от дела с насыщением (чем выше, тем медленнее; потолка нет).
func TrainStat(w *core.World, e *core.Entity, name string, amt float64) {
	c := statsOf(e)
	if c == nil {
		c = &StatsC{S: map[string]float64{}}
		w.Attach(e, c)
	}
	if c.S == nil {
		c.S = map[string]float64{}
	}
	cur, ok := c.S[name]
	if !ok {
		cur = statBase
	}
	c.S[name] = cur + amt/(1+math.Pow((cur-statBase)/8, 2))
}

// какое дело что тренирует (навык → характеристики)
var skillStats = map[string][]string{
	"woodcutter": {"strength", "endurance"}, "miner": {"strength", "endurance"}, "smith": {"strength", "will"},
	"blade": {"strength", "agility"}, "brawl": {"strength", "agility"}, "archery": {"perception", "agility"},
	"hunter": {"perception", "endurance"}, "fisher": {"perception", "will"}, "herbalist": {"perception", "intellect"},
	"cunning": {"agility", "perception"}, "mage": {"intellect", "will"}, "cook": {"intellect"},
	"crafter": {"agility", "intellect"}, "farmer": {"endurance", "strength"}, "herder": {"endurance"},
	"weaver": {"agility"}, "builder": {"strength", "endurance"},
}

// trainStatsFor — труд по навыку заодно растит связанные характеристики.
func trainStatsFor(w *core.World, e *core.Entity, skill string, amt float64) {
	for _, st := range skillStats[skill] {
		TrainStat(w, e, st, amt*0.12)
	}
}

// StatsView — характеристики для кадра (me.stats): все шесть, округлённо.
func StatsView(e *core.Entity) map[string]float64 {
	out := make(map[string]float64, len(StatNames))
	for _, n := range StatNames {
		out[n] = math.Round(Stat(e, n)*10) / 10
	}
	return out
}
