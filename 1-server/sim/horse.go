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

// RiderC — какую из своих лошадей всадник сейчас ведёт (выбранная) и в седле ли он. Своих лошадей
// может быть несколько (v1.32) — хозяин записан в самой лошади (HorseC.Owner).
type RiderC struct {
	Horse   core.ID `json:"horse"`
	Mounted bool    `json:"mounted"`
}

func (c *RiderC) Kind() string { return "rider" }

const (
	HorsePrice   = 40.0
	MaxHorses    = 5 // своих лошадей у одного хозяина, не больше (v1.32)
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
func BagCap(e *core.Entity) float64 { return 20 + Stat(e, "strength")*2 + carryBonus(e) }

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
		} else {
			p.Mult = 1 + paceBonus(e) // добрая обувь — шаг шире
		}
	})
	w.Each("horse", func(he *core.Entity) { // отдых в стойле
		h := he.Get("horse").(*HorseC)
		h.Stam = math.Min(100, h.Stam+dt*0.4)
	})
}

// ownedHorses — все лошади хозяина, по id.
func ownedHorses(w *core.World, e *core.Entity) []*core.Entity {
	var out []*core.Entity
	w.Each("horse", func(he *core.Entity) {
		if h, _ := he.Get("horse").(*HorseC); h != nil && h.Owner == e.ID {
			out = append(out, he)
		}
	})
	return out
}

// selectHorse — сделать лошадь ведомой (в седле с другой не пересаживаются).
func selectHorse(w *core.World, e *core.Entity, he *core.Entity) {
	if r := riderOf(e); r != nil {
		r.Horse, r.Mounted = he.ID, false
		return
	}
	w.Attach(e, &RiderC{Horse: he.ID})
}

