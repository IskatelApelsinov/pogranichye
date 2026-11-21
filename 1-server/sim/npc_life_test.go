package sim

import (
	"testing"

	"pogranichye/arcane"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
	"pogranichye/mind"
)

// Ушёл корчмарь — на его место встаёт житель (сперва без ремесла, иначе из многолюдного ремесла),
// становится хозяином села и продаёт пиво.
func TestTavernkeeperVacancyIsFilled(t *testing.T) {
	w, _, se := starterPlayer(t)
	s := core.Settle(se)
	old := w.Get(s.Keeper)
	if old == nil {
		t.Skip("нет корчмаря")
	}
	w.Remove(old.ID) // корчмарь ушёл
	if n := FillVacancies(w, 3); n == 0 {
		t.Fatal("пустое место корчмаря никто не занял")
	}
	k := w.Get(s.Keeper)
	if k == nil || core.Ident(k).Role != "tavernkeeper" || core.Place(k).Settlement != se.ID {
		t.Fatalf("новый корчмарь не назначен: %+v", k)
	}
	if FillVacancies(w, 4) != 0 {
		t.Fatal("занятое место не должно перезаниматься")
	}
}

// Отчаявшийся житель становится разбойником-человеком; ватага занимает опустевшее село и ходит в
// набег на живое: еда из закромов села переходит в суму разбойников.
func TestBanditsAreThePeopleAndTakeEmptyVillage(t *testing.T) {
	w, hub := Generate("bandit-lair", 10)
	idx := residentIndex(w)
	var home, empty *core.Entity
	w.Each("settlement", func(se *core.Entity) {
		if home == nil && len(idx[se.ID]) >= 6 {
			home = se
		}
	})
	// пустим соседнее село: все его жители ушли
	hs := core.Settle(home)
	best := 1e9
	w.Each("settlement", func(se *core.Entity) {
		if se.ID == home.ID || isOuter(w, core.Settle(se)) != isOuter(w, hs) {
			return
		}
		s := core.Settle(se)
		if d := (s.X-hs.X)*(s.X-hs.X) + (s.Y-hs.Y)*(s.Y-hs.Y); d < best {
			best, empty = d, se
		}
	})
	if empty == nil || best > banditLairReach*banditLairReach {
		t.Skip("нет соседнего села в пределах досягаемости")
	}
	for _, r := range idx[empty.ID] {
		w.Remove(r.ID)
	}
	var pair []*core.Entity
	for _, r := range idx[home.ID] {
		if v := core.Vitals(r); len(pair) < 2 && v.Age >= 18 && core.Settle(home).Keeper != r.ID {
			pair = append(pair, r)
		}
	}
	for i, r := range pair {
		x, y, wild, roam := banditCampFor(w, home.ID, i)
		turnBandit(w, r, home.ID, x, y, wild, roam)
	}
	for _, r := range pair {
		if !IsBanditPerson(r) || w.Get(r.ID) != r || core.Mind(r) == nil || core.Vitals(r) == nil {
			t.Fatal("разбойник должен остаться тем же человеком (имя, тело, разум)")
		}
	}
	hs.Store["bread"] = 50
	RunBandits(w, 10)
	if lairOf(empty) == nil || Foe(pair[0]).Settlement != empty.ID {
		t.Fatalf("ватага не заняла пустое село: lair=%v at=%d", lairOf(empty) != nil, Foe(pair[0]).Settlement)
	}
	v, _ := LairView(w, empty.ID).(map[string]any)
	if v == nil || v["bandits"] != 2 {
		t.Fatalf("place.lair: %v", v)
	}
	got := 0.0
	for _, b := range pair {
		if st := econ.Stash(b); st != nil {
			for _, n := range st.Goods {
				got += n
			}
		}
	}
	if got < 1 {
		t.Fatal("набег ничего не принёс: еда из закромов ближнего живого села должна уйти в суму разбойников")
	}
	// МindSystem не делает из разбойника работника
	r := core.NewRunnerWith(hub, core.Clock{TotalHours: 12}, MindSystem{})
	r.Step(w)
	if m := core.Mind(pair[0]); m.ActCode != "bandit" || m.Goal == mind.GoalWork {
		t.Fatalf("разбойник принялся за честный труд: %q/%q", m.Goal, m.ActCode)
	}
	// ватагу перебили — логово пустеет
	for _, b := range pair {
		w.Remove(b.ID)
	}
	RunBandits(w, 11)
	if lairOf(empty) != nil {
		t.Fatal("логово без разбойников должно опустеть")
	}
}

