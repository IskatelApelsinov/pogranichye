package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Проверка владельческой жалобы #6 из comms 0211 (PR #572): «нет NPC-разбойников». Сервер уже
// заводит `bandit` в foeOrder/foeStats и подсевает его в horячее поселение через populateFoes
// (combat.go) — этот тест проверяет это на НАСТОЯЩЕЙ стартовой деревне тем же реальным
// HTTP+WS/fastStep путём, что и TestStarterVillageResidentsSpreadAndMove (0152/0153), а не на
// синтетическом мире, чтобы отделить «сервер не создаёт бандитов» от «клиент их не показывает».
func TestStarterVillageSpawnsBanditsForPresentPlayer(t *testing.T) {
	s := NewStarter("bandit-presence-demo-0211", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	if r := post(t, ts.URL+"/api/register", `{"login":"watcher","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация наблюдателя: %v", r)
	}
	lr := post(t, ts.URL+"/api/login", `{"login":"watcher","pass":"secret1"}`)
	token, _ := lr["token"].(string)
	if token == "" {
		t.Fatalf("вход наблюдателя: %v", lr)
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

	// Та же инициализация, что handleWS делает при подключении (сцена/знакомства как у настоящего
	// игрока), без отдельного импорта sim — используем уже подключённые в пакете server хелперы.
	s.mu.Lock()
	if e := s.world.Get(cid); e != nil {
		sim.EnsureScenePos(s.world, e)
		sim.EnsurePlayer(s.world, e)
		sim.RollGift(s.world, e, s.runner.Hub.Stream("gift", eid(e.ID)))
		if pl := core.Place(e); pl != nil {
			sim.LearnVisited(s.world, e, pl.Settlement)
		}
	}
	s.mu.Unlock()

	frame := func() map[string]any {
		t.Helper()
		raw := s.stateJSON(cid) // locks s.mu internally
		var f map[string]any
		if err := json.Unmarshal([]byte(raw), &f); err != nil {
			t.Fatalf("кадр не JSON: %v (%s)", err, raw)
		}
		return f
	}
	foeKinds := func(f map[string]any) map[string]int {
		place, _ := f["place"].(map[string]any)
		fs, _ := place["foes"].([]any)
		out := map[string]int{}
		for _, raw := range fs {
			fe, _ := raw.(map[string]any)
			kind, _ := fe["kind"].(string)
			out[kind]++
		}
		return out
	}

	// populateFoes подсевает немедленно на первый fastStep горячего поселения (camp.Timer
	// стартует <=0) — несколько шагов на запас, чтобы не зависеть от порядка систем внутри тика.
	for i := 0; i < 5; i++ {
		s.mu.Lock()
		s.fastStep(0.4)
		s.mu.Unlock()
	}

	kinds := foeKinds(frame())
	if kinds["bandit"] < 1 {
		t.Fatalf("ВОСПРОИЗВЕДЕНА жалоба владельца: сервер не подсеял ни одного bandit в place.foes стартовой деревни (foes=%v)", kinds)
	}
	t.Logf("place.foes после 2с реального времени: %v", kinds)
}
