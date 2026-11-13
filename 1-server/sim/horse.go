package sim

import (
	"math"

	"pogranichye/core"
	"pogranichye/econ"
)

// Лошадь, конюшня и сума (v1.13). Лошадь — настоящая серверная сущность с хозяином: её покупают у
// конюха (лошадей в селе конечное число), на неё садятся и слезают, она устаёт и ест, у неё своя
// седельная сумка — отдельно от сумы всадника. Верхом в сцене быстрее, по тракту — тоже. Сума всадника
// имеет вместимость от силы: перегруженный идёт медленно и не бегает.

func init() {
	core.Register("horse", func() core.Component { return &HorseC{} })
	core.Register("rider", func() core.Component { return &RiderC{} })
}

// HorseC — лошадь.
type HorseC struct {
	Owner      core.ID            `json:"owner"`
	Settlement core.ID            `json:"settlement"` // где стоит, когда не под седлом
	Name       string             `json:"name"`
	Stam       float64            `json:"stam"` // 0..100 — силы лошади
	Bag        map[string]float64 `json:"bag"`  // седельная сумка
}

func (c *HorseC) Kind() string { return "horse" }

// RiderC — у кого какая лошадь и в седле ли он.
type RiderC struct {
	Horse   core.ID `json:"horse"`
	Mounted bool    `json:"mounted"`
}

func (c *RiderC) Kind() string { return "rider" }

const (
	HorsePrice   = 40.0
	horseGood    = "horse"
	saddleCap    = 40.0
	stablePOI    = "granary" // конюшня — у амбара (сено и овёс там же)
	mountedSpeed = 1.8
	loadedSpeed  = 0.6
)

var horseNames = []string{"Гнедко", "Сивка", "Буран", "Зорька", "Воронок", "Ласточка", "Рыжик", "Туман"}

func riderOf(e *core.Entity) *RiderC { c, _ := e.Get("rider").(*RiderC); return c }

func horseOf(w *core.World, e *core.Entity) (*core.Entity, *HorseC) {
	r := riderOf(e)
	if r == nil {
		return nil, nil
	}
	he := w.Get(r.Horse)
	if he == nil {
		return nil, nil
	}
	h, _ := he.Get("horse").(*HorseC)
	return he, h
}

// Mounted — в седле ли.
func Mounted(e *core.Entity) bool { r := riderOf(e); return r != nil && r.Mounted }

// stableStock — сколько лошадей продаёт конюшня села (заводится один раз: деревня — 2, город — 5).
func stableStock(s *core.SettlementC) float64 {
	if s.Store == nil {
		s.Store = map[string]float64{}
	}
	if _, ok := s.Store[horseGood]; !ok {
		s.Store[horseGood] = 2
		if s.Tier == "city" {
			s.Store[horseGood] = 5
		}
	}
	return s.Store[horseGood]
}

// BagCap — вместимость сумы от силы; BagLoad — сколько в ней сейчас (товары закромов).
func BagCap(e *core.Entity) float64 { return 20 + Stat(e, "strength")*2 }

func BagLoad(e *core.Entity) float64 {
	n := 0.0
	if st := econ.Stash(e); st != nil {
		for _, v := range st.Goods {
			n += v
		}
	}
	return n
}

// UpdateBurden — раз в шаг реального времени: всадник быстр, перегруженный пеший медлен.
func UpdateBurden(w *core.World, dt float64) {
	w.Each("player", func(e *core.Entity) {
		p := ScenePos(e)
		if p == nil {
			return
		}
		p.Mult = 1
		if Mounted(e) {
			if _, h := horseOf(w, e); h != nil && h.Stam > 5 {
				p.Mult = mountedSpeed
			}
		} else if BagLoad(e) > BagCap(e) {
			p.Mult = loadedSpeed
			TrainStat(w, e, "strength", 0.002*dt) // тяжёлая ноша растит силу
		}
	})
	w.Each("horse", func(he *core.Entity) { // отдых в стойле
		h := he.Get("horse").(*HorseC)
		h.Stam = math.Min(100, h.Stam+dt*0.4)
	})
}

