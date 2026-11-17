package sim

import (
	"testing"

	"pogranichye/core"
)

// Пустой reqid отвергается (идемпотентность обязательна).
func TestActionRequiresReqID(t *testing.T) {
	w := core.NewWorld("t")
	wx, wy := poiByKind("woods")
	e := actor(w, wx, wy, 100)
	if ok, why := ChopWood(w, e, ""); ok {
		t.Fatalf("пустой reqid должен отвергаться: %s", why)
	}
}

// Повтор СТАРОГО reqid после другого запроса не применяется второй раз (bounded история, а не «последний»).
func TestStaleReqIDNotReapplied(t *testing.T) {
	w := core.NewWorld("t")
	wx, wy := poiByKind("woods")
	e := actor(w, wx, wy, 100)
	ChopWood(w, e, "a") // 1-е бревно
	ChopWood(w, e, "b") // 2-е бревно (другой reqid)
	ChopWood(w, e, "a") // ПОВТОР старого — не должен дать 3-е
	if got := core.Vitals(e).Energy; got != 100-2*chopEnergy {
		t.Fatalf("старый reqid применился повторно: energy=%.0f (ожид %.0f)", got, 100-2*chopEnergy)
	}
}

// Разряд вне поселения/в стене — отказ (проверка зоны).
func TestCastRequiresZone(t *testing.T) {
	w := core.NewWorld("t")
	e := actor(w, 0, 0, 100) // без PlaceC
	EnsurePlayer(w, e)
	if ok, _ := CastSpark(w, e, "c1"); ok {
		t.Fatal("вне угодий поселения разряд невозможен")
	}
}

// Второе приглашение не должно перезаписывать уже ожидающее приглашение игрока.
func TestCoDrinkDoesNotOverwritePendingInvitation(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, se.ID)
	b := playerAt(w, tx+1, ty, 10, se.ID)
	c := playerAt(w, tx+2, ty, 10, se.ID)

	if ok, why := InviteCoDrink(w, a, b.ID, func(id core.ID) bool { return true }); !ok {
		t.Fatalf("первое приглашение должно пройти: %s", why)
	}
	if ok, _ := InviteCoDrink(w, c, b.ID, func(id core.ID) bool { return true }); ok {
		t.Fatal("второе приглашение не должно перезаписывать ожидающее")
	}
	if ok, _ := InviteCoDrink(w, a, c.ID, func(id core.ID) bool { return true }); ok {
		t.Fatal("пригласивший с исходящим приглашением не должен создавать ещё одно")
	}

	ca, cb := CoDrink(a), CoDrink(b)
	if ca == nil || ca.State != "out" || ca.Partner != b.ID {
		t.Fatalf("исходное приглашение пригласившего потеряно: %+v", ca)
	}
	if cb == nil || cb.State != "in" || cb.Partner != a.ID {
		t.Fatalf("исходное входящее приглашение перезаписано: %+v", cb)
	}
	if CoDrink(c) != nil {
		t.Fatal("отклонённая попытка не должна создавать состояние приглашения")
	}
}

// Принятие отклоняется, если у пригласившего больше нет встречной записи.
func TestAcceptCoDrinkRejectsStaleInvitation(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, se.ID)
	b := playerAt(w, tx+1, ty, 10, se.ID)

	if ok, why := InviteCoDrink(w, a, b.ID, func(id core.ID) bool { return true }); !ok {
		t.Fatalf("приглашение должно пройти: %s", why)
	}
	CoDrink(a).State = "" // имитируем снятое/устаревшее исходящее приглашение
	beforeBeer := core.Settle(w.Get(se.ID)).Store[BeerGood]
	beforeA, beforeB := core.Purse(a).Coins, core.Purse(b).Coins

	if ok, _ := AcceptCoDrink(w, b); ok {
		t.Fatal("устаревшее приглашение не должно приниматься")
	}
	if got := core.Settle(w.Get(se.ID)).Store[BeerGood]; got != beforeBeer {
		t.Fatalf("при отказе запас пива изменился: %.1f -> %.1f", beforeBeer, got)
	}
	if core.Purse(a).Coins != beforeA || core.Purse(b).Coins != beforeB {
		t.Fatal("при отказе монеты игроков не должны меняться")
	}
	if c := CoDrink(b); c != nil && c.State != "" {
		t.Fatalf("устаревшее входящее приглашение не очищено: %+v", c)
	}
}

// Успешный co-drink поднимает монотонный social_event у обоих (клиент ловит момент тоста).
func TestCoDrinkFiresSocialEvent(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 6}})
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, se.ID)
	b := playerAt(w, tx+1, ty, 10, se.ID)
	InviteCoDrink(w, a, b.ID, func(id core.ID) bool { return true })
	AcceptCoDrink(w, b)
	ea, eb := SocialEvent(a), SocialEvent(b)
	if ea == nil || eb == nil || ea.Seq == 0 || eb.Seq == 0 {
		t.Fatal("co-drink должен поднять social_event обоим")
	}
	if ea.Type != "codrink" || ea.With != b.ID || eb.With != a.ID {
		t.Fatalf("social_event неверен: %+v / %+v", ea, eb)
	}
}

// Знание: вселение даёт visited, знакомство с местным — known (строго различимы).
func TestKnowledgeVisitedVsKnown(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "Деревня"})
	tx, ty := poiByKind("tavern")
	p := playerAt(w, tx, ty, 10, se.ID)
	LearnVisited(w, p, se.ID)
	f := Knowledge(p).Places[se.ID]
	if !f.Visited || f.Known {
		t.Fatalf("после прибытия: visited, НЕ known: %+v", f)
	}
	npc := villagerAt(w, "smith", tx+1, ty, se.ID)
	StartTalk(w, p, npc.ID)
	Say(w, p, "greet", "r90454") // познакомился → known
	f = Knowledge(p).Places[se.ID]
	if !f.Known || !f.Visited {
		t.Fatalf("после знакомства: known (и visited): %+v", f)
	}
}
