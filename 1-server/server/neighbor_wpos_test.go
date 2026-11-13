package server

import (
	"encoding/json"
	"math"
	"testing"

	"pogranichye/core"
)

// TestNeighborWposMatchesOwnSchemeAndAnchor — comms 0178, п.1: neighbors[].wpos должен быть той же
// схемы, что me.wpos, и сервер считает его сам (не клиент из place.anchor). Для соседа в ТОМ ЖЕ
// поселении его wpos должен совпасть с anchor+pos (как и у себя), в пределах погрешности округления.
func TestNeighborWposMatchesOwnSchemeAndAnchor(t *testing.T) {
	s := New("neighbor-wpos-world", 2, "", []byte("x"), "")

	var cid core.ID
	var settlement core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid != 0 {
			return
		}
		if core.Vitals(e) != nil && core.Mind(e) != nil {
			if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
				n := 0
				s.world.Each("place", func(o *core.Entity) {
					if core.Place(o).Settlement == pl.Settlement {
						n++
					}
				})
				if n >= 2 { // нужен хоть один сосед в том же селе
					cid, settlement = e.ID, pl.Settlement
				}
			}
		}
	})
	if cid == 0 {
		t.Fatal("нужен житель с хотя бы одним соседом в своём селе")
	}

	var live map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &live); err != nil {
		t.Fatalf("кадр не JSON: %v", err)
	}
	place, _ := live["place"].(map[string]any)
	if place == nil {
		t.Fatal("нет place в кадре")
	}
	anchor, _ := place["anchor"].(map[string]any)
	neighbors, _ := place["neighbors"].([]any)
	if anchor == nil || len(neighbors) == 0 {
		t.Fatalf("нужны anchor и хотя бы один neighbor: anchor=%v neighbors=%v", anchor, neighbors)
	}

	n0, _ := neighbors[0].(map[string]any)
	wpos, _ := n0["wpos"].(map[string]any)
	if wpos == nil {
		t.Fatalf("у соседа должен быть wpos той же схемы, что me.wpos: %#v", n0)
	}
	me, _ := live["me"].(map[string]any)
	mwpos, _ := me["wpos"].(map[string]any)
	for _, k := range []string{"scheme", "v", "frame"} {
		if wpos[k] != mwpos[k] {
			t.Fatalf("neighbor.wpos.%s=%v должен совпадать с me.wpos.%s=%v (одна схема)", k, wpos[k], k, mwpos[k])
		}
	}

	npos, _ := n0["pos"].(map[string]any)
	if npos == nil {
		t.Fatal("у соседа должен остаться локальный pos (совместимость)")
	}
	wantE := anchor["e"].(float64) + npos["x"].(float64)
	wantN := anchor["n"].(float64) + npos["y"].(float64)
	if math.Abs(wpos["e"].(float64)-wantE) > 0.1 || math.Abs(wpos["n"].(float64)-wantN) > 0.1 {
		t.Fatalf("wpos соседа должен быть anchor+pos (как и у себя): wpos=(%.2f,%.2f) ждали (%.2f,%.2f)",
			wpos["e"], wpos["n"], wantE, wantN)
	}
	_ = settlement
}
