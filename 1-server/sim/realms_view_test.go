package sim

import (
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Игрок должен видеть, в чьих землях стоит село, и обоз на тракте — там, где тот на самом деле идёт.
func TestRealmAndCaravansAreVisible(t *testing.T) {
	w, _ := Generate("realms-visible", 40)
	var re *core.Entity
	var r *econ.RealmC
	w.Each("realm", func(e *core.Entity) {
		if rc := econ.Realm(e); rc != nil && r == nil && len(rc.Territory) > 0 {
			re, r = e, rc
		}
	})
	if r == nil {
		t.Skip("в мире нет держав")
	}
	v, _ := PlaceRealmView(w, r.Capital).(map[string]any)
	if v == nil || v["name"] != r.Name || v["is_capital"] != true || v["id"] != eidStr(re.ID) {
		t.Fatalf("столица не видит свою державу: %v", v)
	}
	if PlaceRealmView(w, core.ID(999999)) != nil {
		t.Fatal("несуществующее село не должно принадлежать державе")
	}
	if len(RealmsView(w)) == 0 {
		t.Fatal("/api/world должен перечислять державы")
	}

	// Обоз из столицы в другое село: в начале суток — у столицы, к вечеру — дальше по тракту.
	var to core.ID
	w.Each("settlement", func(e *core.Entity) {
		if to == 0 && e.ID != r.Capital {
			to = e.ID
		}
	})
	if to == 0 {
		t.Skip("одно село")
	}
	w.Spawn(&econ.CaravanC{From: r.Capital, To: to, Seller: re.ID, Good: econ.GoodCloth, Amount: 20, Dist: 700, Progress: 0})
	tr := NewTerrain(w)
	morning := CaravansView(w, tr, 0)
	evening := CaravansView(w, tr, 18)
	if len(morning) != 1 || len(evening) != 1 {
		t.Fatalf("обоз не виден: %v / %v", morning, evening)
	}
	ae, an := Anchor(w, settleOf(w, r.Capital))
	d0 := math.Hypot(morning[0]["e"].(float64)-ae, morning[0]["n"].(float64)-an)
	d1 := math.Hypot(evening[0]["e"].(float64)-ae, evening[0]["n"].(float64)-an)
	if d0 > 1 || d1 <= d0 {
		t.Fatalf("обоз должен уходить от столицы в течение дня: утром %.1f м, вечером %.1f м", d0, d1)
	}
	if evening[0]["progress"].(float64) <= 0 || evening[0]["progress"].(float64) >= 1 {
		t.Fatalf("доля пути вне (0,1): %v", evening[0]["progress"])
	}
}
