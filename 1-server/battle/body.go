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

// BodyC — раны по частям тела, кровопотеря и заражение. Пусто — цел. Хранится плоско (снимок).
type BodyC struct {
	Wounds    map[string]float64 `json:"wounds"`    // часть → накопленный урон
	Bleeding  float64            `json:"bleeding"`  // кровит — точит жизнь, пока не унялось/перевязали
	Infection float64            `json:"infection"` // загноение раны — точит жизнь, пока не вычистят травами/лекарем
}

func (c *BodyC) Kind() string { return "body" }

func Body(e *core.Entity) *BodyC { c, _ := e.Get("body").(*BodyC); return c }

// Mend — магическое ускорение обмена веществ (канон: лечение = разгон метаболизма): раны
// затягиваются скачком, кровь унимается, гной отступает. Нельзя отрастить утраченное из ничего —
// только ускорить то, что тело и так чинит.
func (c *BodyC) Mend(power float64) {
	if c == nil {
		return
	}
	for p := range c.Wounds {
		c.Wounds[p] -= power
		if c.Wounds[p] <= 0 {
			delete(c.Wounds, p)
		}
	}
	if c.Bleeding -= power; c.Bleeding < 0 {
		c.Bleeding = 0
	}
	if c.Infection -= power; c.Infection < 0 {
		c.Infection = 0
	}
}

// Hurt наносит урон в случайную часть тела (экспорт для внешних причин — откат магии, бой на дороге).
func (c *BodyC) Hurt(amount float64, s *dice.Stream) {
	if c == nil || amount <= 0 {
		return
	}
	c.hurt(partList[s.Intn(len(partList))], amount)
}

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

// openWound — порог, выше которого рана считается открытой: такую легко занести грязью (загноение),
// а чистые травы/снадобье не дают ей загноиться.
const openWound = 7.0

// Heal — заживление за день. medicine (0..~1) — чистые травы/снадобье И/ИЛИ лекарь рядом: они
// ускоряют заживление, останавливают кровь, НЕ дают открытой ране загноиться и вычищают уже
// начавшееся загноение. Без них открытая рана может загноиться, и заражение само точит жизнь —
// мир суров, но у того, кто запасся травами или держит знахаря, есть чем выжить. Возвращает
// потерю здоровья за день (кровь + заражение).
func (c *BodyC) Heal(rest bool, medicine float64, s *dice.Stream) float64 {
	if c == nil {
		return 0
	}
	rate := 0.6
	if rest {
		rate += 0.8
	}
	rate += medicine * 0.5 // снадобье/знахарь ускоряют затягивание
	worst := 0.0
	for p := range c.Wounds {
		c.Wounds[p] -= rate
		if c.Wounds[p] <= 0 {
			delete(c.Wounds, p)
		} else if c.Wounds[p] > worst {
			worst = c.Wounds[p]
		}
	}
	// заражение: открытая рана без чистых трав может загноиться; травы/лекарь не дают и чистят.
	if worst > openWound {
		fester := 0.18 * (1 - medicine*3) // много медицины (>0.33) — загноения не будет вовсе
		if fester > 0 && s.Chance(fester) {
			c.Infection += 2.5
		}
	}
	if c.Infection > 0 {
		c.Infection -= 0.3 + medicine*2.0 // травы/лекарь вычищают гной; сам по себе уходит еле-еле
		if c.Infection < 0 {
			c.Infection = 0
		}
	}
	lost := 0.0
	if c.Bleeding > 0 {
		lost += c.Bleeding * 0.3
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
	lost += c.Infection * 0.25 // гниющая рана точит здоровье, пока её не вычистят
	return lost
}
