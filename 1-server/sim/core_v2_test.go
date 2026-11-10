package sim

import (
	"math"
	"testing"

	"pogranichye/arcane"
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Тесты ядра v1.10: плетения, бой, просьбы, ремесло, дорога, отряд, температура областей.

// starterPlayer — стартовый мир и взрослый житель Починка как тело игрока (в сцене, с кошелём).
func starterPlayer(t *testing.T) (*core.World, *core.Entity, *core.Entity) {
	t.Helper()
	w, _ := GenerateStarter("core-v2")
	var e, se *core.Entity
	w.Each("settlement", func(s *core.Entity) {
		if se == nil && core.Settle(s).Name == "Починок" {
			se = s
		}
	})
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); e == nil && v != nil && v.Age >= 18 && core.Ident(r).Role == "farmer" {
			e = r
		}
	}
	if e == nil {
		t.Fatal("нет взрослого жителя")
	}
	EnsureScenePos(w, e)
	EnsurePlayer(w, e)
	core.Vitals(e).Health, core.Vitals(e).Energy = 100, 100
	return w, e, se
}

func TestReadWeaveGrammar(t *testing.T) {
	cases := []struct {
		nodes []string
		kind  string
	}{
		{[]string{"knot", "seal"}, "light"},
		{[]string{"knot", "barb", "seal"}, "harm"},
		{[]string{"knot", "loop", "seal"}, "ward"},
		{[]string{"knot", "loop", "thread", "seal"}, "mend"},
		{[]string{"knot", "thread", "seal"}, "push"},
	}
	for _, c := range cases {
		sp, ok := ReadWeave(c.nodes)
		if !ok || sp.Kind != c.kind || !sp.Closed {
			t.Fatalf("%v: ждали %s, получили %+v ok=%v", c.nodes, c.kind, sp, ok)
		}
	}
	if _, ok := ReadWeave([]string{"knot", "fire"}); ok {
		t.Fatal("неизвестный узел должен ломать узор")
	}
	open, _ := ReadWeave([]string{"barb", "knot"})
	closed, _ := ReadWeave([]string{"knot", "barb", "seal"})
	if open.Closed || open.Complexity <= closed.Complexity-1 {
		t.Fatal("незамкнутый узор должен быть сложнее удержать")
	}
	// нить даёт дальность, развилка — цели
	far, _ := ReadWeave([]string{"knot", "thread", "thread", "barb", "seal"})
	if far.Range <= closed.Range {
		t.Fatal("нить должна добавлять дальность")
	}
	chain, _ := ReadWeave([]string{"knot", "fork", "barb", "barb", "seal"})
	if chain.Targets != 2 {
		t.Fatal("развилка должна давать вторую цель")
	}
}

func TestWeaveCastHarmAndLearn(t *testing.T) {
	w, e, se := starterPlayer(t)
	p := ScenePos(e)
	p.X, p.Y = -100, 0
	fe := w.Spawn(&FoeC{Species: "wolf", Settlement: se.ID, X: -100, Y: 6, HP: 30, Max: 30, State: "roam"})
	w.Attach(e, &arcane.MageC{Awakened: true}) // одарённый
	trainSkill(w, e, "mage", 60)               // опытный маг — узор держится
	s := dice.NewStream(7)
	before := Player(e).Mana
	bolt := []string{"knot", "thread", "barb", "seal"}
	hits := 0
	for i := 0; i < 6 && Foe(fe).State != "dead"; i++ {
		Player(e).Mana = 100
		if ok, _ := WeaveCast(w, e, bolt, fe.ID, "w"+roman(i+1), s); ok {
			hits++
		}
	}
	if hits == 0 || Foe(fe).HP >= 30 {
		t.Fatal("плетение-стрела должно ранить волка")
	}
	if before <= 0 {
		t.Fatal("нет маны")
	}
	if _, known := grimoire(w, e).knows(bolt); !known {
		t.Fatal("удержанный незнакомый узор должен запоминаться")
	}
	// повтор reqid — не тратит ману второй раз
	Player(e).Mana = 100
	WeaveCast(w, e, []string{"knot", "seal"}, 0, "same", s)
	m := Player(e).Mana
	WeaveCast(w, e, []string{"knot", "seal"}, 0, "same", s)
	if Player(e).Mana != m {
		t.Fatal("повтор reqid не должен тратить ману")
	}
}

