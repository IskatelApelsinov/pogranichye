package battle

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Фауна — ЖИВЫЕ популяции зверья в угодьях поселения. Это и есть «зверь реально водится тут»:
// охотник встречает кабана не по костям из ниоткуда, а потому что кабаны ПРАВДА живут в здешнем
// лесу и он пошёл туда бить дичь. Выбьют популяцию — встречать станет некого (угодье безопаснее);
// размножится — снова опасно. По канону опаснейшее зверьё кучкуется там, где мана гуще (у древних
// хранилищ) — потому плотность хищников задаёт мана места. Храним поголовьем (не отдельными
// тушами) — тот же принцип LoD: детализируем до особи лишь в активной сцене у игрока.

func init() {
	core.Register("fauna", func() core.Component { return &FaunaC{Pop: map[string]float64{}, Cap: map[string]float64{}} })
}

// виды по классу опасности: мелкая дичь безобидна, крупный зверь кусается.
var smallSpecies = []string{"hare", "fox"}
var bigSpecies = []string{"boar", "wolf", "bear"}

// FaunaC — поголовье зверья по видам в угодьях одного поселения и природные пределы по виду.
type FaunaC struct {
	Pop map[string]float64 `json:"pop"` // вид → сколько голов сейчас водится
	Cap map[string]float64 `json:"cap"` // вид → природный предел (от биома и маны места)
}

func (c *FaunaC) Kind() string { return "fauna" }

func Fauna(e *core.Entity) *FaunaC { c, _ := e.Get("fauna").(*FaunaC); return c }

// biomeFauna — во сколько раз биом богаче/беднее на данный вид (0 — вид тут не живёт вовсе).
func biomeFauna(biome, species string) float64 {
	switch species {
	case "hare":
		switch biome {
		case "steppe", "plain":
			return 1.4
		case "forest":
			return 1.0
		default:
			return 0.8
		}
	case "fox":
		return 0.9
	case "boar":
		switch biome {
		case "forest":
			return 1.3
		case "plain":
			return 0.9
		case "coast":
			return 0.5
		default:
			return 0.7
		}
	case "wolf":
		switch biome {
		case "forest":
			return 1.2
		case "hills", "steppe":
			return 1.0
		default:
			return 0.6
		}
	case "bear":
		switch biome {
		case "forest":
			return 1.3
		case "hills":
			return 1.1
		default:
			return 0.3
		}
	}
	return 1
}

// SeedFauna заселяет угодья поселения по биому и мане. Мелкая дичь — везде; крупный зверь тем
// гуще, чем выше мана (канон: опаснейшее — у мест силы). Старт — доля от предела, с разбросом.
func SeedFauna(biome string, mana float64, s *dice.Stream) *FaunaC {
	if mana <= 0 {
		mana = 1
	}
	f := &FaunaC{Pop: map[string]float64{}, Cap: map[string]float64{}}
	base := map[string]float64{"hare": 40, "fox": 16, "boar": 9, "wolf": 6, "bear": 3}
	for _, sp := range smallSpecies {
		cap := base[sp] * biomeFauna(biome, sp) * (0.8 + 0.4*mana)
		f.Cap[sp] = cap
		f.Pop[sp] = cap * s.Uniform(0.5, 1.0)
	}
	for _, sp := range bigSpecies {
		// крупный зверь сильно зависит от маны: в мёртвой зоне почти нет, у места силы — много.
		manaPull := mana * mana // медведь особенно любит густую ману
		if sp == "boar" {
			manaPull = mana
		}
		cap := base[sp] * biomeFauna(biome, sp) * manaPull
		if cap < 0 {
			cap = 0
		}
		f.Cap[sp] = cap
		f.Pop[sp] = cap * s.Uniform(0.3, 0.9)
	}
	return f
}

// Breed — суточный приплод: каждый вид логистически тянется к своему пределу. Хищники плодятся
// медленнее дичи. Так выбитое угодье со временем снова наполняется (если ещё есть кем плодиться).
func (c *FaunaC) Breed() {
	if c == nil {
		return
	}
	for sp, cap := range c.Cap {
		p := c.Pop[sp]
		if cap <= 0 {
			continue
		}
		rate := 0.04 // дичь
		if sp == "wolf" || sp == "bear" {
			rate = 0.015 // хищники плодятся медленно
		} else if sp == "boar" {
			rate = 0.03
		}
		p += rate * p * (1 - p/cap)
		if p < 0 {
			p = 0
		}
		if p > cap {
			p = cap
		}
		if p < 0.05 {
			p = 0 // вид выбит под ноль — больше некому плодиться, пока не придёт извне
		}
		c.Pop[sp] = p
	}
}

// Danger — доля опасного зверья в угодьях (0..~1): мера, стоит ли ждать беды на охоте и нужен ли оберег.
func (c *FaunaC) Danger() float64 {
	if c == nil {
		return 0
	}
	big, all := 0.0, 0.0
	for sp, p := range c.Pop {
		all += p
		for _, b := range bigSpecies {
			if sp == b {
				big += p * (1 + Beasts[b].Danger/10) // медведь весит больше зайца
			}
		}
	}
	if all <= 0 {
		return 0
	}
	return big / (all + 6)
}

// Encounter решает, НА КОГО вышел охотник в этих угодьях (не из воздуха, а из реального поголовья).
// Возвращает вид крупного зверя и true, если встреча опасная; "",false — встретил лишь мелкую дичь
// (или вообще зверя нет), то есть принёс добычу без боя. ward снижает шанс нарваться (оберег мага).
func (c *FaunaC) Encounter(ward float64, s *dice.Stream) (string, bool) {
	if c == nil {
		return "", false
	}
	// взвешенный список крупного зверя, которого РЕАЛЬНО есть кому встретить
	type w struct {
		sp  string
		pop float64
	}
	var pool []w
	bigPop := 0.0
	for _, sp := range bigSpecies {
		if p := c.Pop[sp]; p >= 1 {
			pool = append(pool, w{sp, p})
			bigPop += p
		}
	}
	if bigPop < 1 || len(pool) == 0 {
		return "", false // опасного зверья тут нет — охота безопасна
	}
	// шанс нарваться растёт с плотностью хищников и падает от оберега
	p := bigPop / (bigPop + 10)
	p *= (1 - ward)
	if p < 0 {
		p = 0
	}
	if !s.Chance(p) {
		return "", false
	}
	// на кого именно — по долям поголовья (где больше волков, вероятнее волк)
	r := s.Uniform(0, bigPop)
	for _, it := range pool {
		if r < it.pop {
			return it.sp, true
		}
		r -= it.pop
	}
	return pool[len(pool)-1].sp, true
}

// Take убавляет поголовье вида на n (добыли/распугали). Ничего не создаётся из ниоткуда — и не
// исчезает бесследно: выбитый зверь снижает популяцию, и это меняет будущие встречи.
func (c *FaunaC) Take(species string, n float64) {
	if c == nil || n <= 0 {
		return
	}
	p := c.Pop[species] - n
	if p < 0 {
		p = 0
	}
	c.Pop[species] = p
}
