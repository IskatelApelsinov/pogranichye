package server

import (
	"encoding/json"
	"math"
	"net/http/httptest"
	"os"
	"testing"
)

// v1.25 по проводу: GET /api/spaces отдаёт корчму стартового села домом в два яруса с погребом,
// чанк без построек — пустой список, кривые параметры — 400. Тот же ответ лежит образцом в общем
// fixture (его читает и клиент): если форма уедет, тест напомнит обновить образец.
func TestSpacesEndpoint(t *testing.T) {
	s := NewStarter("spaces-api", "", []byte("x"), "")
	h := s.Handler()

	world := getJSON(t, h, "/api/world")
	set := world["settlements"].([]any)[0].(map[string]any)
	layout := getJSON(t, h, "/api/layout?settlement="+set["id"].(string))
	var tav map[string]any
	for _, b := range layout["buildings"].([]any) {
		if bm := b.(map[string]any); bm["building_id"] == "tavern" {
			tav = bm
		}
	}
	if tav == nil {
		t.Fatal("в стартовом селе нет корчмы")
	}
	e := set["e"].(float64) + (tav["x0"].(float64)+tav["x1"].(float64))/2
	n := set["n"].(float64) + (tav["y0"].(float64)+tav["y1"].(float64))/2
	cx, cz := int64(math.Floor(e/256)), int64(math.Floor(n/256))
	url := "/api/spaces?cx=" + itoa(cx) + "&cz=" + itoa(cz)
	v := getJSON(t, h, url)
	if v["scheme"] != "yof-tangent" || v["v"] != float64(1) || v["frame"] != float64(0) || v["revision"].(float64) <= 0 {
		t.Fatalf("конверт: %v %v %v %v", v["scheme"], v["v"], v["frame"], v["revision"])
	}
	if ch := v["chunk"].([]any); ch[0] != float64(cx) || ch[1] != float64(cz) {
		t.Fatalf("chunk: %v", ch)
	}
	var sp map[string]any
	for _, x := range v["spaces"].([]any) {
		if m := x.(map[string]any); m["id"] == "b:"+set["id"].(string)+":tavern" {
			sp = m
		}
	}
	if sp == nil {
		t.Fatalf("корчмы нет в ответе: %v", v["spaces"])
	}
	for _, k := range []string{"id", "kind", "parent_id", "frame", "origin", "bounds", "floors", "surfaces", "rooms", "portals", "stairs"} {
		if _, ok := sp[k]; !ok {
			t.Fatalf("у пространства нет поля %q", k)
		}
	}
	if sp["kind"] != "building" || len(sp["floors"].([]any)) != 3 || len(sp["stairs"].([]any)) != 2 || len(sp["portals"].([]any)) != 1 {
		t.Fatalf("корчма: %v, ярусов %d", sp["kind"], len(sp["floors"].([]any)))
	}
	if again := getJSON(t, h, url); again["revision"] != v["revision"] {
		t.Fatal("отпечаток изменился между двумя одинаковыми запросами")
	}
	if far := getJSON(t, h, "/api/spaces?cx=-900000&cz=-900000"); len(far["spaces"].([]any)) != 0 {
		t.Fatalf("в пустом чанке не должно быть пространств: %v", far["spaces"])
	}
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest("GET", "/api/spaces?cx=a&cz=1", nil))
	if rec.Code != 400 {
		t.Fatalf("кривой cx: код %d", rec.Code)
	}

	// образец для клиента: те же поля у конверта и у пространства
	const path = "../../3-dev/contract/fixtures/spaces_v1.example.json"
	if os.Getenv("UPDATE_FIXTURES") == "1" {
		raw, _ := json.MarshalIndent(v, "", "  ")
		if err := os.WriteFile(path, append(raw, '\n'), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("нет образца %s (создать: UPDATE_FIXTURES=1 go test ./server -run TestSpacesEndpoint): %v", path, err)
	}
	var fx map[string]any
	if err := json.Unmarshal(raw, &fx); err != nil {
		t.Fatal(err)
	}
	sameKeys(t, "конверт", v, fx)
	sameKeys(t, "пространство", sp, fx["spaces"].([]any)[0].(map[string]any))
}

func sameKeys(t *testing.T, what string, live, fx map[string]any) {
	t.Helper()
	for k := range live {
		if _, ok := fx[k]; !ok {
			t.Fatalf("%s: в образце нет поля %q", what, k)
		}
	}
	for k := range fx {
		if _, ok := live[k]; !ok {
			t.Fatalf("%s: в живом ответе нет поля %q из образца", what, k)
		}
	}
}

func itoa(v int64) string {
	b, _ := json.Marshal(v)
	return string(b)
}
