package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Kate: «а то что НПС не было — это нормально?». Проверяем ровно сценарий «вышел — зашёл обратно»:
// соединение закрывается (settlement остывает, ScenePos жителей снимается — архивация по дизайну
// A-Life LOD, sim/heat.go), а затем игрок СНОВА заходит в то же поселение. Ожидаемо: после того как
// хотя бы один fastStep (ровно то, что `tickLoop` гоняет каждые 100мс на живом сервере) успел
// пройти, соседи должны СНОВА появиться в кадре — тело не становится постоянно пустым поселением.
func TestNeighborsRepopulateAfterReconnect(t *testing.T) {
	s := NewStarter("reconnect-repopulate-demo-20261113", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	if r := post(t, ts.URL+"/api/register", `{"login":"kate","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация: %v", r)
	}
	login := func() (core.ID, uint64) {
		lr := post(t, ts.URL+"/api/login", `{"login":"kate","pass":"secret1"}`)
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
		return cid, sid
	}
	frameFor := func(cid core.ID) map[string]any {
		t.Helper()
		var f map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &f); err != nil {
			t.Fatalf("кадр не JSON: %v", err)
		}
		return f
	}
	neighborCount := func(f map[string]any) int {
		place, _ := f["place"].(map[string]any)
		ns, _ := place["neighbors"].([]any)
		return len(ns)
	}
	ctl := func() func(core.ID) bool { return func(id core.ID) bool { return s.ctl[id] != 0 } }
	step := func(n int, dt float64) {
		c := ctl()
		s.mu.Lock()
		for i := 0; i < n; i++ {
			s.hot, s.warm, s.cold = s.heatTracker.Update(s.world, c)
			sim.StepNPCMovement(s.world, dt, c)
		}
		s.mu.Unlock()
	}

	// 1) Первый вход: соседи есть (как в 0152/0153).
	cid, sid := login()
	before := neighborCount(frameFor(cid))
	if before < 5 {
		t.Fatalf("деревня слишком пуста для проверки: %d соседей при первом входе", before)
	}
	t.Logf("первый вход: %d соседей", before)

	// 2) Выход: ровно то, что handleWS делает в defer при закрытии соединения.
	s.release(cid, sid)
	// Дать миру несколько fastStep БЕЗ подключённого игрока — поселение остывает, ScenePos жителей
	// снимается (archiveResidents). Реалистично: на живом сервере это происходит за доли секунды.
	step(5, 0.1)
	empty := neighborCount(frameFor(cid))
	t.Logf("после выхода + остывания: %d соседей в кадре (ScenePos игрока сохраняется — он всегда видит СЕБЯ через me, но neighbors считает по сцене)", empty)

	// 3) Повторный вход в то же тело/поселение.
	cid2, _ := login()
	if cid2 != cid {
		t.Fatalf("повторный вход занял другое тело: было %v, стало %v", cid, cid2)
	}
	// Кадр СРАЗУ после реконнекта, ДО того как сработал хоть один fastStep — реальная гонка на живом
	// сервере (reconnect обрабатывается немедленно, fastStep идёт по своему таймеру раз в 100мс).
	immediately := neighborCount(frameFor(cid))
	t.Logf("сразу после реконнекта, до fastStep: %d соседей", immediately)

	// 4) Через один реальный fastStep (100мс на живом сервере) село должно СНОВА нагреться и
	// заселиться — это и есть «нормально» или «баг», который спросила Kate.
	step(1, 0.1)
	after := neighborCount(frameFor(cid))
	t.Logf("после 1 fastStep после реконнекта: %d соседей", after)
	if after < 5 {
		t.Fatalf("ВОСПРОИЗВЕДЕНА жалоба владельца: после реконнекта и одного fastStep поселение всё ещё пустое (%d соседей) — материализация жителей на нагрев не работает", after)
	}
}
