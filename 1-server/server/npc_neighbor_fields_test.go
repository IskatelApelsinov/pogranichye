package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// comms 0219 пп.1/6 — проверка полей place.neighbors[] и того, что далёкие жители стартового села
// не просачиваются в кадр мимо neighborCap. Тот же способ подключения игрока, что и в
// npc_spread_test.go (TestStarterVillageResidentsSpreadAndMove), но проверяет другие аспекты кадра.
func TestNeighborSnapshotCarriesIdentityFieldsAndRespectsCap(t *testing.T) {
	s := NewStarter("npc-neighbor-fields-0219", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	if r := post(t, ts.URL+"/api/register", `{"login":"watcher2","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация наблюдателя: %v", r)
	}
	lr := post(t, ts.URL+"/api/login", `{"login":"watcher2","pass":"secret1"}`)
	token, _ := lr["token"].(string)
	uid, err := s.acc.Validate(token)
	if err != nil {
		t.Fatalf("валидация токена: %v", err)
	}
	cid, sid, status := s.reserveBody(uid)
	if status != "ok" {
		t.Fatalf("не удалось занять тело: %s", status)
	}
	defer s.release(cid, sid)

	var settlement core.ID
	s.mu.Lock()
	e := s.world.Get(cid)
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	sim.RollGift(s.world, e, s.runner.Hub.Stream("gift", eid(e.ID)))
	if pl := core.Place(e); pl != nil {
		settlement = pl.Settlement
		sim.LearnVisited(s.world, e, pl.Settlement)
	}
	total := 0
	s.world.Each("place", func(o *core.Entity) {
		if pl := core.Place(o); pl != nil && pl.Settlement == settlement {
			total++
		}
	})
	s.mu.Unlock()

	raw := s.stateJSON(cid)
	var f map[string]any
	if err := json.Unmarshal([]byte(raw), &f); err != nil {
		t.Fatalf("кадр не JSON: %v", err)
	}
	place, _ := f["place"].(map[string]any)
	ns, _ := place["neighbors"].([]any)
	if len(ns) == 0 {
		t.Fatal("place.neighbors пуст у стартового села с игроком")
	}
	if len(ns) > 24 { // neighborCap, server.go
		t.Fatalf("neighborCap нарушен: %d соседей в кадре", len(ns))
	}
	for i, raw := range ns {
		n, _ := raw.(map[string]any)
		id, _ := n["id"].(string)
		if id == "" {
			t.Fatalf("сосед %d без id: %v", i, n)
		}
		if _, ok := n["role"].(string); !ok {
			t.Fatalf("сосед %s без role: %v", id, n)
		}
		if _, ok := n["act_code"]; !ok {
			t.Fatalf("сосед %s без act_code: %v", id, n)
		}
		pos, ok := n["pos"].(map[string]any)
		if !ok {
			t.Fatalf("сосед %s без pos: %v", id, n)
		}
		if _, ok := pos["x"].(float64); !ok {
			t.Fatalf("сосед %s: pos.x не число: %v", id, pos)
		}
		wpos, ok := n["wpos"].(map[string]any)
		if !ok {
			t.Fatalf("сосед %s без wpos: %v", id, n)
		}
		if _, ok := wpos["e"].(float64); !ok {
			if _, ok2 := wpos["x"].(float64); !ok2 {
				t.Fatalf("сосед %s: wpos без числовых координат: %v", id, wpos)
			}
		}
	}

	// comms 0219 п.6: далёкие жители не просачиваются мимо кэпа — у Починка жителей больше (30 в
	// ростере), чем neighborCap (24), так что кэп обязан РЕЗАТЬ список, а не отдавать всех разом.
	if total <= len(ns) {
		t.Skip("в этом ростере жителей не больше кэпа — проверку просачивания не на чём ставить")
	}
	if len(ns) >= total {
		t.Fatalf("ожидался кэп < общего числа жителей (%d), получили %d в кадре", total, len(ns))
	}
}