func TestWeaveBackfireOnOverload(t *testing.T) {
	w, e, _ := starterPlayer(t)
	s := dice.NewStream(3)
	w.Attach(e, &arcane.MageC{Awakened: true}) // дар есть, но узор не по уму
	huge := []string{"barb", "fork", "fork", "barb", "loop", "thread", "fork", "barb", "knot", "fork", "barb", "loop"}
	fails := 0
	for i := 0; i < 8; i++ {
		Player(e).Mana = 100
		core.Vitals(e).Health = 100
		if ok, _ := WeaveCast(w, e, huge, 0, "h"+roman(i+1), s); !ok {
			fails++
			if core.Vitals(e).Health >= 100 {
				t.Fatal("сорванное плетение должно бить по магу")
			}
		}
	}
	if fails < 6 {
		t.Fatalf("новичок не должен удерживать огромный незамкнутый узор (сорвалось %d из 8)", fails)
	}
}

func TestStrikeKillsFoeAndThinsFauna(t *testing.T) {
	w, e, se := starterPlayer(t)
	p := ScenePos(e)
	p.X, p.Y = -100, 0
	fa := battle.Fauna(se)
	if fa == nil {
		t.Skip("у поселения нет угодий")
	}
	fa.Pop["wolf"] = 5
	fe := w.Spawn(&FoeC{Species: "wolf", Settlement: se.ID, X: -100, Y: 1.5, HP: 30, Max: 30, State: "roam"})
	inv := ensureInv(w, e)
	inv.Items["sword"], inv.Hand = 1, "sword"
	s := dice.NewStream(11)
	for i := 0; i < 40 && Foe(fe).State != "dead"; i++ {
		Combat(e)
		ensureCombat(w, e).CD = 0
		p.Stam = 1
		Strike(w, e, StrikeInput{Target: fe.ID, Aim: "torso", Style: "slash", Aimed: true, Acc: 0.9}, s)
	}
	if Foe(fe).State != "dead" {
		t.Fatal("меч должен убить волка")
	}
	if fa.Pop["wolf"] != 4 {
		t.Fatalf("поголовье волков должно убыть: %v", fa.Pop["wolf"])
	}
	if econ.Stash(e).Goods[econ.GoodMeat] < 1 || Combat(e).Kills["wolf"] != 1 {
		t.Fatal("добыча и счёт убитых должны записаться")
	}
	// откат удара: сразу второй удар не проходит
	fe2 := w.Spawn(&FoeC{Species: "boar", Settlement: se.ID, X: -100, Y: 1.5, HP: 40, Max: 40, State: "roam"})
	ensureCombat(w, e).CD = 0
	Strike(w, e, StrikeInput{Target: fe2.ID, Aimed: true, Acc: 1}, s)
	if ok, _ := Strike(w, e, StrikeInput{Target: fe2.ID, Aimed: true, Acc: 1}, s); ok {
		t.Fatal("во время отката бить нельзя")
	}
	// прицел в сторону: сервер не верит acc, если игрок смотрит не туда
	ensureCombat(w, e).CD = 0
	p.Facing = 270 // враг на севере (+Y = 90°), смотрим на юг
	hp := Foe(fe2).HP
	Strike(w, e, StrikeInput{Target: fe2.ID, Aimed: true, Acc: 1}, s)
	if Foe(fe2).HP != hp {
		t.Fatal("удар спиной к врагу не должен попадать")
	}
}

func TestBowNeedsArrows(t *testing.T) {
	w, e, se := starterPlayer(t)
	p := ScenePos(e)
	p.X, p.Y, p.Facing = -100, 0, 90
	fe := w.Spawn(&FoeC{Species: "boar", Settlement: se.ID, X: -100, Y: 20, HP: 40, Max: 40, State: "roam"})
	inv := ensureInv(w, e)
	inv.Items["bow"], inv.Hand = 1, "bow"
	s := dice.NewStream(5)
	if ok, _ := Strike(w, e, StrikeInput{Target: fe.ID, Aimed: true, Acc: 1}, s); ok {
		t.Fatal("без стрел стрелять нельзя")
	}
	inv.Items["arrow"] = 2
	if ok, why := Strike(w, e, StrikeInput{Target: fe.ID, Aim: "head", Aimed: true, Acc: 1, Power: 1}, s); !ok {
		t.Fatalf("выстрел должен пройти: %s", why)
	}
	if inv.Items["arrow"] != 1 || Foe(fe).HP >= 40 {
		t.Fatal("стрела должна уйти и ранить")
	}
}

