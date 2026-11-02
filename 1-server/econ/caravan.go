package econ

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Межгосударственная торговля КАРАВАНАМИ. Караван — реальный путник: выходит из столицы с
// излишком товара, идёт по тракту в столицу другой державы, где этого мало, и доходит за реальное
// время (версты/скорость). Присутствие товара в чужом краю — следствие того, что караван ТУДА
// ДОШЁЛ, а не телепорта. На дорогах лихих земель (где мана гуще — там и зверь, и разбойник)
// караван могут обобрать в пути. Дойдя, он разгружает товар — и цены в двух столицах сходятся
// (рынок сам переоценит), а излишки шкур/кожи/металла находят спрос там, где их нет.

func init() {
	core.Register("caravan", func() core.Component { return &CaravanC{} })
}

// CaravanC — караван в пути: что везёт, откуда-куда, сколько прошёл.
type CaravanC struct {
	From     core.ID `json:"from"`     // столица-отправитель
	To       core.ID `json:"to"`       // столица-получатель
	Seller   core.ID `json:"seller"`   // держава-продавец (ей идёт выручка в казну)
	Good     string  `json:"good"`     // что везёт
	Amount   float64 `json:"amount"`   // сколько мер (убывает, если ограбят)
	Dist     float64 `json:"dist"`     // длина тракта (версты)
	Progress float64 `json:"progress"` // пройдено (версты)
}

func (c *CaravanC) Kind() string { return "caravan" }

func Caravan(e *core.Entity) *CaravanC { c, _ := e.Get("caravan").(*CaravanC); return c }

const (
	caravanSpeed   = 70.0 // вёрст в день (караван медленнее вестника)
	caravanCap     = 6    // потолок караванов в пути разом (производительность/реализм)
	dispatchChance = 0.1  // шанс, что держава снарядит караван в этот день
	banditBase     = 0.03 // базовый риск ограбления в день пути
	banditLoss     = 0.35 // какую долю груза отнимают при налёте
	surplusAt      = 60.0 // столько товара в столице — уже излишек на вывоз
	deficitAt      = 15.0 // меньше столько — нужда (есть куда везти)
	shipMax        = 40.0 // не грузят больше за раз
	tradeGoodwill  = 3.0  // насколько дошедший обоз роднит державы (растит дружбу к союзу)
)

// settlementRealm строит индекс «поселение → держава-владелец» за один проход по державам.
func settlementRealm(w *core.World) map[core.ID]core.ID {
	owner := map[core.ID]core.ID{}
	w.Each("realm", func(re *core.Entity) {
		r := Realm(re)
		if r == nil {
			return
		}
		for _, sid := range r.Territory {
			owner[sid] = re.ID
		}
	})
	return owner
}

// товары межгосударственной торговли: durable изделия и сырьё, которых где-то густо, где-то пусто.
var tradeGoods = []string{GoodLeather, GoodCloth, GoodTools, GoodMetal, GoodHide, GoodLinen, GoodMead, GoodCheese}

// Диагностические счётчики торговли (для трассировки, не для прода).
var DebugCaravans, DebugArrived, DebugRobbed int

// CaravanSystem — движение караванов в пути и снаряжение новых между державами.
type CaravanSystem struct{}

func (CaravanSystem) Name() string { return "caravan" }

