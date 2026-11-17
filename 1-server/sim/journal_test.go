package sim

import (
	"testing"

	"pogranichye/core"
)

// Личная летопись (#54): записи появляются по РЕАЛЬНОМУ состоянию, по одной на род, монотонным Seq,
// и не дублируются при повторном пересчёте кадра.
func TestJournalRecordsMilestonesOnceAndMonotonic(t *testing.T) {
	w := core.NewWorld("journal-test")
	e := w.Spawn()

	// пусто: ни знакомства, ни труда, ни пива.
	if got := Journal(w, e, 1); len(got) != 0 {
		t.Fatalf("на старте журнал должен быть пуст, получили %d записей", len(got))
	}

	// знакомство → веха met.
	a := &AcquaintC{Known: map[core.ID]bool{7: true}}
	w.Attach(e, a)
	got := Journal(w, e, 2)
	if len(got) != 1 || got[0]["kind"] != "met" {
		t.Fatalf("после знакомства ждём одну веху met, получили %+v", got)
	}
	if seq, _ := got[0]["seq"].(int64); seq != 1 {
		t.Fatalf("первый Seq должен быть 1, получили %v", got[0]["seq"])
	}

	// повторный пересчёт того же состояния не плодит дублей.
	if got := Journal(w, e, 3); len(got) != 1 {
		t.Fatalf("повтор не должен дублировать веху, получили %d", len(got))
	}

	// труд → веха worked со следующим Seq.
	sk := &core.SkillsC{Skills: map[string]float64{"woodcutter": 0.5}}
	w.Attach(e, sk)
	got = Journal(w, e, 4)
	if len(got) != 2 || got[1]["kind"] != "worked" {
		t.Fatalf("после труда ждём вторую веху worked, получили %+v", got)
	}
	if seq, _ := got[1]["seq"].(int64); seq != 2 {
		t.Fatalf("второй Seq должен быть 2, получили %v", got[1]["seq"])
	}

	// кружка пива → веха drank.
	p := EnsurePlayer(w, e)
	p.DrankBeer = true
	got = Journal(w, e, 5)
	if len(got) != 3 || got[2]["kind"] != "drank" {
		t.Fatalf("после пива ждём третью веху drank, получили %+v", got)
	}
	if seq, _ := got[2]["seq"].(int64); seq != 3 {
		t.Fatalf("третий Seq должен быть 3, получили %v", got[2]["seq"])
	}
}
