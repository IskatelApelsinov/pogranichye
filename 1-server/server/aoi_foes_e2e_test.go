package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// TestStateFrameShowsCrossBorderFoe — comms 0219 п.3 (гэп, подтверждённый и отложенный в comms
// 0221): place.foes должен быть AOI-осведомлён о близких чужих сёлах у межи, как уже давно
// AOINeighbors для place.neighbors (см. TestStateFrameShowsCrossBorderNeighbor рядом). Тот же
// сценарий: второе село в 500 м к востоку, но вместо жителя — настоящая сущность FoeC (бандит) у
// ЕГО западной ограды; её позиция в кадре должна быть выражена от якоря СМОТРЯЩЕГО, а не потеряна
// только из-за того, что f.Settlement — чужое.
func TestStateFrameShowsCrossBorderFoe(t *testing.T) {
	s := NewStarter("aoi-foes-e2e-0219", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	if r := post(t, ts.URL+"/api/register", `{"login":"watcher3","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация: %v", r)
	}
	lr := post(t, ts.URL+"/api/login", `{"login":"watcher3","pass":"secret1"}`)
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

	var crossID core.ID
	s.mu.Lock()
	e := s.world.Get(cid)
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		s.mu.Unlock()
		t.Fatal("игрок без своего села")
	}
	ownSE := s.world.Get(pl.Settlement)
	own := core.Settle(ownSE)
	sp := sim.EnsureScenePos(s.world, e)
	sp.X, sp.Y = 200, 0 // к восточной ограде своего села
	sim.EnsurePlayer(s.world, e)
	sim.RollGift(s.world, e, s.runner.Hub.Stream("gift", eid(e.ID)))
	sim.LearnVisited(s.world, e, pl.Settlement)

	otherSE := s.world.Spawn(&core.SettlementC{Name: "Соседний Починок", X: own.X + 2.0, Y: own.Y, Region: own.Region})
	foe := s.world.Spawn(&sim.FoeC{Species: "bandit", Settlement: otherSE.ID, X: -200, Y: 0, HomeX: -200, HomeY: 0, HP: 45, Max: 45, State: "roam"})
	crossID = foe.ID
	s.mu.Unlock()

	raw := s.stateJSON(cid)
	var frame map[string]any
	if err := json.Unmarshal([]byte(raw), &frame); err != nil {
		t.Fatalf("разбор кадра: %v", err)
	}
	placeM, _ := frame["place"].(map[string]any)
	if placeM == nil {
		t.Fatal("нет place в кадре")
	}
	fs, _ := placeM["foes"].([]any)
	var found map[string]any
	for _, fv := range fs {
		fm, _ := fv.(map[string]any)
		if fm != nil && fm["id"] == eid(crossID) {
			found = fm
			break
		}
	}
	if found == nil {
		t.Fatalf("бандит из близкого соседнего села не попал в place.foes (всего %d)", len(fs))
	}
	x, _ := found["x"].(float64)
	if diff := x - 300; diff < -1 || diff > 1 {
		t.Fatalf("x бандита из чужого села должен быть выражен от якоря СВОЕГО села смотрящего (~300), получено %v", x)
	}
}
