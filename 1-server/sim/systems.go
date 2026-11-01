package sim

import (
	"strconv"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
	"pogranichye/mind"
)

// Настройки жизни. Вынесены константами, чтобы разработчик крутил баланс в одном месте,
// не копаясь в логике (это и есть «под людей-разработчиков»).
const (
	yearDays      = 90   // сколько дней мира в одном «годе» возраста (быстрее реальности)
	maxAge        = 72.0 // предел жизни; к нему смертность растёт
	birthCapPop   = 26   // потолок населения деревни (чтобы не взрывалось)
	townAtPop     = 16   // деревня → город (town) при таком населении
	cityAtPop     = 24   // town → город (city)
	hungerPerHour = 0.6
	energyPerHour = 2.2
)

func sid(id core.ID) string { return strconv.FormatUint(uint64(id), 10) }

// Диагностические счётчики (для трассировки баланса). Не для прода.
var DebugBirths, DebugDeaths, DebugFamine, DebugOld int

// --- Нужды: голод, силы, здоровье, настрой. Каждый час. ---

type NeedsSystem struct{}

func (NeedsSystem) Name() string { return "needs" }

func (NeedsSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	hour := clock.Hour()
	night := hour >= 22 || hour < 6
	w.Each("vitals", func(e *core.Entity) {
		v := core.Vitals(e)
		if v == nil {
			return
		}
		m := core.Mind(e)
		resting := night || (m != nil && m.Goal == mind.GoalRest)

		v.Hunger = clamp(v.Hunger+hungerPerHour, 0, 100)
		if resting {
			v.Energy = clamp(v.Energy+3.0, 0, 100)
		} else {
			v.Energy = clamp(v.Energy-energyPerHour, 0, 100)
		}

		// поесть — только если ПРОГОЛОДАЛСЯ (иначе цель-eat держится часами и сытый
		// проедает лишнее — так деревня съедала годовой запас за пару недель)
		if m != nil && m.Goal == mind.GoalEat && v.Hunger >= 25 {
			if s := settlementOf(w, e); s != nil && econ.Eat(s) {
				v.Hunger = clamp(v.Hunger-45, 0, 100)
			}
		}

		// здоровье: гибнет от сильного голода/истощения, иначе медленно восстанавливается
		switch {
		case v.Hunger >= 95 || v.Energy <= 2:
			v.Health = clamp(v.Health-2.5, 0, 100)
		case v.Hunger < 60 && v.Energy > 30:
			v.Health = clamp(v.Health+0.6, 0, 100)
		}

		// настрой тянется к цели, заданной нуждами (сытость/силы/здоровье)
		target := 0.4*(100-v.Hunger) + 0.3*v.Energy + 0.3*v.Health
		v.Morale = clamp(v.Morale+(target-v.Morale)*0.05, 0, 100)
	})
}

// --- Разум: ставит цель (пакет mind) и исполняет её лёгкими эффектами. Каждый час. ---

type MindSystem struct{}

func (MindSystem) Name() string { return "mind" }

func (MindSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	st := hub.Stream("mind")
	hour := clock.Hour()

	// «Чувство продовольственной безопасности» деревни — на сколько дней еды на душу. Это то,
	// что житель ЗНАЕТ о своём доме и из-за чего тревожится о завтрашнем хлебе. Считаем за один
	// проход по поселениям (а не на каждого — масштабируемо).
	foodDays := map[core.ID]float64{}
	pop := map[core.ID]int{}
	w.Each("place", func(e *core.Entity) {
		if core.Ident(e) == nil {
			return
		}
		if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
			pop[pl.Settlement]++
		}
	})
	w.Each("settlement", func(se *core.Entity) {
		p := pop[se.ID]
		if p < 1 {
			p = 1
		}
		foodDays[se.ID] = econ.FeedableFood(se) / float64(p)
	})

	w.Each("mind", func(e *core.Entity) {
		m := core.Mind(e)
		v := core.Vitals(e)
		if m == nil || v == nil {
			return
		}
		pstream := hub.Stream("mind", sid(e.ID))
		if clock.TotalHours >= m.GoalUntil || m.Goal == "" {
			ctx := mind.Context{Hour: hour, Season: clock.Season()}
			if pl := core.Place(e); pl != nil {
				ctx.FoodDays = foodDays[pl.Settlement]
			}
			g, hold := mind.Decide(e, ctx, pstream)
			m.Goal = g
			m.GoalUntil = clock.TotalHours + hold
		}
		execGoal(w, e, m, v, clock, st, pstream)
	})
}

