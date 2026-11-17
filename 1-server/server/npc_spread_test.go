package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Прямая проверка владельческой жалобы (comms 0152, P0): «все NPC стоят в одном месте». Строим
// НАСТОЯЩУЮ стартовую деревню (тот же GenerateStarter, что и реальный сервер), заходим одним игроком
// и смотрим фактический кадр place.neighbors[].pos — РОВНО то, что видит клиент. Затем прогоняем
// реальный шаг ходьбы (StepNPCMovement) на несколько секунд реального времени и проверяем, что
// позиции различны и что хотя бы часть жителей сдвинулась. Это отделяет сервер от клиента: если этот
// тест зелёный на живом ростере, причина жалобы — на стороне Unity (рендер/десериализация pos), а не
// в серверной симуляции.
func TestStarterVillageResidentsSpreadAndMove(t *testing.T) {
	s := NewStarter("npc-spread-demo-0152", "", []byte("secret"), "")
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

	// Та же инициализация, что handleWS делает при подключении — чтобы сцена и знакомства были как у
	// настоящего игрока, а не только у «сырого» AI-тела.
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

	type scenePos struct{ x, y float64 }
	frame := func() map[string]any {
		t.Helper()
		raw := s.stateJSON(cid) // locks s.mu internally
		var f map[string]any
		if err := json.Unmarshal([]byte(raw), &f); err != nil {
			t.Fatalf("кадр не JSON: %v (%s)", err, raw)
		}
		return f
	}
	positionsOf := func(f map[string]any) map[string]scenePos {
		place, _ := f["place"].(map[string]any)
		ns, _ := place["neighbors"].([]any)
		out := make(map[string]scenePos, len(ns))
		for _, raw := range ns {
			n, _ := raw.(map[string]any)
			id, _ := n["id"].(string)
			p, _ := n["pos"].(map[string]any)
			x, _ := p["x"].(float64)
			y, _ := p["y"].(float64)
			out[id] = scenePos{x, y}
		}
		return out
	}

	before := positionsOf(frame())
	if len(before) < 5 {
		t.Fatalf("стартовая деревня слишком пуста для проверки скучивания: %d соседей в кадре (%v)", len(before), before)
	}

	uniq := map[scenePos]int{}
	for _, p := range before {
		uniq[p]++
	}
	if len(uniq) < 2 {
		t.Fatalf("ВОСПРОИЗВЕДЕНА жалоба владельца: все %d видимых жителей делят одну позицию сцены %v — скучивание на сервере", len(before), before)
	}
	for p, n := range uniq {
		if n > len(before)/2 {
			t.Fatalf("слишком много жителей (%d из %d) в одной точке %v — похоже на скучивание: %v", n, len(before), p, uniq)
		}
	}

	// Реальный шаг ходьбы (как в fastStep на живом сервере), без ожидания настенных часов теста.
	ctl := func(id core.ID) bool { return s.ctl[id] != 0 }
	s.mu.Lock()
	for i := 0; i < 60; i++ { // 60 * 0.4с = 24с реального времени ходьбы
		s.hot, s.warm, s.cold = s.heatTracker.Update(s.world, ctl)
		sim.StepNPCMovement(s.world, 0.4, ctl)
	}
	s.mu.Unlock()

	after := positionsOf(frame())
	moved := 0
	for id, p0 := range before {
		if p1, ok := after[id]; ok && (p1.x != p0.x || p1.y != p0.y) {
			moved++
		}
	}
	if moved == 0 {
		t.Fatalf("ни один из %d жителей не сдвинулся за 24с реального времени — похоже на замороженных NPC (before=%v after=%v)", len(before), before, after)
	}
	t.Logf("скучивание: %d уникальных позиций из %d соседей; сдвинулось %d из %d за 24с", len(uniq), len(before), moved, len(before))
}
