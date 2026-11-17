package econ

import (
	"pogranichye/core"
	"testing"
)

// Стояние двигается и держится в пределах [-100,100]; классификация союз/вражда по порогам.
func TestStandNudgeAndClassify(t *testing.T) {
	r := &RealmC{Relations: map[core.ID]float64{}}
	other := core.ID(7)

	r.Nudge(other, 1000) // перебор вверх
	if r.Stand(other) != 100 {
		t.Fatalf("стояние не должно превышать 100: %.1f", r.Stand(other))
	}
	if !Allied(r, other) {
		t.Fatal("при стоянии 100 должны быть союзники")
	}
	r.Nudge(other, -1000) // перебор вниз
	if r.Stand(other) != -100 {
		t.Fatalf("стояние не должно опускаться ниже -100: %.1f", r.Stand(other))
	}
	if !IsRival(r, other) {
		t.Fatal("при стоянии -100 должны быть враги")
	}
	// незнакомая держава — нейтралитет (0), ни союз, ни вражда
	if Allied(r, core.ID(99)) || IsRival(r, core.ID(99)) {
		t.Fatal("с незнакомой державой — нейтралитет")
	}
}
