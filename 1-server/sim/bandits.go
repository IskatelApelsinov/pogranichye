package sim

import (
	"math"
	"sort"

	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Разбойники — живые люди (v1.31). Раньше житель, отчаявшийся от долгов, исчезал из мира, а вместо
// него появлялся безымянный «разбойник»; стан у околицы пополнялся такими же безымянными. Теперь
// разбойник — тот же человек: с именем, возрастом, голодом и памятью, только род занятий у него
// «разбой» и он нападает. Безымянных нет: стан пополняют пришлые — тоже люди с именами.
// Ватаги занимают опустевшие сёла и ходят оттуда в набеги на ближние живые сёла.

const (
	banditLairReach = 60.0 // ед. карты (~15 км): дальше за пустым селом ватага не пойдёт (соседи — 20–150 ед.)
	banditRaidReach = 80.0 // ед. карты (~20 км): на какие сёла ходят в набег
	banditLairRoam  = 110.0
	banditCampReach = 2000.0 // м: урочище, где бездомный разбойник разбивает стан
)

// BanditLairC — метка на селе: его заняли разбойники.
type BanditLairC struct {
	Since int64 `json:"since"` // день, когда заняли
}

func (c *BanditLairC) Kind() string { return "banditlair" }

func init() { core.Register("banditlair", func() core.Component { return &BanditLairC{} }) }

func lairOf(se *core.Entity) *BanditLairC { c, _ := se.Get("banditlair").(*BanditLairC); return c }

// IsBanditPerson — человек, ставший разбойником (не зверь): у него есть и имя, и FoeC.
func IsBanditPerson(e *core.Entity) bool { return e != nil && Foe(e) != nil && core.Ident(e) != nil }

func fullName(e *core.Entity) string {
	id := core.Ident(e)
	if id == nil {
		return ""
	}
	if id.Surname != "" {
		return id.Name + " " + id.Surname
	}
	return id.Name
}

// turnBandit — человек уходит в разбой: род занятий «разбойник», он больше не житель села (без
// PlaceC — не в списках жителей, не работает, не платит), а враг в сцене земель settlement.
func turnBandit(w *core.World, e *core.Entity, settlement core.ID, x, y float64, wild string, roam float64) {
	if id := core.Ident(e); id != nil {
		id.Role = "bandit"
	}
	for _, k := range []string{"place", "scenepos", "nav", "autowalk"} {
		w.Detach(e, k)
	}
	if h := core.Home(e); h != nil {
		h.Status = "none"
	}
	if m := core.Mind(e); m != nil {
		m.Goal, m.Pinned, m.ActCode, m.LastAct = "", "", "bandit", "ушёл в разбой"
		m.RoutineGoal, m.RoutinePhase = "", ""
	}
	st := foeStats["bandit"]
	w.Attach(e, &FoeC{Species: "bandit", Settlement: settlement, X: x, Y: y, HomeX: x, HomeY: y,
		HP: st.hp, Max: st.hp, State: "roam", WX: x, WY: y, Wild: wild, Roam: roam, Name: fullName(e)})
}

// banditCampFor — где разбойник из этого села разобьёт стан: в ближних руинах или у кургана
// (если есть), иначе — в дикой зоне у околицы, как и раньше.
func banditCampFor(w *core.World, settlement core.ID, n int) (x, y float64, wild string, roam float64) {
	if s := settleOf(w, settlement); s != nil && Seamless(w) {
		ae, an := Anchor(w, s)
		for _, p := range WildPlacesNear(w, terrainFor(w), ae, an, banditCampReach) {
			if (p.Kind == "ruins" || p.Kind == "barrow") && nearestSettlementTo(w, p.E, p.N) == settlement {
				ang := float64(n) * 2.1
				return p.E - ae + math.Cos(ang)*5, p.N - an + math.Sin(ang)*5, p.ID, 220
			}
		}
	}
	x, y = foeSpot(LayoutOf(w, settlement), "bandit", settlement, n)
	return x, y, "", 0
}

// spawnBanditNewcomer — пришлый лихой человек для стана у села: с именем и судьбой, как все.
func spawnBanditNewcomer(w *core.World, settlement core.ID, x, y float64, salt uint64) *core.Entity {
	s := dice.NewStream(uint64(settlement)*2654435761 + salt*40503 + 17)
	sex := "м"
	if s.Chance(0.15) {
		sex = "ж"
	}
	ident := makeIdentity(s, sex, "", "bandit")
	ident.Origin = "пришлый"
	e := w.Spawn(ident,
		&core.VitalsC{Age: round1(s.Uniform(18, 45)), Health: s.Uniform(80, 100), Hunger: s.Uniform(10, 40), Energy: s.Uniform(60, 100), Morale: s.Uniform(30, 60)},
		&core.DrivesC{Ambition: s.Uniform(20, 70), Social: s.Uniform(0, 30)},
		&core.MindC{LastAct: "пришёл в стан"},
		&core.SkillsC{Skills: map[string]float64{"fighter": s.Uniform(5, 20)}},
		&core.PurseC{Coins: round1(s.Uniform(0, 8)), Debts: map[string]float64{}},
		&core.HomeC{Status: "none"},
	)
	turnBandit(w, e, settlement, x, y, "", 0)
	return e
}

// BanditSystem — раз в сутки (ночью): ватаги занимают опустевшие сёла, ходят в набеги, а разбитое
// логово снова становится пустым селом.
type BanditSystem struct{}

func (BanditSystem) Name() string { return "bandits" }

func (BanditSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if clock.Hour() != 2 {
		return
	}
	RunBandits(w, int64(clock.Day()))
}

// RunBandits — один ночной ход разбойников (для системы и тестов).
func RunBandits(w *core.World, day int64) {
	groups := map[core.ID][]*core.Entity{}
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); IsBanditPerson(fe) && f.State != "dead" {
			groups[f.Settlement] = append(groups[f.Settlement], fe)
		}
	})
	residents := residentIndex(w)
	// разбитые логова снова пустые сёла
	w.Each("banditlair", func(se *core.Entity) {
		if len(groups[se.ID]) == 0 {
			defer w.Detach(se, "banditlair")
			if s := core.Settle(se); s != nil {
				chronicle.Record(w, day, "разбой", "логово разбойников в селе "+s.Name+" опустело")
			}
		}
	})
	sids := make([]core.ID, 0, len(groups))
	for sid := range groups {
		sids = append(sids, sid)
	}
	sort.Slice(sids, func(i, j int) bool { return sids[i] < sids[j] })
	for _, sid := range sids {
		g := groups[sid]
		se := w.Get(sid)
		if se == nil {
			continue
		}
		// 1. ватага без логова ищет опустевшее село поблизости
		if lairOf(se) == nil && len(g) >= 2 {
			if lair := emptySettlementNear(w, se, residents); lair != nil {
				for i, b := range g {
					f := Foe(b)
					ang := float64(i) * 2.4
					f.Settlement, f.Wild, f.Roam = lair.ID, "", banditLairRoam
					f.X, f.Y = math.Cos(ang)*12, math.Sin(ang)*12
					f.HomeX, f.HomeY, f.WX, f.WY = 0, 0, f.X, f.Y
				}
				w.Attach(lair, &BanditLairC{Since: day})
				chronicle.Record(w, day, "разбой", "разбойники заняли опустевшее село "+core.Settle(lair).Name)
				sid, se = lair.ID, lair
			}
		}
		// 2. набег на ближнее живое село: еда в суму, монеты у хозяина села
		if target := raidTarget(w, se, residents); target != nil {
			ts := core.Settle(target)
			took := 0.0
			for _, b := range g {
				if good := richestEdible(ts); good != "" {
					ts.Store[good]--
					addStash(w, b, good, 1)
					took++
				}
				if k := w.Get(ts.Keeper); k != nil && core.Purse(k) != nil && core.Purse(b) != nil {
					c := math.Min(2, core.Purse(k).Coins)
					core.Purse(k).Coins -= c
					core.Purse(b).Coins += c
				}
				if m := core.Mind(b); m != nil {
					m.LastAct = "ходил в набег на " + ts.Name
				}
			}
			if took > 0 && day%3 == 0 {
				chronicle.Record(w, day, "разбой", "лихие люди обобрали "+ts.Name)
			}
		}
	}
}