func execGoal(w *core.World, e *core.Entity, m *core.MindC, v *core.VitalsC,
	clock core.Clock, shared, s *dice.Stream) {
	switch m.Goal {
	case mind.GoalWork:
		ident := core.Ident(e)
		sk := core.Skills(e)
		if ident != nil && sk != nil && v.Energy > 8 {
			sk.Train(ident.Role, 0.5) // труд растит навык и платит; саму добычу считает econ
			if p := core.Purse(e); p != nil {
				p.Coins = round1(p.Coins + s.Uniform(0.5, 2.5))
			}
			m.LastAct = "работает (" + ident.Role + ")"
		} else {
			m.LastAct = "выдохся"
		}
	case mind.GoalSocialize:
		v.Morale = clamp(v.Morale+4, 0, 100)
		if d := core.Drives(e); d != nil {
			d.Social = clamp(d.Social-12, 0, 100)
		}
		m.LastAct = "в таверне с людьми"
	case mind.GoalRest:
		m.LastAct = "отдыхает"
	case mind.GoalEat:
		m.LastAct = "ест"
	case mind.GoalSeekHome:
		if p := core.Purse(e); p != nil {
			h := core.Home(e)
			if h != nil && h.Status == "none" && p.Coins >= 30 {
				p.Coins -= 30
				h.Status = "owned"
				if d := core.Drives(e); d != nil {
					d.Dwelling = clamp(d.Dwelling+30, 0, 100)
				}
				m.LastAct = "обзавёлся домом"
			} else {
				m.LastAct = "ищет, где осесть"
			}
		}
	case mind.GoalLeave:
		// уходит искать долю: переселяется в другое поселение (осмысленная миграция)
		relocate(w, e, s)
		m.LastAct = "ушёл искать лучшей доли"
		if d := core.Drives(e); d != nil {
			d.Ambition = 0
		}
	default:
		m.LastAct = "бродит"
	}
}

// --- Жизнь: старение, смертность, рождения, рост деревни в город. Раз в сутки. ---

type LifeSystem struct{}

func (LifeSystem) Name() string { return "life" }

