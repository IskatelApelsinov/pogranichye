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