func emptySettlementNear(w *core.World, from *core.Entity, residents map[core.ID][]*core.Entity) *core.Entity {
	fs := core.Settle(from)
	var best *core.Entity
	bd := banditLairReach
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil || len(residents[se.ID]) > 0 || lairOf(se) != nil || isOuter(w, s) != isOuter(w, fs) {
			return
		}
		if d := math.Hypot(s.X-fs.X, s.Y-fs.Y); d <= bd && (best == nil || d < bd || se.ID < best.ID) {
			best, bd = se, d
		}
	})
	return best
}

func raidTarget(w *core.World, from *core.Entity, residents map[core.ID][]*core.Entity) *core.Entity {
	fs := core.Settle(from)
	var best *core.Entity
	bd := banditRaidReach
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil || len(residents[se.ID]) == 0 || richestEdible(s) == "" {
			return
		}
		if d := math.Hypot(s.X-fs.X, s.Y-fs.Y); d <= bd && (best == nil || d < bd) {
			best, bd = se, d
		}
	})
	return best
}

func richestEdible(s *core.SettlementC) string {
	best, bn := "", 1.0
	for _, g := range []string{econ.GoodBread, econ.GoodMeat, econ.GoodFish, econ.GoodCheese, econ.GoodVeg, econ.GoodGame, econ.GoodWheat} {
		if s.Store[g] >= bn+1 {
			best, bn = g, s.Store[g]
		}
	}
	return best
}

// LairView — place.lair: заняли ли село разбойники и сколько их (nil — нет).
func LairView(w *core.World, settlement core.ID) any {
	se := w.Get(settlement)
	if se == nil {
		return nil
	}
	l := lairOf(se)
	if l == nil {
		return nil
	}
	n := 0
	names := []string{}
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); IsBanditPerson(fe) && f.Settlement == settlement && f.State != "dead" {
			n++
			if len(names) < 5 {
				names = append(names, f.Name)
			}
		}
	})
	return map[string]any{"since_day": l.Since, "bandits": n, "names": names}
}