func (LifeSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	mort := hub.Stream("life", "mortality")
	role := hub.Stream("life", "role")
	// 1) старение + смертность (собираем на удаление, чтобы не менять карту на ходу)
	var dead []core.ID
	w.Each("vitals", func(e *core.Entity) {
		v := core.Vitals(e)
		if v == nil {
			return
		}
		v.Age += 1.0 / float64(yearDays) // без округления: иначе дневной прирост (0.011) съедается
		// взросление: ребёнок-idler, дорастя до 15, берёт ремесло — иначе мир вырождается
		// в сплошных idler'ов (болезнь прототипа: родились безликими и такими остались)
		if id := core.Ident(e); id != nil && id.Role == "idler" && v.Age >= 15 {
			id.Role = pickRole(role) // ремесло по весам (пахарей больше — хлеб основа)
			if sk := core.Skills(e); sk != nil {
				sk.Train(id.Role, role.Uniform(2, 8)) // юношеские азы ремесла
			}
		}
		// Смерть — это прежде всего СТАРОСТЬ (оборот поколений идёт сам). Голод убивает
		// лишь в НАСТОЯЩИЙ голод (hunger>=100 — еды нет совсем), а не при лёгком недоедании:
		// иначе любой сезонный просад превращается в мор. Болезни/раны добавим отдельным слоем.
		p := 0.0
		if v.Age > 50 {
			p += (v.Age - 50) / (maxAge - 50) * 0.03 // старость: чем старше, тем вероятнее
		}
		if v.Hunger >= 100 {
			p += 0.02 // голод истощает медленно (переживают лихой сезон); катастрофу делает СОБЫТИЕ, не фон
		}
		if p > 0 && mort.Chance(p) {
			dead = append(dead, e.ID)
			if v.Hunger >= 100 {
				DebugFamine++
			} else {
				DebugOld++
			}
		}
	})
	DebugDeaths += len(dead)
	for _, id := range dead {
		w.Remove(id) // идентичность уходит из мира; хроника памяти — отдельный слой (позже)
	}

	// 2) рождения и рост поселений
	birth := hub.Stream("life", "birth")
	w.Each("settlement", func(se *core.Entity) {
		set := core.Settle(se)
		res := residentsOf(w, se.ID)
		pop := len(res)
		adults := 0
		for _, r := range res {
			if v := core.Vitals(r); v != nil && v.Age >= 16 && v.Age <= 48 {
				adults++
			}
		}
		// деревня жива и сыта → изредка рождается ребёнок (не выше потолка).
		// Сытость меряем РЕАЛЬНОЙ едой на складе: нет хлеба/рыбы — не до детей.
		if adults >= 2 && pop < birthCapPop && econ.FeedableFood(se) > float64(pop) {
			chance := 0.05 * float64(adults) / 4.0
			if birth.Chance(chance) {
				bornTo := res[birth.Intn(len(res))]
				spawnNewborn(w, birth, se.ID, bornTo)
				DebugBirths++
				for i := 0; i < 4; i++ {
					econ.Eat(se) // прокорм младенца из реальных запасов
				}
			}
		}
		// рост ступени: деревня → город по населению (цивилизация растёт сама)
		switch {
		case pop >= cityAtPop && set.Tier != "city":
			set.Tier = "city"
		case pop >= townAtPop && set.Tier == "village":
			set.Tier = "town"
		}
	})

	// основание НОВЫХ поселений: из переполненной деревни с амбициозными — переселенцы ставят
	// новую (мир растёт не только населением, но и числом мест). Ограничено потолком.
	foundNewSettlement(w, hub.Stream("life", "found"))
}

const maxSettlements = 400 // потолок числа поселений (производительность; поднимается легко)

func foundNewSettlement(w *core.World, s *dice.Stream) {
	if !s.Chance(0.05) { // редкое событие в масштабе мира
		return
	}
	nSet := 0
	w.Each("settlement", func(*core.Entity) { nSet++ })
	if nSet >= maxSettlements {
		return
	}
	// найти переполненную деревню с достатком амбициозных взрослых
	var source *core.Entity
	w.Each("settlement", func(se *core.Entity) {
		if source != nil {
			return
		}
		res := residentsOf(w, se.ID)
		if len(res) < birthCapPop-2 {
			return
		}
		amb := 0
		for _, r := range res {
			if d := core.Drives(r); d != nil && d.Ambition >= 55 {
				if v := core.Vitals(r); v != nil && v.Age >= 16 && v.Age <= 45 {
					amb++
				}
			}
		}
		if amb >= 4 {
			source = se
		}
	})
	if source == nil {
		return
	}
	// собрать ватагу переселенцев (амбициозные взрослые)
	var band []*core.Entity
	for _, r := range residentsOf(w, source.ID) {
		d := core.Drives(r)
		v := core.Vitals(r)
		if d != nil && v != nil && d.Ambition >= 55 && v.Age >= 16 && v.Age <= 45 {
			band = append(band, r)
		}
		if len(band) >= 5 {
			break
		}
	}
	if len(band) < 3 {
		return
	}
	ne := NewSettlement(w, s, procToponym(s))
	// новая деревня встаёт НЕПОДАЛЁКУ от родной (переселенцы уходят не за тридевять земель),
	// но часто за пределы досягаемости сборщиков — пограничье вольнее от податей
	if src3 := core.Settle(source); src3 != nil {
		ns := core.Settle(ne)
		ns.X = clamp(src3.X+s.Uniform(-180, 180), 0, mapSize)
		ns.Y = clamp(src3.Y+s.Uniform(-180, 180), 0, mapSize)
		ns.Region = src3.Region
	}
	for _, r := range band {
		if pl := core.Place(r); pl != nil {
			pl.Settlement = ne.ID
		}
		if h := core.Home(r); h != nil {
			h.Status = "none"
			h.Settlement = ne.ID
		}
		if d := core.Drives(r); d != nil {
			d.Ambition = 0 // доля найдена — осели
		}
	}
	// обеспечить костяк: первый — пахарь, второй — повар
	setRole(band[0], "farmer")
	if len(band) > 1 {
		setRole(band[1], "cook")
	}
	core.Settle(ne).Keeper = band[len(band)-1].ID
	core.Ident(band[len(band)-1]).Role = "tavernkeeper"
	// приписать к державе источника (если есть)
	w.Each("realm", func(re *core.Entity) {
		r := econ.Realm(re)
		if r == nil {
			return
		}
		for _, sid := range r.Territory {
			if sid == source.ID {
				r.Territory = append(r.Territory, ne.ID)
				return
			}
		}
	})
}

