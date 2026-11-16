package sim

import (
	"strings"
	"testing"

	"pogranichye/core"
)

// Село ходит в урочища: запас ближних мест убывает, добыча ложится в закрома, путник видит след.
func TestVillagersForageNearbyWildPlaces(t *testing.T) {
	w, hub := Generate("forays", 12)
	tr := terrainFor(w)
	// село, у которого рядом есть хоть одно место, где есть что собрать
	var se *core.Entity
	var spot WildPlace
	w.Each("settlement", func(e *core.Entity) {
		if se != nil {
			return
		}
		ae, an := Anchor(w, core.Settle(e))
		for _, p := range WildPlacesNear(w, tr, ae, an, forayReach) {
			if forageGood(p.Kind) != "" {
				se, spot = e, p
				return
			}
		}
	})
	if se == nil {
		t.Skip("рядом с сёлами нет урочищ с запасом")
	}
	s := core.Settle(se)
	total := func() float64 {
		sum := 0.0
		for _, g := range []string{"herb", "wood", "ore", "fish", "veg"} {
			sum += s.Store[g]
		}
		return sum
	}
	before := total()
	got := 0
	for day := 1; day <= 4 && got == 0; day++ { // опасное место может спугнуть — даём несколько дней
		got = RunForays(w, day, hub.Stream("foray-test"))
	}
	if got == 0 {
		t.Fatal("за четыре дня ни одна ватага не вернулась с добычей")
	}
	if total() <= before {
		t.Fatalf("в закромах села не прибавилось: было %.1f, стало %.1f", before, total())
	}
	u := peekWildUse(w)
	if u == nil || len(u.Foray) == 0 {
		t.Fatal("следов похода нет")
	}
	touched := ""
	for id := range u.Foray {
		if wildLeft(u, id, u.Foray[id].Day) >= wildStockCap {
			t.Fatalf("место %s посещено, а запас не убыл", id)
		}
		if wildLeft(u, id, u.Foray[id].Day) < forayLeave {
			t.Fatalf("место %s выбрано дочиста — путнику ничего не оставили", id)
		}
		touched = id
	}
	if note := forayNote(u, touched, u.Foray[touched].Day); !strings.Contains(note, "сегодня здесь были люди из села") {
		t.Fatalf("след для путника: %q", note)
	}
	if forayNote(u, touched, u.Foray[touched].Day+5) != "" {
		t.Fatal("старый след не должен показываться")
	}
	_ = spot
}

// Выбранное место ватага обходит: каждый день по одному и тому же лугу не ходят до дна.
func TestForaysNeverEmptyAPlace(t *testing.T) {
	w, hub := Generate("forays", 12)
	for i := 0; i < 40; i++ {
		RunForays(w, 1, hub.Stream("foray-test")) // сорок ватаг в один день — запас не восполняется
	}
	u := peekWildUse(w)
	if u == nil {
		t.Skip("нет походов")
	}
	for id := range u.Stock {
		if left := wildLeft(u, id, 1); left < forayLeave {
			t.Fatalf("место %s выбрано до %.1f", id, left)
		}
	}
}
