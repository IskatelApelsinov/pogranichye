package server

import (
	"sort"
	"testing"

	"pogranichye/core"
)

func TestNearestReturnsBoundedStableClosestResidents(t *testing.T) {
	w := core.NewWorld("nearest")
	player := w.Spawn(&core.PlaceC{Settlement: 1}, &core.IdentC{Name: "player"})
	for i := 0; i < 80; i++ {
		w.Spawn(&core.PlaceC{Settlement: 1}, &core.IdentC{Name: "resident"})
	}
	// A resident in another settlement must never leak into this player's frame.
	w.Spawn(&core.PlaceC{Settlement: 2}, &core.IdentC{Name: "outsider"})
	s := &Server{world: w}

	got := s.nearest(player, 1, 12)
	if len(got) != 12 {
		t.Fatalf("nearest returned %d residents, want 12", len(got))
	}
	type candidate struct {
		e *core.Entity
		d float64
	}
	px, py, _ := neighborPos(player)
	all := make([]candidate, 0, 80)
	w.Each("place", func(e *core.Entity) {
		if e.ID == player.ID || core.Place(e).Settlement != 1 {
			return
		}
		x, y, _ := neighborPos(e)
		all = append(all, candidate{e, (x-px)*(x-px) + (y-py)*(y-py)})
	})
	sort.Slice(all, func(i, j int) bool {
		if all[i].d != all[j].d { return all[i].d < all[j].d }
		return all[i].e.ID < all[j].e.ID
	})
	for i := range got {
		if got[i].ID != all[i].e.ID {
			t.Fatalf("nearest[%d]=%d, want %d", i, got[i].ID, all[i].e.ID)
		}
	}
	if len(s.nearest(player, 1, 0)) != 0 {
		t.Fatal("zero-cap query must return no residents")
	}
}