func spawnNewborn(w *core.World, s *dice.Stream, settlement core.ID, parent *core.Entity) {
	sex := randomSex(s)
	surname := ""
	if pi := core.Ident(parent); pi != nil {
		surname = inheritedSurname(s, pi.Surname, sex) // род продолжается, форма по полу
	}
	ident := makeIdentity(s, sex, surname, "idler")
	w.Spawn(
		ident,
		&core.VitalsC{Age: 0.1, Health: 100, Hunger: 20, Energy: 90, Morale: 70},
		&core.DrivesC{Ambition: s.Uniform(0, 20), Social: s.Uniform(0, 20), Dwelling: 0},
		&core.MindC{LastAct: "родился"},
		&core.SkillsC{Skills: map[string]float64{}},
		&core.PurseC{Coins: 0, Debts: map[string]float64{}},
		&core.HomeC{Status: "none", Settlement: settlement},
		&core.PlaceC{Settlement: settlement},
	)
}

// --- вспомогательное ---

func clamp(x, lo, hi float64) float64 {
	if x < lo {
		return lo
	}
	if x > hi {
		return hi
	}
	return x
}

func settlementOf(w *core.World, e *core.Entity) *core.Entity {
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return nil
	}
	return w.Get(pl.Settlement)
}

func residentsOf(w *core.World, settlement core.ID) []*core.Entity {
	var out []*core.Entity
	w.Each("place", func(e *core.Entity) {
		if pl := core.Place(e); pl != nil && pl.Settlement == settlement && core.Ident(e) != nil {
			out = append(out, e)
		}
	})
	return out
}

func relocate(w *core.World, e *core.Entity, s *dice.Stream) {
	var others []core.ID
	cur := core.ID(0)
	if pl := core.Place(e); pl != nil {
		cur = pl.Settlement
	}
	w.Each("settlement", func(se *core.Entity) {
		if se.ID != cur {
			others = append(others, se.ID)
		}
	})
	if len(others) == 0 {
		return
	}
	dest := others[s.Intn(len(others))]
	if pl := core.Place(e); pl != nil {
		pl.Settlement = dest
	}
	if h := core.Home(e); h != nil {
		h.Status = "none"
		h.Settlement = dest
	}
}

// Default — стандартный набор систем в фиксированном порядке. Экономика идёт ПЕРВОЙ в сутках
// (сев/жатва/добыча/переработка наполняют склады), затем нужды тратят, разум ставит цели,
// жизнь считает старение/смертность/рождения.
func Default() []core.System {
	return []core.System{econ.EconomySystem{}, econ.TradeSystem{}, NeedsSystem{}, MindSystem{}, LifeSystem{}}
}