func TestFoeAttacksAndGuardHelps(t *testing.T) {
	w, e, se := starterPlayer(t)
	ctl := func(id core.ID) bool { return id == e.ID }
	run := func(guard bool) float64 {
		p := ScenePos(e)
		p.X, p.Y, p.Facing, p.Stam = -100, 0, 90, 1
		core.Vitals(e).Health = 100
		c := ensureCombat(w, e)
		c.Guard, c.Dodge, c.Shield = guard, 0, 0
		fe := w.Spawn(&FoeC{Species: "wolf", Settlement: se.ID, X: -100, Y: 1.2, HomeX: -100, HomeY: 1.2, HP: 30, Max: 30, State: "roam"})
		s := dice.NewStream(21)
		camp := &FoeCampC{Init: true, Timer: 1e9}
		if se.Get("foecamp") == nil {
			w.Attach(se, camp)
		} else {
			se.Get("foecamp").(*FoeCampC).Timer = 1e9
		}
		for i := 0; i < 80; i++ {
			p.Stam = 1
			StepCombat(w, 0.1, ctl, s)
		}
		w.Remove(fe.ID)
		return 100 - core.Vitals(e).Health
	}
	open := run(false)
	blocked := run(true)
	if open <= 0 {
		t.Fatal("волк рядом должен ранить игрока")
	}
	if blocked >= open {
		t.Fatalf("блок должен снижать урон: без блока %.1f, с блоком %.1f", open, blocked)
	}
}

func TestFoesSpawnFromFaunaOnlyNearPlayers(t *testing.T) {
	w, e, se := starterPlayer(t)
	fa := battle.Fauna(se)
	if fa == nil {
		t.Skip("нет угодий")
	}
	fa.Pop["wolf"], fa.Pop["hare"] = 6, 6
	s := dice.NewStream(1)
	StepCombat(w, 0.1, func(id core.ID) bool { return id == e.ID }, s)
	n := 0
	w.Each("foe", func(fe *core.Entity) {
		n++
		if f := Foe(fe); inVillageCore(f.X, f.Y) || Blocked(f.X, f.Y) {
			t.Fatalf("враг появился в селе/в стене: %+v", f)
		}
	})
	if n == 0 {
		t.Fatal("рядом с игроком должны появиться звери из поголовья")
	}
	// игрок ушёл — сцена врагов снимается
	StepCombat(w, 0.1, func(core.ID) bool { return false }, s)
	n = 0
	w.Each("foe", func(*core.Entity) { n++ })
	if n != 0 {
		t.Fatal("без игроков врагов в сцене быть не должно")
	}
}

func TestQuestFromRealNeedAndTurnIn(t *testing.T) {
	w, e, se := starterPlayer(t)
	st := core.Settle(se)
	st.Store[econ.GoodWood] = 0 // дровяник пуст — должна появиться просьба
	var q *Quest
	for _, o := range QuestOffers(w, e, 10) {
		if o.Kind == "deliver" && o.Good == "wood" {
			o := o
			q = &o
		}
	}
	if q == nil {
		t.Fatal("пустой дровяник должен рождать просьбу о дровах")
	}
	if ok, why := QuestAccept(w, e, q.ID, 10); !ok {
		t.Fatalf("принять: %s", why)
	}
	if ok, _ := QuestTurnIn(w, e, q.ID); ok {
		t.Fatal("без дров сдать нельзя")
	}
	addStash(w, e, econ.GoodWood, 5)
	keeper := w.Get(st.Keeper)
	if keeper != nil && core.Purse(keeper) != nil {
		core.Purse(keeper).Coins = 10
	}
	coins := core.Purse(e).Coins
	if ok, why := QuestTurnIn(w, e, q.ID); !ok {
		t.Fatalf("сдать: %s", why)
	}
	if st.Store[econ.GoodWood] != 4 || econ.Stash(e).Goods[econ.GoodWood] != 1 {
		t.Fatal("дрова должны лечь на склад села")
	}
	if keeper != nil && core.Purse(keeper) != nil {
		if core.Purse(e).Coins != coins+4 || core.Purse(keeper).Coins != 6 {
			t.Fatal("плата должна уйти из кошеля старосты игроку")
		}
	}
	// нужда закрыта и просьба не предлагается снова
	for _, o := range QuestOffers(w, e, 10) {
		if o.ID == q.ID {
			t.Fatal("закрытая просьба не должна предлагаться снова")
		}
	}
	// нет нужды — нет просьбы
	st.Store[econ.GoodWood] = 1000
	for _, o := range QuestOffers(w, e, 30) {
		if o.Good == "wood" {
			t.Fatal("полный дровяник — просьбы о дровах быть не должно")
		}
	}
}