// Кров: житель с монетами на угол снимает его, а не стоит вечно «в поисках»; накопив — покупает избу.
func TestHomeSeekingEndsInRentOrHouse(t *testing.T) {
	w, hub := GenerateStarter("homes")
	var e *core.Entity
	w.Each("mind", func(x *core.Entity) {
		if e == nil && core.Home(x) != nil && core.Vitals(x).Age >= 18 && Player(x) == nil {
			e = x
		}
	})
	h, p := core.Home(e), core.Purse(e)
	h.Status, p.Coins = "none", 27 // раньше: искал (≥25), но изба 30 — застревал навсегда
	core.Vitals(e).Hunger, core.Vitals(e).Energy = 10, 90
	m := core.Mind(e)
	m.Goal, m.GoalUntil = mind.GoalSeekHome, 1e9
	r := core.NewRunnerWith(hub, core.Clock{TotalHours: 10}, MindSystem{})
	r.Step(w)
	if h.Status != "rented" {
		t.Fatalf("с 27 монетами житель должен снять угол, статус %q", h.Status)
	}
	p.Coins = 50
	m.Goal, m.GoalUntil = mind.GoalSeekHome, 1e9
	r.Step(w)
	if h.Status != "owned" {
		t.Fatalf("накопив, житель должен купить избу, статус %q", h.Status)
	}
}

// Лишние маги-ремесленники (старое сохранение) возвращаются к делу: в селе остаётся столько магов,
// сколько мест; дар у вернувшихся сохраняется. Новый пробудившийся при занятых местах остаётся при
// своём ремесле.
func TestExtraMagesReturnToCrafts(t *testing.T) {
	w, _, se := starterPlayer(t)
	res := residentsOf(w, se.ID)
	made := 0
	for _, r := range res {
		if v := core.Vitals(r); v.Age >= 18 && Player(r) == nil && core.Settle(se).Keeper != r.ID && made < 9 {
			core.Ident(r).Role = "mage"
			w.Attach(r, &arcane.MageC{Awakened: true})
			made++
		}
	}
	FillVacancies(w, 5)
	if got, seats := mageRolesIn(w, se.ID), mageSeats(len(residentsOf(w, se.ID))); got != seats {
		t.Fatalf("магов-ремесленников %d при местах %d", got, seats)
	}
	gifted := 0
	for _, r := range residentsOf(w, se.ID) {
		if m := arcane.Mage(r); m != nil && m.Awakened {
			gifted++
		}
	}
	if gifted < made {
		t.Fatal("у вернувшихся к ремеслу пропал дар")
	}
	if DescFor("mage") != "маг" || DescFor("bandit") == "местный" {
		t.Fatal("маг и разбойник не должны подписываться «местный»")
	}
}

// Фон (MindSystem.Hour) идёт по ВСЕМ сущностям с MindC, включая тело игрока — но игрок зарабатывает
// и растит навык ТОЛЬКО явным op:"act" на верном месте, а не просто простаивая с присвоенной целью
// GoalWork. Без этой защиты реальные тики мира, идущие пока ответ едет по сокету между собственными
// действиями игрока, могли сами начислить ему зарплату и навык — ровно так проявился флак
// TestVerticalSliceTalkWorkBeer (сервер покупка пива увеличивала кошелёк; comms 0313, Grok).
// НЕигровых жителей это не касается: фон как и раньше платит им и растит навык — тест держит оба
// исхода рядом, чтобы не превратить защиту игрока в тихую порчу экономики NPC.
func TestPlayerIdleGoalWorkDoesNotAutoPay(t *testing.T) {
	w, e, se := starterPlayer(t)
	if core.Ident(e).Role != "farmer" {
		t.Fatalf("ожидали игрока-фермера для теста, получили %q", core.Ident(e).Role)
	}
	var npc *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if r.ID != e.ID && Player(r) == nil && core.Ident(r).Role == "farmer" {
			npc = r
			break
		}
	}
	if npc == nil {
		t.Fatal("нет второго жителя-фермера для контраста")
	}

	clock := core.Clock{TotalHours: 100}
	for _, who := range []*core.Entity{e, npc} {
		core.Vitals(who).Energy = 100
		m := core.Mind(who)
		m.Pinned = ""
		// цель уже назначена и ещё держится — mind.Decide() в этот тик не перевыбирает её.
		m.Goal, m.GoalUntil = mind.GoalWork, clock.TotalHours+10
	}
	playerCoins0 := core.Purse(e).Coins
	playerSkill0 := core.Skills(e).Level("farmer")
	npcCoins0 := core.Purse(npc).Coins
	npcSkill0 := core.Skills(npc).Level("farmer")

	MindSystem{}.Hour(w, clock, dice.NewHub("player-no-autowage"))

	if c := core.Purse(e).Coins; c != playerCoins0 {
		t.Fatalf("фон не должен платить игроку без его op:\"act\": было %.2f, стало %.2f", playerCoins0, c)
	}
	if sk := core.Skills(e).Level("farmer"); sk != playerSkill0 {
		t.Fatalf("фон не должен растить навык игрока без его op:\"act\": было %.2f, стало %.2f", playerSkill0, sk)
	}
	if core.Mind(e).Goal != mind.GoalWork {
		t.Fatal("занятие (Goal/ActCode) игрока должно отражаться для анимации/летописи, даже без оплаты")
	}

	if c := core.Purse(npc).Coins; c <= npcCoins0 {
		t.Fatalf("фон должен платить НЕигровому жителю за труд: было %.2f, стало %.2f", npcCoins0, c)
	}
	if sk := core.Skills(npc).Level("farmer"); sk <= npcSkill0 {
		t.Fatalf("фон должен растить навык НЕигрового жителя: было %.2f, стало %.2f", npcSkill0, sk)
	}
}
