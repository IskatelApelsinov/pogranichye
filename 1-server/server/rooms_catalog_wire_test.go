package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
)

// TestStateFrameRoomsCatalog checks the public room directory through the actual wire snapshot,
// not only the lower-level sim view. Private tenant and chest data must never leak into place.rooms.
func TestStateFrameRoomsCatalog(t *testing.T) {
	s := New("rooms-catalog-wire-world", 3, "", []byte("x"), "")

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

	var frame map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
		t.Fatalf("кадр не JSON: %v", err)
	}
	place, _ := frame["place"].(map[string]any)
	if place == nil {
		t.Fatal("нет place в кадре")
	}
	rooms, _ := place["rooms"].(map[string]any)
	if rooms == nil {
		t.Fatal("нет place.rooms в кадре")
	}
	items, ok := rooms["items"].([]any)
	if !ok {
		t.Fatalf("place.rooms.items должен быть массивом, получили %T", rooms["items"])
	}
	if total, _ := rooms["total"].(float64); int(total) != len(items) {
		t.Fatalf("items[] (%d) должен покрывать total (%v)", len(items), rooms["total"])
	}

	previousID := ""
	for i, raw := range items {
		item, _ := raw.(map[string]any)
		if item == nil {
			t.Fatalf("items[%d] должен быть объектом", i)
		}
		id, ok := item["id"].(string)
		if !ok || id == "" {
			t.Fatalf("items[%d].id должен быть непустой строкой, получили %T %v", i, item["id"], item["id"])
		}
		if i > 0 && id <= previousID {
			t.Fatalf("items должны быть отсортированы по id: %q после %q", id, previousID)
		}
		previousID = id
		switch item["lease_state"] {
		case "vacant", "active", "grace", "expired":
		default:
			t.Fatalf("items[%d].lease_state неожиданный: %v", i, item["lease_state"])
		}
		if _, ok := item["rent_price"].(float64); !ok {
			t.Fatalf("items[%d].rent_price должен быть числом, получили %T", i, item["rent_price"])
		}
		if _, ok := item["available"].(bool); !ok {
			t.Fatalf("items[%d].available должен быть bool, получили %T", i, item["available"])
		}
		if _, leak := item["tenant"]; leak {
			t.Fatalf("каталог не должен раскрывать арендатора: %#v", item)
		}
		if _, leak := item["chest_items"]; leak {
			t.Fatalf("каталог не должен раскрывать содержимое сундука: %#v", item)
		}
	}
}