func TestCraftChainOreToSword(t *testing.T) {
	w, e, _ := starterPlayer(t)
	GoTo(w, e, "mine")
	for i := 0; i < 4; i++ {
		core.Vitals(e).Energy = 100
		if ok, why := Mine(w, e, "m"+roman(i+1)); !ok {
			t.Fatalf("руда: %s", why)
		}
	}
	addStash(w, e, econ.GoodWood, 3)
	if ok, _ := Craft(w, e, "metal", "c0"); ok {
		t.Fatal("плавить можно только в кузне")
	}
	GoTo(w, e, "smithy")
	for _, step := range []string{"metal", "metal", "sword"} {
		core.Vitals(e).Energy = 100
		if ok, why := Craft(w, e, step, "c-"+step+roman(int(econ.Stash(e).Goods["metal"])+1)); !ok {
			t.Fatalf("%s: %s", step, why)
		}
	}
	if Inv(e).Items["sword"] != 1 || econ.Stash(e).Goods[econ.GoodOre] != 0 {
		t.Fatalf("цепочка руда→железо→меч не сошлась: %+v %+v", Inv(e).Items, econ.Stash(e).Goods)
	}
	if ok, _ := Equip(w, e, "sword"); !ok {
		t.Fatal("скованный меч берётся в руку")
	}
}

func TestJourneyTakesTimeAndAsksChoice(t *testing.T) {
	w, e, se := starterPlayer(t)
	var dst *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if dst == nil && o.ID != se.ID && !isOuter(w, core.Settle(o)) {
			dst = o
		}
	})
	if dst == nil {
		t.Skip("нет соседней деревни")
	}
	var outer *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if outer == nil && isOuter(w, core.Settle(o)) {
			outer = o
		}
	})
	s := dice.NewStream(9)
	if outer != nil {
		if ok, _ := JourneyStart(w, e, outer.ID, "j0", s); ok {
			t.Fatal("во внешний мир пешком нельзя — горы")
		}
	}
	if ok, why := JourneyStart(w, e, dst.ID, "j1", s); !ok {
		t.Fatalf("выйти в путь: %s", why)
	}
	if !OnRoad(e) || core.Place(e).Settlement != se.ID {
		t.Fatal("в пути игрок ещё не в новом поселении")
	}
	events := 0
	for i := 0; i < 3000 && OnRoad(e); i++ {
		StepJourneys(w, 0.1)
		if j := journeyOf(e); j != nil && j.Pending != nil {
			events++
			done := j.Done
			StepJourneys(w, 1)
			if journeyOf(e).Done != done {
				t.Fatal("дорога должна ждать решения игрока")
			}
			opt := j.Pending.Options[len(j.Pending.Options)-1]
			if ok, why := JourneyChoose(w, e, opt, s); !ok {
				t.Fatalf("выбор %s: %s", opt, why)
			}
		}
	}
	if OnRoad(e) || core.Place(e).Settlement != dst.ID {
		t.Fatal("игрок должен дойти")
	}
	if events == 0 {
		t.Fatal("в пути должна быть хотя бы одна встреча")
	}
}

