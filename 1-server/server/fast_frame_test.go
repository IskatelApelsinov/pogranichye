package server

import (
	"encoding/json"
	"sort"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Быстрый кадр (comms 0059, часть C): только собственное тело/бой игрока и публичные позиции —
// без чужого инвентаря, кошелей, имён и прочих личных данных; состав полей зафиксирован.
func TestFastFrameShapeAndNoLeak(t *testing.T) {
	s := NewStarter("fast-shape", "", []byte("x"), "")
	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if v := core.Vitals(e); cid == 0 && v != nil && v.Age >= 18 {
			if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
				cid = e.ID
			}
		}
	})
	e := s.world.Get(cid)
	s.ctl[cid] = 1
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	s.fastStep(0.1)

	var f map[string]any
	if err := json.Unmarshal([]byte(s.fastJSON(cid)), &f); err != nil {
		t.Fatal(err)
	}
	keys := func(m map[string]any) []string {
		out := make([]string, 0, len(m))
		for k := range m {
			out = append(out, k)
		}
		sort.Strings(out)
		return out
	}
	eq := func(where string, got, want []string) {
		if len(got) != len(want) {
			t.Fatalf("%s: поля %v, ждали %v", where, got, want)
		}
		for i := range got {
			if got[i] != want[i] {
				t.Fatalf("%s: поля %v, ждали %v", where, got, want)
			}
		}
	}
	eq("кадр", keys(f), []string{"alive", "fast", "me", "place", "v"})
	eq("me", keys(f["me"].(map[string]any)), []string{"combat", "energy", "feed", "health", "mana", "pos", "result", "wounds"})
	place := f["place"].(map[string]any)
	eq("place", keys(place), []string{"foes", "neighbors"})
	ns := place["neighbors"].([]any)
	if len(ns) == 0 || len(ns) > neighborCap {
		t.Fatalf("соседей в быстром кадре: %d (предел %d)", len(ns), neighborCap)
	}
	for _, raw := range ns {
		eq("neighbor", keys(raw.(map[string]any)), []string{"id", "pos"}) // ни имени, ни вещей, ни кошеля
	}
}
