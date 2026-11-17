package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// TestStateFrameShowsCrossBorderNeighbor — запрос владельца (comms 0179/0182/0184/0196, AOI через
// границу села, v1.22): у ограды близкого соседнего села (минимум по генерации — 500 м) житель
// того соседнего села должен появиться в place.neighbors, а его "pos" должен быть посчитан в
// локальной сцене СМОТРЯЩЕГО (якорь его же села), а не в сцене чужого села — иначе клиент
// расположил бы его в неправильном месте. Строим через настоящий HTTP/WS логин, затем добираем
// мир вручную вторым, близким селом (готовая генерация этого не даёт — сёла разнесены сильнее).
func TestStateFrameShowsCrossBorderNeighbor(t *testing.T) {
	s := NewStarter("aoi-e2e-0196", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	if r := post(t, ts.URL+"/api/register", `{"login":"watcher","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация: %v", r)
	}
	lr := post(t, ts.URL+"/api/login", `{"login":"watcher","pass":"secret1"}`)
	token, _ := lr["token"].(string)
	if token == "" {
		t.Fatalf("вход: %v", lr)
	}
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
	if e == nil {
		s.mu.Unlock()
		t.Fatal("нет тела игрока")
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		s.mu.Unlock()
		t.Fatal("игрок без своего села")
	}
	ownSE := s.world.Get(pl.Settlement)
	own := core.Settle(ownSE)
	// смотрящего — к восточной ограде своего села
	sp := sim.EnsureScenePos(s.world, e)
	sp.X, sp.Y = 200, 0
	sim.EnsurePlayer(s.world, e)
	sim.RollGift(s.world, e, s.runner.Hub.Stream("gift", eid(e.ID)))
	sim.LearnVisited(s.world, e, pl.Settlement)

	// второе село ровно в 500 м к востоку (минимум по правилам генерации) + его житель у СВОЕЙ
	// (западной, обращённой к первому) ограды — должен оказаться физически ближе к смотрящему, чем
	// средняя точка его же села.
	otherSE := s.world.Spawn(&core.SettlementC{Name: "Соседний Починок", X: own.X + 2.0, Y: own.Y, Region: own.Region})
	cross := s.world.Spawn(&core.IdentC{Name: "Соседка", Sex: "ж", Role: "farmer"}, &core.MindC{},
		&core.PlaceC{Settlement: otherSE.ID}, &sim.ScenePosC{X: -200, Y: 0, Facing: 180})
	crossID = cross.ID
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
	ns, _ := placeM["neighbors"].([]any)
	var found map[string]any
	for _, n := range ns {
		nm, _ := n.(map[string]any)
		if nm == nil {
			continue
		}
		if nm["id"] == eid(crossID) {
			found = nm
			break
		}
	}
	if found == nil {
		t.Fatalf("сосед из близкого соседнего села не попал в neighbors (всего %d)", len(ns))
	}
	pos, _ := found["pos"].(map[string]any)
	if pos == nil {
		t.Fatal("у соседа нет pos")
	}
	// мир: якорь своего села (0 м вдоль нашей оси, т.к. own.X — опорная) + 500 м до соседнего села
	// минус 200 м (её смещение к западной ограде) = 300 м от якоря СВОЕГО села вдоль X.
	x, _ := pos["x"].(float64)
	if diff := x - 300; diff < -1 || diff > 1 {
		t.Fatalf("pos.x соседа из чужого села должен быть выражен от якоря СВОЕГО села смотрящего (~300), получено %v", x)
	}
}
