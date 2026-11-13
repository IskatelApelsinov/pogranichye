package server

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

func getJSON(t *testing.T, h http.Handler, url string) map[string]any {
	t.Helper()
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest("GET", url, nil))
	if rec.Code != 200 {
		t.Fatalf("%s: код %d: %s", url, rec.Code, rec.Body.String())
	}
	var out map[string]any
	if err := json.Unmarshal(rec.Body.Bytes(), &out); err != nil {
		t.Fatalf("%s: не JSON: %v", url, err)
	}
	return out
}

// v1.19 по проводу: раскладка на поселение, карта мира, высоты чанка, положение игрока в мире.
func TestWorldEndpointsAndFrameCoordinates(t *testing.T) {
	s := NewStarter("world-api", "", []byte("x"), "")
	h := s.Handler()

	// без параметра — стартовое село, как отдавалось всегда (старый клиент не ломается)
	legacy := getJSON(t, h, "/api/layout")
	if legacy["version"] != float64(2) || len(legacy["buildings"].([]any)) != 8 || legacy["plan"] != "starter" {
		t.Fatalf("/api/layout без параметра должен отдавать стартовую раскладку v2: %v", legacy["plan"])
	}

	world := getJSON(t, h, "/api/world")
	if world["scheme"] != sim.WorldScheme || world["chunk_m"] != float64(sim.ChunkMeters) || world["seamless"] != false {
		t.Fatalf("/api/world: %v", world)
	}
	sets := world["settlements"].([]any)
	if len(sets) < 20 || len(world["roads"].([]any)) == 0 {
		t.Fatalf("/api/world: поселений %d", len(sets))
	}
	plans := map[string]bool{}
	for _, raw := range sets {
		st := raw.(map[string]any)
		if st["cauldron"] != true {
			continue
		}
		lay := getJSON(t, h, st["layout"].(string))
		if lay["settlement"] != st["id"] || lay["plan"] != st["plan"] || lay["anchor"].(map[string]any)["e"] != st["e"] {
			t.Fatalf("раскладка села %v не сходится с картой мира", st["id"])
		}
		for _, k := range []string{"buildings", "river", "pois", "zones", "roads", "homes", "entry"} {
			if lay[k] == nil {
				t.Fatalf("в раскладке села %v нет поля %s", st["id"], k)
			}
		}
		plans[lay["plan"].(string)] = true
	}
	if len(plans) < 4 {
		t.Fatalf("у сёл котла должны быть разные планы застройки, получили %v", plans)
	}
	if rec := httptest.NewRecorder(); true {
		h.ServeHTTP(rec, httptest.NewRequest("GET", "/api/terrain?cx=x&cz=1", nil))
		if rec.Code != http.StatusBadRequest {
			t.Fatalf("кривой запрос высот должен отклоняться, код %d", rec.Code)
		}
	}

	// кадр игрока: me.wpos = place.anchor + me.pos, чанк и высота на месте
	cid, _, _ := s.reserveBody(1)
	e := s.world.Get(cid)
	sim.EnsurePlayer(s.world, e)
	p := sim.EnsureScenePos(s.world, e)
	p.X, p.Y, p.Z = -20.5, 31.25, 6
	var f map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &f); err != nil {
		t.Fatal(err)
	}
	wp := f["me"].(map[string]any)["wpos"].(map[string]any)
	an := f["place"].(map[string]any)["anchor"].(map[string]any)
	if d := wp["e"].(float64) - (an["e"].(float64) - 20.5); d > 0.0011 || d < -0.0011 {
		t.Fatalf("wpos.e должно быть anchor.e + pos.x: %v против %v", wp["e"], an["e"])
	}
	if d := wp["n"].(float64) - (an["n"].(float64) + 31.25); d > 0.0011 || d < -0.0011 {
		t.Fatalf("wpos.n должно быть anchor.n + pos.y")
	}
	if wp["z"] != 6.0 || wp["u"].(float64)-wp["ground"].(float64) != 6 || an["u"] != wp["ground"] {
		t.Fatalf("высота: %v", wp)
	}
	ch := wp["chunk"].([]any)
	tc := getJSON(t, h, fmt.Sprintf("/api/terrain?cx=%v&cz=%v&step=32", ch[0], ch[1]))
	if tc["side"] != float64(9) || len(tc["heights"].([]any)) != 81 || tc["id"] != fmt.Sprintf("0:%v:%v", ch[0], ch[1]) {
		t.Fatalf("/api/terrain: %v", tc["id"])
	}
	if f["place"].(map[string]any)["plan"] != "starter" {
		t.Fatal("place.plan стартового села — starter")
	}
}

// Бесшовный режим и высота тела переживают сохранение.
func TestSeamlessAndHeightSurviveRestart(t *testing.T) {
	path := filepath.Join(t.TempDir(), "world.sav")
	a := NewStarter("seamless-save", "", []byte("x"), "")
	a.SetSeamless(true)
	cid, _, _ := a.reserveBody(1)
	sim.EnsurePlayer(a.world, a.world.Get(cid))
	p := sim.EnsureScenePos(a.world, a.world.Get(cid))
	p.X, p.Y, p.Z = 900, -1200, -4.5 // далеко за околицей, в подземелье
	if err := a.SaveTo(path); err != nil {
		t.Fatal(err)
	}
	b := NewStarter("other", "", []byte("x"), "")
	if ok, err := b.LoadFrom(path); err != nil || !ok {
		t.Fatalf("загрузка: %v %v", ok, err)
	}
	if !sim.Seamless(b.world) {
		t.Fatal("бесшовный режим должен сохраняться вместе с миром")
	}
	q := sim.ScenePos(b.world.Get(cid))
	if q == nil || q.X != 900 || q.Y != -1200 || q.Z != -4.5 {
		t.Fatalf("место и высота тела после загрузки: %+v", q)
	}
	var settlement core.ID = core.Place(b.world.Get(cid)).Settlement
	if l := sim.LayoutOf(b.world, settlement); !l.Open || l.Blocked(900, -1200) {
		t.Fatal("после загрузки бесшовного мира поле за околицей проходимо")
	}
}