func TestPartyTravelsTogether(t *testing.T) {
	w, a, se := starterPlayer(t)
	var b *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); b == nil && r.ID != a.ID && v != nil && v.Age >= 18 {
			b = r
		}
	}
	EnsureScenePos(w, b)
	EnsurePlayer(w, b)
	core.Vitals(b).Energy = 100
	ctl := func(id core.ID) bool { return id == a.ID || id == b.ID }
	if ok, why := PartyInvite(w, a, b.ID, ctl); !ok {
		t.Fatalf("позвать: %s", why)
	}
	if ok, why := PartyAccept(w, b); !ok {
		t.Fatalf("принять: %s", why)
	}
	if len(PartyMembers(w, a)) != 2 {
		t.Fatal("в отряде двое")
	}
	var dst *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if dst == nil && o.ID != se.ID && !isOuter(w, core.Settle(o)) {
			dst = o
		}
	})
	if dst == nil {
		t.Skip("нет соседней деревни")
	}
	s := dice.NewStream(4)
	if ok, _ := JourneyStart(w, b, dst.ID, "pj0", s); ok {
		t.Fatal("отряд ведёт вожак, не ведомый")
	}
	if ok, why := JourneyStart(w, a, dst.ID, "pj1", s); !ok {
		t.Fatalf("путь: %s", why)
	}
	if !OnRoad(b) {
		t.Fatal("ведомый должен выйти вместе с вожаком")
	}
	for i := 0; i < 3000 && (OnRoad(a) || OnRoad(b)); i++ {
		StepJourneys(w, 0.1)
		if j := journeyOf(a); j != nil && j.Pending != nil {
			JourneyChoose(w, a, j.Pending.Options[len(j.Pending.Options)-1], s)
		}
	}
	if core.Place(a).Settlement != dst.ID || core.Place(b).Settlement != dst.ID {
		t.Fatal("оба должны дойти")
	}
	PartyLeave(w, a)
	if len(PartyMembers(w, b)) != 1 {
		t.Fatal("вожак ушёл — отряд распался")
	}
}

func TestHeatArchivesAndMaterializes(t *testing.T) {
	w, e, se := starterPlayer(t)
	none := func(core.ID) bool { return false }
	me := func(id core.ID) bool { return id == e.ID }
	// игрок в мире: его поселение горячее, жители получают тела в сцене
	hot, _, cold := UpdateHeat(w, me)
	if hot != 1 || cold == 0 {
		t.Fatalf("ждали одно горячее и холодные дальние: hot=%d cold=%d", hot, cold)
	}
	bodies := 0
	for _, r := range residentsOf(w, se.ID) {
		if p := ScenePos(r); p != nil && r.ID != e.ID {
			bodies++
			if Blocked(p.X, p.Y) {
				t.Fatal("житель материализован в стене")
			}
		}
	}
	if bodies < 10 {
		t.Fatalf("жители горячего поселения должны получить тела: %d", bodies)
	}
	// в холодных поселениях ходьба не считается
	StepNPCMovement(w, 0.5, me)
	w.Each("settlement", func(o *core.Entity) {
		if o.ID == se.ID {
			return
		}
		for _, r := range residentsOf(w, o.ID) {
			if ScenePos(r) != nil {
				t.Fatal("в холодном поселении не должно быть тел в сцене")
			}
		}
	})
	// игрок ушёл — сцена архивируется
	UpdateHeat(w, none)
	for _, r := range residentsOf(w, se.ID) {
		if r.ID != e.ID && ScenePos(r) != nil {
			t.Fatal("после остывания тела должны быть сняты")
		}
	}
}

// Нужды холодного жителя за сутки сходятся с горячим: LoD меняет шаг, а не итог.
func TestNeedsLoDMatchesFullRate(t *testing.T) {
	mk := func(level int) float64 {
		w := core.NewWorld("lod")
		reg := w.Spawn(&core.RegionC{Name: "r"})
		se := w.Spawn(&core.SettlementC{Name: "s", Region: reg.ID, Store: map[string]float64{}})
		if level >= 0 {
			w.Attach(se, &HeatC{Level: level})
		}
		e := w.Spawn(&core.IdentC{Name: "x"}, &core.VitalsC{Hunger: 10, Energy: 60, Health: 80, Morale: 50, Age: 30},
			&core.PlaceC{Settlement: se.ID})
		clock := core.Clock{}
		for h := 0; h < 24; h++ {
			NeedsSystem{}.Hour(w, clock, dice.NewHub("lod"))
			clock.Advance()
		}
		return core.Vitals(e).Hunger
	}
	full, cold := mk(-1), mk(0)
	if math.Abs(full-cold) > 0.01 {
		t.Fatalf("голод за сутки должен совпасть: полный %.2f, холодный %.2f", full, cold)
	}
}

