package sim

import (
	"strconv"

	"pogranichye/arcane"
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
	"pogranichye/mind"
	"pogranichye/worldmut"
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
			id := core.Ident(e)
			if id != nil && v.Age < 15 {
				// ребёнка кормит семья/община из общих запасов — без платы и без долга (дети
				// на иждивении: едят, но ещё не работают). Нет еды в селе — голодают, как и все.
				if s := settlementOf(w, e); s != nil && econ.Eat(s) {
					v.Hunger = clamp(v.Hunger-45, 0, 100)
				}
			} else if econ.EatFromStash(e) {
				// взрослый ест сперва из СВОИХ закромов; пусто — покупает на рынке (нет монет — в долг)
				v.Hunger = clamp(v.Hunger-45, 0, 100)
			} else if s := settlementOf(w, e); s != nil && econ.BuyMeal(s, e) {
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
	res := map[core.ID]localResources{}
	season := clock.Season()
	w.Each("settlement", func(se *core.Entity) {
		p := pop[se.ID]
		if p < 1 {
			p = 1
		}
		foodDays[se.ID] = econ.FeedableFood(se) / float64(p)
		res[se.ID] = settlementResources(se, season) // что реально доступно для занятий
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
		// уточняем цель до КОНКРЕТНОГО занятия по профессии/сезону/часу/доступным ресурсам
		role := ""
		if id := core.Ident(e); id != nil {
			role = id.Role
		}
		lr := localResources{}
		if pl := core.Place(e); pl != nil {
			lr = res[pl.Settlement]
		}
		act := chooseActivity(role, m.Goal, season, hour, lr)
		execGoal(w, e, m, v, clock, st, pstream, act.label)
	})
}

func execGoal(w *core.World, e *core.Entity, m *core.MindC, v *core.VitalsC,
	clock core.Clock, shared, s *dice.Stream, actLabel string) {
	m.LastAct = actLabel // по умолчанию — конкретное занятие (chooseActivity); особые исходы ниже переопределяют
	switch m.Goal {
	case mind.GoalWork:
		ident := core.Ident(e)
		sk := core.Skills(e)
		if ident != nil && sk != nil && v.Energy > 8 {
			sk.Train(ident.Role, 0.5) // труд растит навык
			// заработок: продал излишек своего ремесла на рынок, получил из котла торга
			// (дороже товар — больше плата; часть сразу гасит долг по еде). Добычу считает econ.
			if se := settlementOf(w, e); se != nil {
				econ.Wage(se, e, ident.Role)
				// кормилец уносит часть добытого домой в свои закрома (личное имущество)
				if isFoodRole(ident.Role) {
					econ.TakeHome(se, e, econ.FoodGoodOfRole(ident.Role))
				}
			}
		} else {
			m.LastAct = "выдохся" // нет сил работать — особый исход важнее занятия
		}
	case mind.GoalSocialize:
		// честно: если в корчме есть медовуха — угостился (мера списывается, настрой выше);
		// нет — просто посидел с людьми. Подпись отражает ФАКТ, а не намерение.
		drank := false
		if se := settlementOf(w, e); se != nil {
			if st := core.Settle(se).Store; st[econ.GoodMead] >= 1 {
				st[econ.GoodMead]--
				drank = true
			}
		}
		if drank {
			v.Morale = clamp(v.Morale+6, 0, 100)
			m.LastAct = "в корчме за кружкой"
		} else {
			v.Morale = clamp(v.Morale+3, 0, 100)
			m.LastAct = "коротает вечер с соседями"
		}
		if d := core.Drives(e); d != nil {
			d.Social = clamp(d.Social-12, 0, 100)
		}
	case mind.GoalRest:
	case mind.GoalEat:
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
			if id.Role == "mage" && arcane.Mage(e) == nil {
				w.Attach(e, &arcane.MageC{}) // проснулся дар — запас маны
			}
		}
		// Смерть — это прежде всего СТАРОСТЬ (оборот поколений идёт сам). Голод убивает
		// лишь в НАСТОЯЩИЙ голод (hunger>=100 — еды нет совсем), а не при лёгком недоедании:
		// иначе любой сезонный просад превращается в мор. Болезни/раны добавим отдельным слоем.
		p := 0.0
		if v.Age > 55 {
			// старость кусает мягко и ускоряется лишь к пределу (кубом) — так появляются
			// старики, а не выкашивается всё: большинство доживает, немногие живут долго
			t := (v.Age - 55) / (maxAge - 55)
			if t > 1 {
				t = 1
			}
			p += t * t * t * 0.02
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
		if v.Age >= 16 {
			econ.Upkeep(e) // дневной быт: трудящийся покрывает, незанятый влезает в долг
		}
	})
	DebugDeaths += len(dead)
	for _, id := range dead {
		w.Remove(id) // идентичность уходит из мира; хроника памяти — отдельный слой (позже)
	}

	// 2) рождения и рост поселений. Жителей по деревням берём из ИНДЕКСА (один проход),
	// а не сканируем мир на каждую деревню — иначе O(деревни × жители) на тысячах поселений.
	byHome := residentIndex(w)
	birth := hub.Stream("life", "birth")
	pairUp(w, byHome, hub.Stream("life", "pair")) // одинокие взрослые обоих полов сходятся в пары
	w.Each("settlement", func(se *core.Entity) {
		set := core.Settle(se)
		res := byHome[se.ID]
		pop := len(res)
		// Дети родятся у ПАРЫ (мужчина+женщина), а не «при двух взрослых». Считаем пары, где
		// женщина детородного возраста и оба в этом селе — только у них может родиться. Нет
		// взрослых обоих полов → нет пар → нет детей: пирамида выправляется сама, без скрипта.
		fed := econ.FeedableFood(se) > float64(pop) && pop < birthCapPop
		if fed {
			for _, r := range res {
				id := core.Ident(r)
				v := core.Vitals(r)
				if id == nil || v == nil || id.Sex != "ж" || v.Age < 16 || v.Age > 45 {
					continue // рождает женщина детородного возраста
				}
				mate := partnerAlive(w, r)
				if mate == nil {
					continue // нет супруга рядом — нет ребёнка
				}
				// сытая семья изредка прибавляется; реже в тесноте (у предела села)
				chance := 0.03 * (1 - float64(pop)/float64(birthCapPop))
				if chance > 0 && birth.Chance(chance) {
					spawnNewborn(w, birth, se.ID, r)
					DebugBirths++
					for i := 0; i < 4; i++ {
						econ.Eat(se) // прокорм младенца из реальных запасов
					}
					pop++ // в этом дне село уже пополнилось
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
	src3 := core.Settle(source)
	var reg *core.Entity
	if src3 != nil {
		reg = w.Get(src3.Region) // новая деревня — того же края, что родная
	}
	ne := NewSettlement(w, s, procToponym(s), reg)
	// встаёт НЕПОДАЛЁКУ от родной (переселенцы уходят не за тридевять земель), но часто за
	// пределы досягаемости сборщиков — так растёт вольное от податей пограничье
	if src3 != nil {
		ns := core.Settle(ne)
		ns.X = clamp(src3.X+s.Uniform(-180, 180), 0, mapSize)
		ns.Y = clamp(src3.Y+s.Uniform(-180, 180), 0, mapSize)
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

// isFoodRole — ремесло, добывающее съестное (приносит часть добытого домой в закрома).
func isFoodRole(role string) bool {
	switch role {
	case "farmer", "fisher", "hunter", "herder", "cook":
		return true
	}
	return false
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

// residentIndex — жители по поселениям за ОДИН проход по миру. Заменяет вызов residentsOf на
// каждую деревню (который давал O(деревни × жители)) — ключ к масштабу в тысячи поселений.
func residentIndex(w *core.World) map[core.ID][]*core.Entity {
	idx := map[core.ID][]*core.Entity{}
	w.Each("place", func(e *core.Entity) {
		if core.Ident(e) == nil {
			return
		}
		if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
			idx[pl.Settlement] = append(idx[pl.Settlement], e)
		}
	})
	return idx
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
	return []core.System{econ.EconomySystem{}, econ.TradeSystem{}, econ.CaravanSystem{}, NeedsSystem{}, MindSystem{}, LifeSystem{}, WarSystem{}, ClimateSystem{}, DiseaseSystem{}, BattleSystem{}, MagicSystem{}, worldmut.System{}}
}

// --- Болезни (хворь): НЕ сезонные и не из воздуха, а от РЕАЛЬНЫХ причин ---
// Мор вспыхивает лишь там, где есть настоящая почва: скученность без гигиены (большой город)
// И нехватка медицины (мало снадобья, нет знахаря). Крючки под войну (road_danger) и
// нетипичный климат заложены. Снадобье и знахарь гасят хворь. Смертность ограничена, чтобы
// мир не вымирал (болезнь прореживает скученные города, а не выкашивает всех).
var DebugPlague int

type DiseaseSystem struct{}

func (DiseaseSystem) Name() string { return "disease" }

const (
	plagueCrowdPop  = 20    // ниже столько душ скученности для вспышки нет
	medNeedPerCap   = 0.6   // сколько «медицины» нужно на душу, чтобы не вспыхнуло
	herbMedicine    = 8.0   // вклад знахаря в медицину деревни
	plagueStart     = 1.0   // стартовая интенсивность вспышки
	plagueDecay     = 0.9   // сама стихает день ото дня
	plagueLethality = 0.015 // базовый дневной риск смерти при полной хвори (мал — не мор поголовный)
	outbreakBase    = 0.015 // базовый шанс вспышки при сошедшихся причинах
)

func (DiseaseSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	idx := residentIndex(w)
	dis := hub.Stream("disease")
	var dead []core.ID
	w.Each("settlement", func(se *core.Entity) {
		set := core.Settle(se)
		res := idx[se.ID]
		pop := len(res)
		if pop == 0 {
			set.Plague = 0
			return
		}
		herb := 0
		for _, r := range res {
			if id := core.Ident(r); id != nil && id.Role == "herbalist" {
				herb++
			}
		}
		medicine := set.Store[econ.GoodRemedy] + float64(herb)*herbMedicine

		// давление хвори от реальных бед (война/климат) спадает само день ото дня
		if set.Strain > 0 {
			set.Strain *= 0.9
			if set.Strain < 0.05 {
				set.Strain = 0
			}
		}

		if set.Plague <= 0 {
			// ВОЗНИКНОВЕНИЕ — только по РЕАЛЬНЫМ причинам и только при нехватке медицины:
			// скученность большого города ИЛИ беда (война/климат, Strain). Сезон тут ни при чём.
			undermediced := medicine < float64(pop)*medNeedPerCap
			cause := 0.0
			if pop >= plagueCrowdPop {
				cause += float64(pop) / plagueCrowdPop // теснота
			}
			cause += set.Strain // раны/беженцы войны, холод/сырость/недород непогоды
			if cause > 1.0 && undermediced {
				risk := outbreakBase * cause * (1 - medicine/(float64(pop)*medNeedPerCap+1))
				if dis.Chance(risk) {
					set.Plague = plagueStart // хворь вспыхнула
				}
			}
			return
		}
		// активная хворь: лечим снадобьем (тратим), знахарь помогает, сама стихает
		treat := min(set.Store[econ.GoodRemedy], float64(pop)*0.5)
		set.Store[econ.GoodRemedy] -= treat
		set.Plague = set.Plague*plagueDecay - treat*0.02 - float64(herb)*0.05
		if set.Plague < 0.05 {
			set.Plague = 0
			return
		}
		// смертность — выше при сильной хвори и слабой медицине, но ограничена
		relief := medicine / (float64(pop)*medNeedPerCap + 1)
		if relief > 0.8 {
			relief = 0.8
		}
		deathP := set.Plague * plagueLethality * (1 - relief)
		for _, r := range res {
			v := core.Vitals(r)
			if v == nil {
				continue
			}
			if dis.Chance(deathP) {
				dead = append(dead, r.ID)
			}
		}
	})
	DebugPlague += len(dead)
	for _, id := range dead {
		w.Remove(id)
	}
}

// --- Боевой слой: риск на охоте и заживление ран (перенос геймплея на новое ядро) ---
// Охотник может нарваться на опасного зверя (кабан/волк/медведь) или разбойника: удачно —
// больше мяса и шкур на рынок, неудачно — рана по телу. Раны заживают со временем, быстрее
// в покое и с медициной (снадобье/знахарь — та же смычка, что у болезней). Запущенная
// кровопотеря без лечения может добить. Смерти ограничены — риск, а не бойня.
var DebugBattleDeaths, DebugHunts, DebugWins, DebugWounds int

const remedyPerTreat = 0.5 // снадобья уходит на одну перевязку раненого (чистые травы тратятся)

type BattleSystem struct{}

func (BattleSystem) Name() string { return "battle" }

func (BattleSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	idx := residentIndex(w)
	hs := hub.Stream("battle")

	// медицина деревни (0..~1): снадобье на складе + знахарь; плюс суточный приплод зверья
	// (угодья живут своим чередом) и оберег мага, если он здесь есть (отваживает хищников).
	med := map[core.ID]float64{}
	ward := map[core.ID]float64{}
	w.Each("settlement", func(se *core.Entity) {
		res := idx[se.ID]
		herb := 0
		for _, r := range res {
			if id := core.Ident(r); id != nil && id.Role == "herbalist" {
				herb++
			}
		}
		// присутствие знахаря само по себе помогает раненым (перевязка, уход); снадобье со
		// склада пойдёт в дело отдельно — его реально ТРАТЯТ на лечение ниже (не висит пассивно).
		m := float64(herb) * 0.45
		if m > 0.8 {
			m = 0.8
		}
		med[se.ID] = m
		ward[se.ID] = core.Settle(se).Ward // оберег ставит магия (слой ниже); пока 0
		if f := battle.Fauna(se); f != nil {
			f.Breed() // зверьё плодится к своему пределу — выбитое угодье со временем снова оживает
		}
	})

	var dead []core.ID
	w.Each("vitals", func(e *core.Entity) {
		v := core.Vitals(e)
		id := core.Ident(e)
		pl := core.Place(e)
		if v == nil || id == nil || pl == nil {
			return
		}
		medicine := med[pl.Settlement]
		se := w.Get(pl.Settlement)

		// 1) заживление уже имеющихся ран. Раненому дают чистые травы со склада (их ТРАТЯТ) —
		// это и ускоряет, и не даёт ране загноиться. Нет трав и нет знахаря — рана гниёт, мир суров.
		if b := battle.Body(e); b != nil && (len(b.Wounds) > 0 || b.Bleeding > 0 || b.Infection > 0) {
			rest := core.Home(e) != nil && core.Home(e).Status == "owned"
			care := medicine
			if se != nil {
				if st := core.Settle(se).Store; st[econ.GoodRemedy] >= remedyPerTreat {
					st[econ.GoodRemedy] -= remedyPerTreat // перевязали чистыми травами — снадобье убыло
					care = clamp(care+0.5, 0, 1)
				}
			}
			lost := b.Heal(rest, care, hs)
			v.Health = clamp(v.Health-lost, 0, 100)
			if (b.Bleeding > 0 || b.Infection > 4) && v.Health <= 2 && hs.Chance(0.2) {
				dead = append(dead, e.ID) // истёк кровью / сгорел от заражения без лечения
				return
			}
		}

		// 2) охота: встреча НЕ из воздуха. Охотник встречает зверя, только если РЕАЛЬНО вышел на
		// промысел (его разум послал работать) и в здешних угодьях этот зверь ПРАВДА водится. Кого
		// встретит — решает живое поголовье фауны (где гуще волки — вероятнее волк), а не кости из
		// ниоткуда. Выбьет — встречать станет некого. Так осторожный (бьёт там, где зверь смирный,
		// или под оберегом мага) цел, а лезущий в глухой угол места силы — рискует.
		if id.Role == "hunter" && v.Energy > 20 && se != nil {
			if mn := core.Mind(e); mn != nil && mn.Goal == mind.GoalWork {
				if f := battle.Fauna(se); f != nil {
					beast, dangerous := f.Encounter(ward[pl.Settlement], hs)
					if dangerous {
						b := battle.Body(e)
						if b == nil {
							nb := &battle.BodyC{Wounds: map[string]float64{}}
							w.Attach(e, nb)
							b = nb
						}
						skill := 0.0
						if sk := core.Skills(e); sk != nil {
							skill = sk.Level("hunter")
						}
						out := battle.Resolve(skill, 0.3+skill/150.0, beast, b, hs)
						DebugHunts++
						if out.Hurt > 0 {
							DebugWounds++
						}
						if out.Win {
							DebugWins++
							f.Take(beast, 1) // добытый зверь убывает из популяции (сохранение)
							core.Settle(se).Store[econ.GoodMeat] += out.Meat
							core.Settle(se).Store[econ.GoodHide] += out.Hide
						} else {
							f.Take(beast, 0.3) // подранок/распуган — тоже чуть убывает/уходит
						}
					}
				}
			}
		}
	})
	DebugBattleDeaths += len(dead)
	for _, idd := range dead {
		w.Remove(idd)
	}
}
