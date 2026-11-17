package sim

import (
	"math"
	"sort"

	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Хозяйство большого города (v1.33). Город не пашет и не ловит: он берёт налог с горожан, на эти
// деньги и на выручку ремесла снаряжает караваны — еда и сырьё едут к нему из деревень, а ремесло
// (орудия, сукно, кожа) — из него в деревни. Караваны — настоящие обозы мира (econ.CaravanC): их
// видно на карте и на тракте, их могут ограбить, они приходят через дни, а не мгновенно.

const (
	cityTaxRate    = 0.03 // доля монет горожанина в день — в городской котёл
	cityFoodDays   = 6.0  // запас еды, ниже которого город шлёт за ней обозы (дней на душу)
	cityRawFloor   = 25.0 // меньше столько сырья ремеслу — шлют за сырьём
	cityShipMax    = 40.0
	cityTradeReach = 160.0 // ед. карты: деревни, с которыми торгует город
	farImportMark  = 2.0   // привоз «издалека», когда рядом нечего купить: дороже вдвое
)

var cityRaw = []string{econ.GoodOre, econ.GoodWool, econ.GoodFlax, econ.GoodHide, econ.GoodWood}
var cityExports = []string{econ.GoodTools, econ.GoodCloth, econ.GoodLeather, econ.GoodRemedy}

// CityTradeSystem — раз в сутки (утром) налоги и караваны больших городов.
type CityTradeSystem struct{}

func (CityTradeSystem) Name() string { return "citytrade" }

func (CityTradeSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if clock.Hour() != 8 {
		return
	}
	RunCityTrade(w, int64(clock.Day()))
}

func foodStock(st map[string]float64) float64 {
	n := 0.0
	for g, v := range st {
		if econ.Edible(g) {
			n += v
		}
	}
	return n
}

// RunCityTrade — один день хозяйства всех больших городов. Возвращает, сколько обозов снаряжено.
func RunCityTrade(w *core.World, day int64) int {
	idx := residentIndex(w)
	owner := map[core.ID]core.ID{}
	w.Each("realm", func(re *core.Entity) {
		for _, sid := range econ.Realm(re).Territory {
			owner[sid] = re.ID
		}
	})
	var cities []*core.Entity
	w.Each("metropolis", func(se *core.Entity) { cities = append(cities, se) })
	sort.Slice(cities, func(i, j int) bool { return cities[i].ID < cities[j].ID })
	sent := 0
	for _, city := range cities {
		cs := core.Settle(city)
		if cs.Store == nil {
			cs.Store = map[string]float64{}
		}
		pop := float64(len(idx[city.ID]))
		// 1) налог: горожане отдают долю монет в городской котёл
		for _, r := range idx[city.ID] {
			if pu := core.Purse(r); pu != nil && pu.Coins > 5 && Player(r) == nil {
				t := pu.Coins * cityTaxRate
				pu.Coins -= t
				cs.Coin += t
			}
		}
		// 2) еда: мало — за ней едут обозы из деревень с излишком
		if need := pop*cityFoodDays - foodStock(cs.Store) - incoming(w, city.ID, ""); need > 0 {
			sent += importToCity(w, city, owner, need, true, day)
		}
		// 3) сырьё ремеслу
		for _, g := range cityRaw {
			if need := cityRawFloor - cs.Store[g] - incoming(w, city.ID, g); need > 0 {
				sent += importGood(w, city, owner, g, need, day)
			}
		}
		// 4) ремесло — в деревни, где его не хватает
		for _, g := range cityExports {
			if cs.Store[g] < 6 {
				continue
			}
			if v := villageInNeed(w, city, idx, g); v != nil {
				amt := math.Min(cityShipMax/2, cs.Store[g]-5)
				cs.Store[g] -= amt
				spawnCaravan(w, city, v, owner[city.ID], g, amt)
				sent++
			}
		}
	}
	return sent
}

// incoming — сколько этого добра уже едет в город (good=="" — любой еды).
func incoming(w *core.World, to core.ID, good string) float64 {
	n := 0.0
	w.Each("caravan", func(e *core.Entity) {
		if c := econ.Caravan(e); c != nil && c.To == to && ((good == "" && econ.Edible(c.Good)) || c.Good == good) {
			n += c.Amount
		}
	})
	return n
}

func spawnCaravan(w *core.World, from, to *core.Entity, seller core.ID, good string, amt float64) {
	fs, ts := core.Settle(from), core.Settle(to)
	d := math.Max(1, math.Hypot(fs.X-ts.X, fs.Y-ts.Y))
	w.Spawn(&econ.CaravanC{From: from.ID, To: to.ID, Seller: seller, Good: good, Amount: math.Round(amt*10) / 10, Dist: d})
}

// importToCity — еда: из ближних деревень с излишком (несколько обозов), иначе привоз издалека.
func importToCity(w *core.World, city *core.Entity, owner map[core.ID]core.ID, need float64, food bool, day int64) int {
	cs := core.Settle(city)
	sent := 0
	for _, v := range villagesByDistance(w, city) {
		if need <= 0 || sent >= 4 {
			break
		}
		vs := core.Settle(v)
		vpop := float64(len(residentsOf(w, v.ID)))
		best, bn := "", 0.0
		for _, g := range []string{econ.GoodWheat, econ.GoodBread, econ.GoodMeat, econ.GoodCheese, econ.GoodVeg, econ.GoodFish, econ.GoodMilk} {
			if spare := vs.Store[g] - vpop*10; spare > bn {
				best, bn = g, spare
			}
		}
		if best == "" || bn < 5 {
			continue
		}
		amt := math.Min(math.Min(bn, cityShipMax), need)
		vs.Store[best] -= amt
		spawnCaravan(w, v, city, owner[v.ID], best, amt)
		need -= amt
		sent++
	}
	if need > 0 && sent == 0 { // рядом ничего — привоз издалека, дорого, но город не вымрет
		cost := need * econ.Price(city, econ.GoodWheat) * farImportMark
		if pay := math.Min(cost, cs.Coin); pay > 0 {
			got := need * pay / cost
			cs.Coin -= pay
			cs.Store[econ.GoodWheat] += got
			if day%5 == 0 {
				chronicle.Record(w, day, "торговля", cs.Name+" закупил зерно у дальних купцов — ближние деревни не дали излишка")
			}
		}
	}
	return sent
}

func importGood(w *core.World, city *core.Entity, owner map[core.ID]core.ID, good string, need float64, day int64) int {
	cs := core.Settle(city)
	for _, v := range villagesByDistance(w, city) {
		vs := core.Settle(v)
		if spare := vs.Store[good] - 15; spare >= 5 {
			amt := math.Min(math.Min(spare, cityShipMax), need)
			vs.Store[good] -= amt
			spawnCaravan(w, v, city, owner[v.ID], good, amt)
			return 1
		}
	}
	cost := need * econ.Price(city, good) * farImportMark
	if pay := math.Min(cost, cs.Coin*0.2); pay > 0 {
		cs.Coin -= pay
		cs.Store[good] += need * pay / cost
	}
	return 0
}

func villagesByDistance(w *core.World, city *core.Entity) []*core.Entity {
	cs := core.Settle(city)
	var out []*core.Entity
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if se.ID == city.ID || IsMetropolis(se) || s == nil {
			return
		}
		if math.Hypot(s.X-cs.X, s.Y-cs.Y) <= cityTradeReach {
			out = append(out, se)
		}
	})
	sort.Slice(out, func(i, j int) bool {
		a, b := core.Settle(out[i]), core.Settle(out[j])
		da, db := math.Hypot(a.X-cs.X, a.Y-cs.Y), math.Hypot(b.X-cs.X, b.Y-cs.Y)
		if da != db {
			return da < db
		}
		return out[i].ID < out[j].ID
	})
	return out
}

func villageInNeed(w *core.World, city *core.Entity, idx map[core.ID][]*core.Entity, good string) *core.Entity {
	for _, v := range villagesByDistance(w, city) {
		vs := core.Settle(v)
		if len(idx[v.ID]) > 0 && vs.Store[good] < 3 && incoming(w, v.ID, good) == 0 {
			return v
		}
	}
	return nil
}
