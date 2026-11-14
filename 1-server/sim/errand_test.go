package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Первое поручение: выдаётся только знакомым и только из настоящей нужды жителя; сдаётся только с
// принесённым; добро ложится в закрома жителя, плата идёт из его кошеля; второй раз подряд не выдаётся.
func TestErrandFromRealNeed(t *testing.T) {
	w, e, se := starterPlayer(t)
	var npc *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); npc == nil && r.ID != e.ID && v != nil && v.Age >= 18 {
			npc = r
		}
	}
	p := ScenePos(e)
	np := EnsureScenePos(w, npc)
	np.X, np.Y = p.X+1, p.Y
	hasWork := func() bool {
		for _, o := range TalkOptionsFor(w, e, npc) {
			if o.ID == "work" {
				return true
			}
		}
		return false
	}
	nst := ensureStashOf(w, npc)
	nst.Goods[econ.GoodWood], nst.Goods[econ.GoodFish] = 0, 10
	core.Purse(npc).Coins = 7

	if hasWork() {
		t.Fatal("незнакомцу поручений не дают")
	}
	StartTalk(w, e, npc.ID)
	if ok, _ := Say(w, e, "work", "e0"); ok {
		t.Fatal("реплика work до знакомства должна отклоняться")
	}
	Say(w, e, "greet", "e1")
	if !hasWork() {
		t.Fatal("знакомый житель без дров должен предложить поручение")
	}
	if ok, why := Say(w, e, "work", "e2"); !ok {
		t.Fatalf("взять поручение: %s", why)
	}
	q := activeErrand(e, npc.ID)
	if q == nil || q.Good != econ.GoodWood || q.Qty != 2 || q.Via != "npc" || q.Coins != 2 {
		t.Fatalf("поручение должно быть про 2 охапки дров за 2 монеты: %+v", q)
	}
	// не принёс — не сдать, монет не прибавилось
	coins := core.Purse(e).Coins
	if ok, _ := Say(w, e, "work", "e3"); !ok || activeErrand(e, npc.ID) == nil || core.Purse(e).Coins != coins {
		t.Fatal("без дров поручение остаётся открытым и не оплачивается")
	}
	addStash(w, e, econ.GoodWood, 3)
	if ok, why := Say(w, e, "work", "e4"); !ok {
		t.Fatalf("сдать поручение: %s", why)
	}
	if activeErrand(e, npc.ID) != nil || econ.Stash(e).Goods[econ.GoodWood] != 1 || nst.Goods[econ.GoodWood] != 2 {
		t.Fatalf("дрова должны перейти жителю: у игрока %.0f, у жителя %.0f", econ.Stash(e).Goods[econ.GoodWood], nst.Goods[econ.GoodWood])
	}
	if core.Purse(e).Coins != coins+2 || core.Purse(npc).Coins != 5 {
		t.Fatal("плата должна уйти из кошеля жителя игроку")
	}
	if hasWork() {
		t.Fatal("тот же житель сразу второй раз не просит")
	}
	// нужды нет — поручения нет
	var other *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); other == nil && r.ID != e.ID && r.ID != npc.ID && v != nil && v.Age >= 18 {
			other = r
		}
	}
	ost := ensureStashOf(w, other)
	ost.Goods[econ.GoodWood], ost.Goods[econ.GoodFish] = 10, 10
	ensureAcquaint(w, e).Known[other.ID] = true
	if errandOption(e, other) != "" {
		t.Fatal("у кого всё есть, тот ни о чём не просит")
	}
}

// TestErrandProvenanceSurvivesSettlementMove — запрос владельца (comms 0215/0218, v1.23): поручение
// взято в селе A — после перехода игрока в село B кадр должен ПО-ПРЕЖНЕМУ показывать A, а не текущее
// (B) и не пересчитываться на лету из текущего места игрока/NPC при каждом snapshot.
func TestErrandProvenanceSurvivesSettlementMove(t *testing.T) {
	w, e, se := starterPlayer(t)
	var npc *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); npc == nil && r.ID != e.ID && v != nil && v.Age >= 18 {
			npc = r
		}
	}
	p := ScenePos(e)
	np := EnsureScenePos(w, npc)
	np.X, np.Y = p.X+1, p.Y
	ensureStashOf(w, npc).Goods[econ.GoodWood], ensureStashOf(w, npc).Goods[econ.GoodFish] = 0, 10
	StartTalk(w, e, npc.ID)
	Say(w, e, "greet", "e1")
	if ok, why := Say(w, e, "work", "e2"); !ok {
		t.Fatalf("взять поручение: %s", why)
	}

	ownName := core.Settle(se).Name
	ownID := eidStr(se.ID)
	issued, _ := OpeningTask(w, e)["issued"].(map[string]any)
	if issued == nil || issued["settlement_id"] != ownID || issued["settlement_name"] != ownName {
		t.Fatalf("сразу после выдачи issued должен показывать своё село %q (%s): %+v", ownName, ownID, issued)
	}

	// переходим в другое (новое, никак не связанное) село
	other := w.Spawn(&core.SettlementC{Name: "Дальнее", X: 900, Y: 900, Region: core.Settle(se).Region})
	core.Place(e).Settlement = other.ID

	issued2, _ := OpeningTask(w, e)["issued"].(map[string]any)
	if issued2 == nil {
		t.Fatal("issued пропал после перехода — поручение должно переживать смену села")
	}
	if issued2["settlement_id"] != ownID || issued2["settlement_name"] != ownName {
		t.Fatalf("после перехода A→B issued должен ВСЁ ЕЩЁ показывать A=%q (%s), не текущее B: %+v",
			ownName, ownID, issued2)
	}
}