func (CaravanSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	s := hub.Stream("caravan")
	owner := settlementRealm(w) // поселение → его держава (для дипломатии дорог/торговли)

	// 1) двигаем караваны в пути; разбой на лихих дорогах; разгрузка по прибытии
	var arrived, lost []core.ID
	active := 0
	w.Each("caravan", func(e *core.Entity) {
		active++
		c := Caravan(e)
		from := w.Get(c.From)
		to := w.Get(c.To)
		if from == nil || to == nil || c.Amount <= 0 {
			lost = append(lost, e.ID)
			return
		}
		// лихость дороги — от маны краёв (гуще мана → злее зверь/разбойник) И от ДИПЛОМАТИИ:
		// меж союзниками дороги патрулируют (безопаснее), меж врагами — грабят охотнее.
		road := (core.Settle(from).Mana + core.Settle(to).Mana) / 2
		destRealm := owner[c.To]
		if sr := Realm(w.Get(c.Seller)); sr != nil && destRealm != 0 {
			if Allied(sr, destRealm) {
				road *= 0.4
			} else if IsRival(sr, destRealm) {
				road *= 1.6
			}
		}
		if s.Chance(banditBase * road) {
			c.Amount *= (1 - banditLoss) // обоз обобрали в пути (товар реально пропал)
			DebugRobbed++
		}
		c.Progress += caravanSpeed
		if c.Progress >= c.Dist {
			ds := core.Settle(to)
			if ds.Store == nil {
				ds.Store = map[string]float64{}
			}
			ds.Store[c.Good] += c.Amount // товар дошёл и лёг на чужой рынок — цены сойдутся сами
			// получатель платит за товар из своего котла торга; выручка идёт в казну продавца
			value := c.Amount * Price(to, c.Good)
			pay := value
			if pay > ds.Coin {
				pay = ds.Coin
			}
			ds.Coin -= pay
			if re := w.Get(c.Seller); re != nil {
				if r := Realm(re); r != nil {
					r.Treasury += pay
					// дошедший обоз роднит державы: растим дружбу обеих сторон (симметрично)
					if destRealm != 0 && destRealm != c.Seller {
						r.Nudge(destRealm, tradeGoodwill)
						if dr := Realm(w.Get(destRealm)); dr != nil {
							dr.Nudge(c.Seller, tradeGoodwill)
						}
					}
				}
			}
			arrived = append(arrived, e.ID)
			DebugArrived++
		}
	})
	for _, id := range append(arrived, lost...) {
		w.Remove(id)
	}

	// 2) снаряжение новых караванов между державами (от излишка к нужде)
	if active >= caravanCap {
		return
	}
	type realmCap struct {
		realm   core.ID
		capital *core.Entity
	}
	var realms []realmCap
	w.Each("realm", func(re *core.Entity) {
		r := Realm(re)
		if r == nil || r.Capital == 0 {
			return
		}
		if capEnt := w.Get(r.Capital); capEnt != nil {
			realms = append(realms, realmCap{re.ID, capEnt})
		}
	})
	if len(realms) < 2 {
		return
	}
	for _, src := range realms {
		if active >= caravanCap || !s.Chance(dispatchChance) {
			continue
		}
		sStore := store(src.capital)
		// найти товар с наибольшим излишком у этой столицы
		bestGood, bestSurplus := "", 0.0
		for _, g := range tradeGoods {
			if sur := sStore[g] - surplusAt; sur > bestSurplus {
				bestSurplus, bestGood = sur, g
			}
		}
		if bestGood == "" {
			continue
		}
		// найти чужую столицу, где этого товара мало (нужда) и дальше всех по цене — туда и везём
		var dst *core.Entity
		for _, d := range realms {
			if d.realm == src.realm {
				continue
			}
			if store(d.capital)[bestGood] < deficitAt {
				if dst == nil || Price(d.capital, bestGood) > Price(dst, bestGood) {
					dst = d.capital
				}
			}
		}
		if dst == nil {
			continue
		}
		amount := bestSurplus * 0.5
		if amount > shipMax {
			amount = shipMax
		}
		sStore[bestGood] -= amount // товар ушёл со склада в обоз (ничего не создаётся из воздуха)
		w.Spawn(&CaravanC{
			From: src.capital.ID, To: dst.ID, Seller: src.realm,
			Good: bestGood, Amount: amount,
			Dist: dist(core.Settle(src.capital), core.Settle(dst)) + 1,
		})
		active++
		DebugCaravans++
	}
}
