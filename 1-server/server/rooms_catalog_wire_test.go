package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
)

// TestStateFrameRoomsCatalog — полный stateJSON жителя поселения содержит публичный каталог комнат
// place.rooms.items[] (v1.20, comms 0181) с id/lease_state/rent_price и БЕЗ утечки арендатора.
func TestStateFrameRoomsCatalog(t *testing.T) {
	s := New("rooms-cat-world", 3, "", []byte("x"), "")

	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid != 0 {
			return
		}
		if core.Vitals(e) != nil && core.Mind(e) != nil && core.Home(e) != nil {
			if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
				cid = e.ID
			}
		}
	})
	if cid == 0 {
		t.Fatal("нет жителя в поселении")
	}

	var live map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &live); err != nil {
		t.Fatalf("кадр не JSON: %v", err)
	}
	place, _ := live["place"].(map[string]any)
	rooms, _ := place["rooms"].(map[string]any)
	if rooms == nil {
		t.Fatal("нет place.rooms в кадре")
	}
	items, ok := rooms["items"].([]any)
	if !ok {
		t.Fatalf("place.rooms.items должен быть массивом, получили %T", rooms["items"])
	}
	// total совместим с длиной каталога
	if tot, _ := rooms["total"].(float64); int(tot) != len(items) {
		t.Fatalf("items[] (%d) должен покрывать total (%v)", len(items), rooms["total"])
	}
	var prev string
	for i, raw := range items {
		it, _ := raw.(map[string]any)
		id, ok := it["id"].(string)
		if !ok || id == "" {
			t.Fatalf("items[%d].id должен быть непустой строкой, получили %T %v", i, it["id"], it["id"])
		}
		if i > 0 && id <= prev {
			t.Fatalf("items должны быть отсортированы по id: %q после %q", id, prev)
		}
		prev = id
		switch it["lease_state"] {
		case "vacant", "active", "grace", "expired":
		default:
			t.Fatalf("items[%d].lease_state неожиданный: %v", i, it["lease_state"])
		}
		if _, ok := it["rent_price"].(float64); !ok {
			t.Fatalf("items[%d].rent_price должен быть числом, получили %T", i, it["rent_price"])
		}
		if _, leak := it["tenant"]; leak {
			t.Fatalf("каталог не должен раскрывать арендатора: %#v", it)
		}
		if _, leak := it["chest_items"]; leak {
			t.Fatalf("каталог не должен раскрывать содержимое сундука: %#v", it)
		}
	}
}
