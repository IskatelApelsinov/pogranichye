package chronicle

import (
	"testing"

	"pogranichye/core"
)

// Летопись растит пожизненные счётчики и держит кольцо последних записей (снимок не пухнет без края).
func TestRecordTallyAndCap(t *testing.T) {
	w := core.NewWorld("chr")
	w.Spawn(&ChronicleC{Tally: map[string]int{}})

	for i := 0; i < maxEntries+50; i++ {
		Record(w, int64(i), "основание", "село")
	}
	Record(w, 9999, "война", "держава на державу")

	c := Of(w)
	if c == nil {
		t.Fatal("летопись должна находиться")
	}
	if c.Tally["основание"] != maxEntries+50 {
		t.Fatalf("счётчик оснований должен считать ВСЕ события: %d", c.Tally["основание"])
	}
	if c.Tally["война"] != 1 {
		t.Fatalf("счётчик войн: %d", c.Tally["война"])
	}
	if len(c.Entries) > maxEntries {
		t.Fatalf("построчно держим не больше предела: %d", len(c.Entries))
	}
	// самая свежая запись на месте, давние свёрнуты (остались только в счётчиках)
	last := c.Entries[len(c.Entries)-1]
	if last.Kind != "война" || last.Day != 9999 {
		t.Fatalf("последняя запись должна быть свежей войной, получили %+v", last)
	}
}

// Без сущности-летописи Record молчит (не паникует) — мир просто ничего не помнит.
func TestRecordWithoutChronicleIsSafe(t *testing.T) {
	w := core.NewWorld("chr2")
	Record(w, 1, "основание", "село") // летописи нет
	if Of(w) != nil {
		t.Fatal("летописи не было — и не должно появиться")
	}
}
