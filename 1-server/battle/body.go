// Пакет battle — тело, раны и схватки (перенос боевого геймплея прототипа на новое ядро,
// но как компонент+функции, а не монолит). Принцип прежний: исход решают кости и навык,
// рана ложится на ЧАСТЬ тела и даёт последствие, лечение — время + медицина (снадобье/знахарь).
// Смерти прямо в бою нет (как в каноне) — тяжёлая рана кровит и может добить без лечения.
package battle

import (
	"pogranichye/core"
	"pogranichye/dice"
)

func init() {
	core.Register("body", func() core.Component { return &BodyC{Wounds: map[string]float64{}} })
}

// части тела и их запас прочности (последствие раны — через множители ниже).
var partMax = map[string]float64{"head": 20, "torso": 45, "arm": 25, "leg": 30}

var partList = []string{"head", "torso", "arm", "leg"}

// BodyC — раны по частям тела и кровопотеря. Пусто — цел. Хранится плоско (переживает снимок).
type BodyC struct {
	Wounds   map[string]float64 `json:"wounds"`   // часть → накопленный урон
	Bleeding float64            `json:"bleeding"` // кровит — точит жизнь, пока не унялось/перевязали
}

func (c *BodyC) Kind() string { return "body" }

func Body(e *core.Entity) *BodyC { c, _ := e.Get("body").(*BodyC); return c }

// Severity — совокупная тяжесть ран 0..1.
func (c *BodyC) Severity() float64 {
	if c == nil {
		return 0
	}
	tot, mx := 0.0, 0.0
	for p, m := range partMax {
		tot += c.Wounds[p]
		mx += m
	}
	if mx == 0 {
		return 0
	}
	return tot / mx
}

// hurt наносит урон в часть (кости выбирают куда), спилловер в кровь при тяжёлом ударе.
func (c *BodyC) hurt(part string, amount float64) {
	if c.Wounds == nil {
		c.Wounds = map[string]float64{}
	}
	m := partMax[part]
	c.Wounds[part] += amount
	if c.Wounds[part] > m { // часть добита — хлещет кровь
		c.Bleeding += (c.Wounds[part] - m) * 0.5
		c.Wounds[part] = m
	}
}

// Heal — заживление за день: раны затягиваются (быстрее в покое и со снадобьем/знахарем),
// кровь унимается (перевязка/лекарь). Возвращает потерю здоровья от кровопотери за день.
func (c *BodyC) Heal(rest bool, medicine float64, s *dice.Stream) float64 {
	if c == nil {
		return 0
	}
	rate := 0.6
	if rest {
		rate += 0.8
	}
	rate += medicine * 0.5 // снадобье/знахарь ускоряют
	for p := range c.Wounds {
		c.Wounds[p] -= rate
		if c.Wounds[p] <= 0 {
			delete(c.Wounds, p)
		}
	}
	lost := 0.0
	if c.Bleeding > 0 {
		lost = c.Bleeding * 0.3
		stop := 0.15 + medicine*0.4 // лекарь/снадобье останавливают кровь быстрее
		if s.Chance(stop) {
			c.Bleeding = 0
		} else {
			c.Bleeding *= 0.85
			if c.Bleeding < 0.5 {
				c.Bleeding = 0
			}
		}
	}
	return lost
}
