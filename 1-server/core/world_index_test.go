package core

import "testing"

func TestEachUsesStableComponentIndex(t *testing.T) {
	w := NewWorld("index")
	first := w.Spawn()
	second := w.Spawn(&VitalsC{Health: 20})
	third := w.Spawn(&VitalsC{Health: 30})

	// Attaching a component to an older entity must insert its ID in sorted order.
	w.Attach(first, &VitalsC{Health: 10})
	var got []ID
	w.Each("vitals", func(e *Entity) { got = append(got, e.ID) })
	want := []ID{first.ID, second.ID, third.ID}
	if len(got) != len(want) {
		t.Fatalf("Each returned %v, want %v", got, want)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("Each order = %v, want %v", got, want)
		}
	}

	w.Detach(second, "vitals")
	w.Remove(third.ID)
	got = got[:0]
	w.Each("vitals", func(e *Entity) { got = append(got, e.ID) })
	if len(got) != 1 || got[0] != first.ID {
		t.Fatalf("after detach/remove Each returned %v, want [%d]", got, first.ID)
	}

	// Reattaching must not create duplicate index entries.
	w.Attach(first, &VitalsC{Health: 99})
	got = got[:0]
	w.Each("vitals", func(e *Entity) { got = append(got, e.ID) })
	if len(got) != 1 || got[0] != first.ID {
		t.Fatalf("reattach duplicated index entry: %v", got)
	}
}

func BenchmarkEachIndexed(b *testing.B) {
	w := NewWorld("benchmark")
	for i := 0; i < 10000; i++ {
		w.Spawn(&VitalsC{Health: 100})
	}
	b.ReportAllocs()
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		w.Each("vitals", func(*Entity) {})
	}
}
