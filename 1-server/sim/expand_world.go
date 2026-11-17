package sim

import (
	"fmt"
	"math"

	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Новые края мира (v1.33): три державы, у каждой — большой многоэтажный город-столица и деревни
// вокруг (всего 50 деревень). Город не пашет и не ловит рыбу: он живёт ремеслом, налогами и
// караванами (city_trade.go). Добавляется и в уже сохранённый мир — один раз (метка WorldExpandC).

const (
	expandVersion    = 1
	expandVillages   = 50
	metropolisPeople = 150
	expandSpread     = 70.0 // ед. карты: деревни державы — в таком круге от столицы
)

// MetropolisC — метка большого города: плотная многоэтажная застройка, без полей и реки.
type MetropolisC struct {
	Floors int `json:"floors"` // самый высокий дом, ярусов
}

func (c *MetropolisC) Kind() string { return "metropolis" }

// WorldExpandC — метка мира: какие новые края уже добавлены.
type WorldExpandC struct {
	Version int `json:"version"`
}

func (c *WorldExpandC) Kind() string { return "worldexpand" }

func init() {
	core.Register("metropolis", func() core.Component { return &MetropolisC{} })
	core.Register("worldexpand", func() core.Component { return &WorldExpandC{} })
}

// IsMetropolis — большой город.
func IsMetropolis(se *core.Entity) bool { return se != nil && se.Get("metropolis") != nil }

var metropolisNames = []string{"Великоград", "Китеж-на-Камне", "Святобор"}

// cityRoles — ремёсла большого города (по весу): ни пахарей, ни рыбаков.
var cityRoles = []struct {
	role string
	n    int
}{
	{"merchant", 20}, {"smith", 14}, {"weaver", 14}, {"cook", 14}, {"guard", 14}, {"carpenter", 11},
	{"tanner", 8}, {"potter", 8}, {"baker", 9}, {"scribe", 6}, {"herbalist", 5}, {"tavernkeeper", 3},
	{"jeweler", 4}, {"woodcutter", 2}, {"mage", 1},
}

// ExpandWorld — добавить новые края, если их ещё нет. Возвращает, сколько поселений добавлено.
func ExpandWorld(w *core.World, day int64) int {
	var mark *WorldExpandC
	w.Each("worldexpand", func(e *core.Entity) { mark, _ = e.Get("worldexpand").(*WorldExpandC) })
	if mark != nil && mark.Version >= expandVersion {
		return 0
	}
	g := dice.NewStream(dice.Seed64(w.Seed, "expand-world", "v1"))
	var pts [][2]float64
	w.Each("settlement", func(se *core.Entity) {
		if s := core.Settle(se); s != nil {
			pts = append(pts, [2]float64{s.X, s.Y})
		}
	})
	added := 0
	villagesLeft := expandVillages
	for i := 0; i < len(metropolisNames); i++ {
		cx, cy := emptyCorner(pts, g)
		biome := []string{"plain", "hills", "steppe"}[i]
		reg := w.Spawn(&core.RegionC{Name: metropolisNames[i] + "ская земля", Biome: biome, X: cx, Y: cy, Mana: 1.0})
		// столица
		city := NewSettlement(w, g, metropolisNames[i], reg)
		cs := core.Settle(city)
		cs.X, cs.Y = cx, cy
		w.Detach(city, "site") // место пересчитается от новых координат
		makeMetropolis(w, g, city)
		pts = append(pts, [2]float64{cx, cy})
		territory := []core.ID{city.ID}
		added++
		// деревни вокруг
		n := villagesLeft / (len(metropolisNames) - i)
		villagesLeft -= n
		for k := 0; k < n; k++ {
			ang := float64(k)/float64(n)*2*math.Pi + g.Uniform(-0.2, 0.2)
			d := g.Uniform(14, expandSpread)
			vse := NewSettlement(w, g, procToponym(g), reg)
			vs := core.Settle(vse)
			vs.X, vs.Y = clampf(cx+math.Cos(ang)*d, 0, mapSize), clampf(cy+math.Sin(ang)*d, 0, mapSize)
			nudgeApart(w, vse)
			w.Detach(vse, "site")
			populateVillage(w, g, vse)
			pts = append(pts, [2]float64{vs.X, vs.Y})
			territory = append(territory, vse.ID)
			added++
		}
		w.Spawn(&econ.RealmC{Name: realmOfCityName(metropolisNames[i]), Capital: city.ID, TaxRate: 0.1,
			Territory: territory, Treasury: 500, Relations: map[core.ID]float64{}})
		chronicle.Record(w, day, "держава", fmt.Sprintf("на карте мира — %s со столицей %s и %d деревнями", realmOfCityName(metropolisNames[i]), metropolisNames[i], n))
	}
	if mark == nil {
		w.Spawn(&WorldExpandC{Version: expandVersion})
	} else {
		mark.Version = expandVersion
	}
	return added
}

func realmOfCityName(city string) string {
	switch city {
	case "Великоград":
		return "Великоградское княжество"
	case "Китеж-на-Камне":
		return "Каменная держава"
	}
	return "Святоборская земля"
}

// emptyCorner — место на карте края, дальше всего от уже стоящих сёл (сетка 25 ед.), но не у
// самого обреза карты — там нет места для деревень вокруг.
func emptyCorner(pts [][2]float64, g *dice.Stream) (float64, float64) {
	bx, by, bd := mapSize/2, mapSize/2, -1.0
	for x := expandSpread + 10; x <= mapSize-expandSpread-10; x += 25 {
		for y := expandSpread + 10; y <= mapSize-expandSpread-10; y += 25 {
			d := math.Inf(1)
			for _, p := range pts {
				d = math.Min(d, math.Hypot(p[0]-x, p[1]-y))
			}
			if d > bd {
				bx, by, bd = x, y, d
			}
		}
	}
	return bx + g.Uniform(-5, 5), by + g.Uniform(-5, 5)
}

// nudgeApart — отодвинуть село от соседа, если земли налезают (как в NewSettlement).
func nudgeApart(w *core.World, se *core.Entity) {
	s := core.Settle(se)
	const minUnits = 2.0
	for try := 0; try < 16; try++ {
		clash := false
		w.Each("settlement", func(o *core.Entity) {
			if o.ID != se.ID && math.Hypot(core.Settle(o).X-s.X, core.Settle(o).Y-s.Y) < minUnits {
				clash = true
			}
		})
		if !clash {
			return
		}
		a := float64(try) * 2.399963
		s.X += math.Cos(a) * minUnits * (1 + float64(try)/4)
		s.Y += math.Sin(a) * minUnits * (1 + float64(try)/4)
	}
}

// populateVillage — жители деревни, как в Generate: костяк (пахари, кухарь, пастух) и прочие по весам.
func populateVillage(w *core.World, g *dice.Stream, se *core.Entity) {
	pop := g.Between(6, 11)
	var res []*core.Entity
	for j := 0; j < pop; j++ {
		p := spawnPerson(w, g, se.ID)
		switch j {
		case 0, 1:
			setRole(p, "farmer")
		case 2:
			setRole(p, "cook")
		case 3:
			setRole(p, "herder")
		}
		res = append(res, p)
	}
	k := res[g.Intn(len(res))]
	setRole(k, "tavernkeeper")
	core.Settle(se).Keeper = k.ID
}

// makeMetropolis — превратить свежее поселение в большой город: убрать угодья (полей, леса, стад
// тут нет), положить запасы на первые недели, заселить горожан всех ремёсел.
func makeMetropolis(w *core.World, g *dice.Stream, se *core.Entity) {
	s := core.Settle(se)
	s.Tier = "city"
	var drop []core.ID
	w.Each("resnode", func(ne *core.Entity) {
		if n := econ.Node(ne); n != nil && n.Settlement == se.ID {
			drop = append(drop, ne.ID)
		}
	})
	for _, id := range drop {
		w.Remove(id)
	}
	if site := SiteOf(w, se); site != nil {
		site.Terrain = "plain"
		site.Features = map[string]bool{}
		site.Specialty = "ремесло и торговля"
	}
	w.Attach(se, &MetropolisC{Floors: 5})
	s.Coin = 6000
	s.Store = map[string]float64{
		econ.GoodBread: metropolisPeople * 8, econ.GoodWheat: 400, econ.GoodMeat: 120, econ.GoodCheese: 80,
		econ.GoodOre: 80, econ.GoodMetal: 30, econ.GoodTools: 40, econ.GoodWool: 60, econ.GoodFlax: 60,
		econ.GoodCloth: 30, econ.GoodHide: 40, econ.GoodLeather: 20, econ.GoodWood: 120, econ.GoodMead: 40, BeerGood: 40,
	}
	var keeper *core.Entity
	for _, cr := range cityRoles {
		for k := 0; k < cr.n; k++ {
			p := spawnPerson(w, g, se.ID)
			setRole(p, cr.role)
			if v := core.Vitals(p); v.Age < 16 {
				v.Age = g.Uniform(17, 50)
			}
			if h := core.Home(p); h != nil { // в городе живут в доходных домах — снимают
				h.Status = "rented"
				if g.Chance(0.3) {
					h.Status = "owned"
				}
			}
			if pu := core.Purse(p); pu != nil {
				pu.Coins += g.Uniform(10, 40)
			}
			if cr.role == "tavernkeeper" && keeper == nil {
				keeper = p
			}
		}
	}
	// дети горожан
	for k := 0; k < metropolisPeople/10; k++ {
		p := spawnPerson(w, g, se.ID)
		setRole(p, "idler")
		core.Vitals(p).Age = g.Uniform(2, 13)
		if h := core.Home(p); h != nil {
			h.Status = "family"
		}
	}
	if keeper != nil {
		s.Keeper = keeper.ID
	}
}