// Horse — намерение op:"horse": buy | mount | dismount | stow | unstow | feed.
func Horse(w *core.World, e *core.Entity, action, good string, qty int, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) {
		return false, "в пути не до того"
	}
	se := settlementEnt(w, e)
	s := core.Settle(se)
	he, h := horseOf(w, e)
	ok := func() (bool, string) { pc.markDone(reqid); return true, "" }
	here := h != nil && se != nil && h.Settlement == se.ID
	switch action {
	case "buy":
		if h != nil {
			return false, "у тебя уже есть лошадь"
		}
		if s == nil || !atPoi(w, e, stablePOI) {
			return false, "лошадей продают у конюшни возле амбара"
		}
		if stableStock(s) < 1 {
			return false, "в конюшне нет лошадей на продажу"
		}
		pu := core.Purse(e)
		if pu == nil || pu.Coins < HorsePrice {
			return false, "не хватает монет"
		}
		pu.Coins -= HorsePrice
		s.Store[horseGood]--
		if k := w.Get(s.Keeper); k != nil && core.Purse(k) != nil {
			core.Purse(k).Coins += HorsePrice // деньги уходят селу, а не в никуда
		}
		nh := w.Spawn(&HorseC{Owner: e.ID, Settlement: se.ID, Stam: 100, Bag: map[string]float64{},
			Name: horseNames[int(hashReq(reqid)%uint64(len(horseNames)))]})
		w.Attach(e, &RiderC{Horse: nh.ID})
		say(w, e, "loot", "Теперь у тебя есть лошадь.")
		return ok()
	case "mount":
		if h == nil {
			return false, "у тебя нет лошади"
		}
		if !here {
			return false, "твоя лошадь осталась в другом селе"
		}
		if h.Stam <= 5 {
			return false, "лошадь выбилась из сил — дай ей отдохнуть"
		}
		riderOf(e).Mounted = true
		if c := Combat(e); c != nil {
			c.Sneak = false
		}
		return ok()
	case "dismount":
		if r := riderOf(e); r != nil {
			r.Mounted = false
		}
		return ok()
	case "feed":
		if h == nil || !here {
			return false, "лошади рядом нет"
		}
		st := ensureStashOf(w, e)
		for _, g := range []string{"hay", "wheat", "veg"} {
			if st.Goods[g] >= 1 {
				st.Goods[g]--
				h.Stam = math.Min(100, h.Stam+35)
				return ok()
			}
		}
		return false, "нечем кормить: нужно сено, зерно или овощи"
	case "stow", "unstow":
		if h == nil || !here {
			return false, "лошади рядом нет"
		}
		if qty < 1 {
			qty = 1
		}
		n := float64(qty)
		st := ensureStashOf(w, e)
		if h.Bag == nil {
			h.Bag = map[string]float64{}
		}
		from, to := st.Goods, h.Bag
		if action == "unstow" {
			from, to = h.Bag, st.Goods
		}
		if from[good] < n {
			return false, "столько нет"
		}
		if action == "stow" {
			load := 0.0
			for _, v := range h.Bag {
				load += v
			}
			if load+n > saddleCap {
				return false, "седельная сумка полна"
			}
		}
		from[good] -= n
		to[good] += n
		return ok()
	}
	_ = he
	return false, "нет такого действия"
}

// HorseView — лошадь и сума для кадра (me.horse — null, если лошади нет; me.bag).
func HorseView(w *core.World, e *core.Entity) any {
	he, h := horseOf(w, e)
	if h == nil {
		return nil
	}
	bag := map[string]float64{}
	load := 0.0
	for g, v := range h.Bag {
		if v >= 1 {
			bag[g] = math.Floor(v)
			load += v
		}
	}
	pl := core.Place(e)
	return map[string]any{"id": eidStr(he.ID), "name": h.Name, "stam": math.Round(h.Stam), "mounted": Mounted(e),
		"here": pl != nil && h.Settlement == pl.Settlement, "bag": bag, "bag_load": math.Round(load), "bag_cap": saddleCap}
}

// BagView — сума всадника: загрузка и вместимость.
func BagView(e *core.Entity) map[string]any {
	return map[string]any{"load": math.Round(BagLoad(e)), "cap": math.Round(BagCap(e)), "over": BagLoad(e) > BagCap(e)}
}

// StableView — конюшня села для кадра (place.stable): сколько лошадей и почём.
func StableView(se *core.Entity) map[string]any {
	s := core.Settle(se)
	return map[string]any{"horses": stableStock(s), "price": HorsePrice}
}
