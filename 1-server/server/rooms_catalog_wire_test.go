package server

import (
	"encoding/json"
	"strconv"
	"testing"

	"pogranichye/core"
)

// TestStateFrameRoomsCatalog — полный stateJSON содержит безопасный, отсортированный каталог комнат.
func TestStateFrameRoomsCatalog(t *testing.T) {
	s := New("rooms-catalog-wire", 3, "", []byte("x"), "")

	var characterID core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if characterID != 0 {
			return
		}
		if core.Vitals(e) != nil && core.Mind(e) != nil && core.Home(e) != nil {
			if place := core.Place(e); place != nil && place.Settlement != 0 {
				characterID = e.ID
			}
		}
	})
	if characterID == 0 {
		t.Fatal("нет жителя в поселении")
	}

	var frame map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(characterID)), &frame); err != nil {
		t.Fatalf("кадр не JSON: %v", err)
	}
	place, _ := frame["place"].(map[string]any)
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

	var previous uint64
	for index, raw := range items {
		item, _ := raw.(map[string]any)
		id, ok := item["id"].(string)
		if !ok || id == "" {
			t.Fatalf("items[%d].id должен быть непустой строкой: %#v", index, item)
		}
		parsed, err := strconv.ParseUint(id, 10, 64)
		if err != nil || (index > 0 && parsed <= previous) {
			t.Fatalf("items должны быть отсортированы по числовому id: %q после %d", id, previous)
		}
		previous = parsed
		if _, ok := item["lease_state"].(string); !ok {
			t.Fatalf("items[%d].lease_state должен быть строкой", index)
		}
		if _, ok := item["rent_price"].(float64); !ok {
			t.Fatalf("items[%d].rent_price должен быть числом", index)
		}
		if _, ok := item["available"].(bool); !ok {
			t.Fatalf("items[%d].available должен быть boolean", index)
		}
		if _, leak := item["tenant"]; leak {
			t.Fatalf("каталог не должен раскрывать арендатора: %#v", item)
		}
		if _, leak := item["chest_items"]; leak {
			t.Fatalf("каталог не должен раскрывать содержимое сундука: %#v", item)
		}
	}
}