// Бесконечный мир: приход игрока во внешний край открывает следующий край дальше от центра; повторно
// тот же край не открывается; потолок числа поселений соблюдается; стартовые земли не растут.
func TestFrontierGrowsWherePlayersGo(t *testing.T) {
	w, e, se := starterPlayer(t)
	count := func() int { n := 0; w.Each("settlement", func(*core.Entity) { n++ }); return n }
	var outer *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if outer == nil && isOuter(w, core.Settle(o)) {
			outer = o
		}
	})
	if outer == nil {
		t.Skip("нет внешнего мира")
	}
	base := count()
	if OpenFrontier(w, e, se.ID) != "" || count() != base {
		t.Fatal("стартовые земли расти не должны")
	}
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 5, Villages: 4, Folk: 5})
	if ok, why := Travel(w, e, outer.ID); !ok {
		t.Fatalf("врата: %s", why)
	}
	if count() != base+4 {
		t.Fatalf("за краем должны открыться 4 деревни: было %d, стало %d", base, count())
	}
	r0 := core.Region(w.Get(core.Settle(outer).Region))
	far := false
	w.Each("region", func(re *core.Entity) {
		r := core.Region(re)
		if f, _ := re.Get("frontier").(*FrontierC); f != nil && f.Ring == 1 {
			far = math.Hypot(r.X-500, r.Y-500) > math.Hypot(r0.X-500, r0.Y-500)
		}
	})
	if !far {
		t.Fatal("новый край должен лежать дальше от центра")
	}
	n := count()
	OpenFrontier(w, e, outer.ID)
	if count() != n {
		t.Fatal("один край открывается один раз")
	}
	// потолок: в новом краю следующий уже не откроется (base+5 < n+4)
	var fresh core.ID
	w.Each("settlement", func(o *core.Entity) {
		if re := w.Get(core.Settle(o).Region); re != nil {
			if f, _ := re.Get("frontier").(*FrontierC); f != nil && f.Ring == 1 {
				fresh = o.ID
			}
		}
	})
	if OpenFrontier(w, e, fresh) != "" || count() != n {
		t.Fatal("потолок мира должен останавливать рост")
	}
	// у новых деревень есть жители и староста — в них можно жить и брать просьбы
	if len(residentsOf(w, fresh)) < 3 || core.Settle(w.Get(fresh)).Keeper == 0 {
		t.Fatal("новая деревня должна быть населена")
	}
}

// #40 (0057 ChatGPT, п.3): разговор начат успешно, затем собеседник ушёл за радиус или в другое
// поселение — Say закрывает разговор и НЕ даёт эффекта (ни знакомства, ни оплаты).
func TestSayAfterPartnerLeftHasNoEffect(t *testing.T) {
	w, e, se := starterPlayer(t)
	var npc *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); npc == nil && r.ID != e.ID && v != nil && v.Age >= 18 {
			npc = r
		}
	}
	p := ScenePos(e)
	place := func(dx float64) {
		np := EnsureScenePos(w, npc)
		np.X, np.Y = p.X+dx, p.Y
	}
	coins := core.Purse(e).Coins
	closed := func() bool { tk := Talk(e); return tk == nil || tk.Stage == "" }

	// 1) ушёл за радиус
	place(1)
	if ok, why := StartTalk(w, e, npc.ID); !ok {
		t.Fatalf("разговор рядом должен начаться: %s", why)
	}
	place(60)
	if ok, _ := Say(w, e, "greet", "leave-1"); ok {
		t.Fatal("реплика ушедшему не должна приниматься")
	}
	if !closed() || Knows(e, npc.ID) || core.Purse(e).Coins != coins {
		t.Fatal("разговор должен закрыться без знакомства и без оплаты")
	}
	// 2) оказался в другом поселении (те же локальные координаты)
	place(1)
	if ok, why := StartTalk(w, e, npc.ID); !ok {
		t.Fatalf("разговор рядом должен начаться снова: %s", why)
	}
	var other core.ID
	w.Each("settlement", func(o *core.Entity) {
		if other == 0 && o.ID != se.ID {
			other = o.ID
		}
	})
	core.Place(npc).Settlement = other
	if ok, _ := Say(w, e, "greet", "leave-2"); ok {
		t.Fatal("реплика жителю другого поселения не должна приниматься")
	}
	if !closed() || Knows(e, npc.ID) {
		t.Fatal("разговор должен закрыться без знакомства")
	}
}
