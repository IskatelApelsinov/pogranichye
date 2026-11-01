package econ

import "pogranichye/core"

// Личное имущество в ТОВАРАХ (не только монеты): у человека есть свои закрома — домашняя кладовая.
// Промысловик/землепашец приносит часть добытого ДОМОЙ (берёт своё с общего склада-рынка), и ест
// сперва из своих закромов; лишь когда дома пусто — покупает на рынке у чужих излишков (а нет
// монет — в долг). Так «продаёт только тот, у кого излишек»: кто наполнил кладовую, тот и выносит
// остаток на рынок, а кому не хватает — придерживает. Сохранение соблюдено: добро лишь переходит
// со склада в кладовую и обратно, ничего не творится из воздуха.

func init() {
	core.Register("stash", func() core.Component { return &StashC{Goods: map[string]float64{}} })
}

// StashC — личные закрома: сколько чего у человека лежит дома.
type StashC struct {
	Goods map[string]float64 `json:"goods"`
}

func (c *StashC) Kind() string { return "stash" }

func Stash(e *core.Entity) *StashC { c, _ := e.Get("stash").(*StashC); return c }

const (
	larderCap   = 12.0 // сколько еды человек держит дома (больше не уносит — остальное на рынок)
	takeHomeDay = 2.0  // сколько своего продукта уносит домой за рабочий день
)

func ensureStash(e *core.Entity) *StashC {
	s := Stash(e)
	if s == nil {
		s = &StashC{Goods: map[string]float64{}}
		e.Add(s) // переживает снимок (Dump обходит компоненты сущности)
	}
	if s.Goods == nil {
		s.Goods = map[string]float64{}
	}
	return s
}

func stashTotal(s *StashC) float64 {
	t := 0.0
	for _, v := range s.Goods {
		t += v
	}
	return t
}

// TakeHome — работник уносит ДОМОЙ часть своего съестного продукта с общего склада (своё добытое),
// пока кладовая не полна. Зерно домой не тащат ниже посевного резерва общины (та же бережливость).
func TakeHome(se, e *core.Entity, good string) {
	if se == nil || !Edible(good) {
		return
	}
	s := ensureStash(e)
	if stashTotal(s) >= larderCap {
		return
	}
	st := store(se)
	floor := 0.0
	if good == GoodWheat {
		floor = sowReserve // общинный сев не растаскивают по домам
	}
	avail := st[good] - floor
	if avail <= 0 {
		return
	}
	take := takeHomeDay
	if take > avail {
		take = avail
	}
	if room := larderCap - stashTotal(s); take > room {
		take = room
	}
	if take <= 0 {
		return
	}
	st[good] -= take
	s.Goods[good] += take
}

// FoodGoodOfRole — какой съестной продукт ремесло уносит домой (у неедовых ремёсел — пусто).
func FoodGoodOfRole(role string) string {
	switch role {
	case "farmer":
		return GoodWheat
	case "fisher":
		return GoodFish
	case "hunter":
		return GoodMeat
	case "herder":
		return GoodMilk
	case "cook":
		return GoodBread
	}
	return ""
}

// EatFromStash — съесть меру лучшей своей еды из закромов. true — поел из своего (рынок не нужен).
func EatFromStash(e *core.Entity) bool {
	s := Stash(e)
	if s == nil {
		return false
	}
	for _, g := range edibles {
		if s.Goods[g] >= 1 {
			s.Goods[g]--
			if s.Goods[g] < 0.01 {
				delete(s.Goods, g)
			}
			return true
		}
	}
	return false
}
