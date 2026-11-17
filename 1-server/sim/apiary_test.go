package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Пасека как реальное занятие игрока (comms 0089): мёд вынимается из запаса улья в общий склад,
// не творится из воздуха и не задваивает фоновых бортников; отказ ничего не стоит.

func hiveWithStock(t *testing.T, w *core.World, se *core.Entity, stock float64) *econ.ResourceNodeC {
	t.Helper()
	h := hiveOf(w, se.ID)
	if h == nil {
		t.Fatal("в стартовом селе должна быть пасека")
	}
	h.Stock = stock
	return h
}

func TestApiaryRequiresBeingAtTheMeadow(t *testing.T) {
	w, e, se := starterPlayer(t)
	hiveWithStock(t, w, se, 10)
	energy := core.Vitals(e).Energy
	if ok, why := Apiary(w, e, core.Summer, "ap0"); ok || why == "" {
		t.Fatalf("вдали от пасеки не должно получиться: ok=%t why=%q", ok, why)
	}
	if core.Vitals(e).Energy != energy {
		t.Fatal("отказ не должен стоить сил")
	}
}

func TestApiaryMovesHoneyFromHiveToSettlementStore(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "garden")
	h := hiveWithStock(t, w, se, 10)
	s := core.Settle(se)
	honey, coins := s.Store[econ.GoodHoney], core.Purse(e).Coins

	if ok, why := Apiary(w, e, core.Summer, "ap1"); !ok {
		t.Fatalf("сбор мёда должен получиться: %s", why)
	}
	if h.Stock != 10-apiaryPortion {
		t.Fatalf("из улья должно уйти %v, осталось %v", apiaryPortion, h.Stock)
	}
	if got := s.Store[econ.GoodHoney] - honey; got != apiaryPortion {
		t.Fatalf("на склад села должно прийти ровно вынутое (%v), пришло %v", apiaryPortion, got)
	}
	if core.Purse(e).Coins <= coins {
		t.Fatal("за труд на пасеке должна быть выплата")
	}
	if sk := core.Skills(e); sk == nil || sk.Level("herbalist") <= 0 {
		t.Fatal("пасека должна растить навык herbalist")
	}

	// повтор того же reqid — тот же ответ, второй рамки нет
	if ok, _ := Apiary(w, e, core.Summer, "ap1"); !ok {
		t.Fatal("повтор reqid должен быть идемпотентным")
	}
	if h.Stock != 10-apiaryPortion {
		t.Fatal("повтор reqid не должен вынимать мёд второй раз")
	}
}

func TestApiaryRefusesWhenEmptyWinterOrTired(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "garden")
	h := hiveWithStock(t, w, se, 0.5)
	s := core.Settle(se)
	honey, energy := s.Store[econ.GoodHoney], core.Vitals(e).Energy

	if ok, why := Apiary(w, e, core.Summer, "e1"); ok || why == "" {
		t.Fatal("из пустых сот мёда не взять")
	}
	h.Stock = 10
	if ok, why := Apiary(w, e, core.Winter, "e2"); ok || why == "" {
		t.Fatal("зимой ульи не трогают")
	}
	core.Vitals(e).Energy = 3
	if ok, why := Apiary(w, e, core.Summer, "e3"); ok || why == "" {
		t.Fatal("без сил на пасеке не работают")
	}
	if s.Store[econ.GoodHoney] != honey || h.Stock != 10 || core.Vitals(e).Energy != 3 {
		t.Fatal("отказы не должны менять мир")
	}
	// причина устранена — тот же reqid, что получил отказ, теперь срабатывает
	core.Vitals(e).Energy = energy
	if ok, why := Apiary(w, e, core.Summer, "e3"); !ok {
		t.Fatalf("после отказа повтор должен сработать: %s", why)
	}
}

func TestUnwearFreesSlotAndKeepsItem(t *testing.T) {
	w, e, _ := starterPlayer(t)
	inv := ensureInv(w, e)
	inv.Items["cloak"] = 1
	if ok, why := Wear(w, e, "cloak", "w1"); !ok {
		t.Fatalf("надеть плащ: %s", why)
	}
	slot := wearSlot["cloak"]
	if ok, why := Unwear(w, e, "cloak", "u1"); !ok {
		t.Fatalf("снять плащ: %s", why)
	}
	if _, worn := inv.Worn[slot]; worn || inv.Items["cloak"] != 1 {
		t.Fatal("после снятия слот пуст, а плащ остаётся в суме")
	}
	if ok, _ := Unwear(w, e, "cloak", "u1"); !ok {
		t.Fatal("повтор reqid идемпотентен")
	}
	if ok, why := Unwear(w, e, "cloak", "u2"); ok || why == "" {
		t.Fatal("снять ненадетое нельзя")
	}
	if ok, why := Unwear(w, e, "rod", "u3"); ok || why == "" {
		t.Fatal("удочку не носят")
	}
}