// Horse — намерение op:"horse": buy | choose | mount | dismount | stow | unstow | feed.
// target — id лошади для choose (и для любого действия: сначала выбрать её).
func Horse(w *core.World, e *core.Entity, action, good string, qty int, reqid string, target ...core.ID) (bool, string) {
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
	if len(target) > 0 && target[0] != 0 { // v1.32: действие над выбранной своей лошадью
		th := w.Get(target[0])
		if hc, _ := th.Get("horse").(*HorseC); th == nil || hc == nil || hc.Owner != e.ID {
			return false, "это не твоя лошадь"
		}
		if Mounted(e) && riderOf(e).Horse != th.ID {
			return false, "сначала спешься"
		}
		selectHorse(w, e, th)
	} else if action == "mount" && se != nil { // ведомая осталась в другом селе — садимся на ту, что здесь
		if _, cur := horseOf(w, e); cur == nil || cur.Settlement != se.ID {
			for _, oh := range ownedHorses(w, e) {
				if hc := oh.Get("horse").(*HorseC); hc.Settlement == se.ID {
					selectHorse(w, e, oh)
					break
				}
			}
		}
	}
	he, h := horseOf(w, e)
	ok := func() (bool, string) { pc.markDone(reqid); return true, "" }
	here := h != nil && se != nil && h.Settlement == se.ID
	switch action {
	case "choose":
		if h == nil {
			return false, "выбери, какую из своих лошадей вести"
		}
		return ok()
	case "buy":
		if len(ownedHorses(w, e)) >= MaxHorses {
			return false, "больше лошадей тебе не прокормить"
		}
		if Mounted(e) {
			return false, "сначала спешься"
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
		first := h == nil
		if first || h.Settlement != se.ID {
			selectHorse(w, e, nh) // новая — ведомая, если своей рядом нет
		}
		if first {
			say(w, e, "loot", "Теперь у тебя есть лошадь.")
		} else {
			say(w, e, "loot", "В твоём табуне прибыло: "+core.Settle(se).Name+" — ещё одна лошадь.")
		}
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

// horseEntry — лошадь для кадра: где она стоит (село и держава) и в каком она состоянии.
func horseEntry(w *core.World, e, he *core.Entity, h *HorseC) map[string]any {
	bag := map[string]float64{}
	load := 0.0
	for g, v := range h.Bag {
		if v >= 1 {
			bag[g] = math.Floor(v)
			load += v
		}
	}
	pl := core.Place(e)
	r := riderOf(e)
	active := r != nil && r.Horse == he.ID
	return map[string]any{"id": eidStr(he.ID), "name": h.Name, "stam": math.Round(h.Stam), "mounted": active && Mounted(e),
		"active": active, "here": pl != nil && h.Settlement == pl.Settlement, "bag": bag, "bag_load": math.Round(load), "bag_cap": saddleCap,
		// v1.32: где лошадь осталась — село и держава ("" — вольная земля)
		"settlement_id": eidStr(h.Settlement), "settlement_name": settleName(w, h.Settlement), "realm_name": realmNameOf(w, h.Settlement)}
}

// HorseView — ведомая лошадь (me.horse — null, если лошадей нет). Если ведомой нет, а свои есть —
// показываем первую.
func HorseView(w *core.World, e *core.Entity) any {
	he, h := horseOf(w, e)
	if h == nil {
		owned := ownedHorses(w, e)
		if len(owned) == 0 {
			return nil
		}
		he = owned[0]
		h = he.Get("horse").(*HorseC)
	}
	return horseEntry(w, e, he, h)
}

// HorsesView — me.horses: все свои лошади (v1.32), где каждая стоит; [] — лошадей нет.
func HorsesView(w *core.World, e *core.Entity) []map[string]any {
	out := []map[string]any{}
	for _, he := range ownedHorses(w, e) {
		out = append(out, horseEntry(w, e, he, he.Get("horse").(*HorseC)))
	}
	return out
}

func BagView(e *core.Entity) map[string]any {
	pack := ""
	if inv := Inv(e); inv != nil {
		pack = inv.Worn["back"]
	}
	// pack — что надето на спину (заплечный мешок, сума) и сколько оно прибавляет; "" — ничего (v1.22)
	return map[string]any{"load": math.Round(BagLoad(e)), "cap": math.Round(BagCap(e)), "over": BagLoad(e) > BagCap(e),
		"pack": pack, "pack_bonus": math.Round(carryBonus(e))}
}

// StableView — конюшня села для кадра (place.stable): сколько лошадей и почём.
func StableView(se *core.Entity) map[string]any {
	s := core.Settle(se)
	return map[string]any{"horses": stableStock(s), "price": HorsePrice}
}

// FixHorseGoods — починка старых сохранений. До этой правки рынок по ошибке показывал счётчик
// лошадей конюшни (Store["horse"]) как обычный товар: «лошадь» можно было купить за монету, и она
// ложилась в суму вещью, а настоящей лошади (HorseC) не появлялось. Здесь такие «вещи» превращаются
// обратно: у игрока без лошади первая становится настоящей лошадью, остальные возвращаются в
// конюшню его села. Возвращает, сколько «вещей» исправлено.
func FixHorseGoods(w *core.World) int {
	fixed := 0
	type item struct {
		e *core.Entity
		n float64
	}
	var found []item
	w.Each("stash", func(e *core.Entity) {
		if st := econ.Stash(e); st != nil && st.Goods[horseGood] > 0 {
			found = append(found, item{e, st.Goods[horseGood]})
		}
	})
	w.Each("horse", func(he *core.Entity) {
		if h, _ := he.Get("horse").(*HorseC); h != nil && h.Bag[horseGood] > 0 {
			if o := w.Get(h.Owner); o != nil {
				found = append(found, item{o, h.Bag[horseGood]})
			}
			delete(h.Bag, horseGood)
		}
	})
	for _, it := range found {
		e := it.e
		if st := econ.Stash(e); st != nil {
			delete(st.Goods, horseGood)
		}
		n := math.Floor(it.n)
		fixed += int(n)
		var home *core.Entity
		if pl := core.Place(e); pl != nil {
			home = w.Get(pl.Settlement)
		}
		if _, h := horseOf(w, e); h == nil && Player(e) != nil && n >= 1 && home != nil {
			nh := w.Spawn(&HorseC{Owner: e.ID, Settlement: home.ID, Stam: 100, Bag: map[string]float64{},
				Name: horseNames[int(uint64(e.ID)%uint64(len(horseNames)))]})
			w.Attach(e, &RiderC{Horse: nh.ID})
			say(w, e, "loot", "Купленная на рынке «лошадь» теперь настоящая — она ждёт у конюшни.")
			n--
		}
		if n > 0 && home != nil {
			if s := core.Settle(home); s != nil {
				stableStock(s)
				s.Store[horseGood] += n
			}
		}
	}
	return fixed
}
