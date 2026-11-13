package server

import (
	"encoding/json"
	"math"
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// TestNeighborsWposMatchesProjectionAndTracksMovement — запрошенный в comms 0201 регресс: Unity
// (`WorldPositionProjection.TryToUnityPosition`) берёт позицию соседа из `neighbors[].wpos`, а не
// из `neighbors[].pos` — `TestStarterVillageResidentsSpreadAndMove` (comms 0152/0153) проверяет
// только `pos` и не покрывает именно то поле, которым руководствуется клиент. Строим ту же живую
// стартовую деревню через настоящий WS/HTTP стек и проверяем ИМЕННО wpos всего видимого ростера.
func TestNeighborsWposMatchesProjectionAndTracksMovement(t *testing.T) {
	s := NewStarter("npc-wpos-e2e-0201", "", []byte("secret"), "")
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

	type wp struct {
		e, n, u float64
		frame   int64
		scheme  string
		v       int64
	}
	type full struct {
		me     wp
		mePos  struct{ x, y float64 }
		nbrs   map[string]wp
		nbrPos map[string]struct{ x, y float64 }
	}
	frameOf := func() full {
		t.Helper()
		raw := s.stateJSON(cid)
		var f map[string]any
		if err := json.Unmarshal([]byte(raw), &f); err != nil {
			t.Fatalf("кадр не JSON: %v (%s)", err, raw)
		}
		me, _ := f["me"].(map[string]any)
		mw, _ := me["wpos"].(map[string]any)
		mp, _ := me["pos"].(map[string]any)
		out := full{
			me: wp{
				e: mw["e"].(float64), n: mw["n"].(float64), u: mw["u"].(float64),
				frame: int64(mw["frame"].(float64)), scheme: mw["scheme"].(string), v: int64(mw["v"].(float64)),
			},
			nbrs:   map[string]wp{},
			nbrPos: map[string]struct{ x, y float64 }{},
		}
		out.mePos.x, _ = mp["x"].(float64)
		out.mePos.y, _ = mp["y"].(float64)
		place, _ := f["place"].(map[string]any)
		ns, _ := place["neighbors"].([]any)
		for _, raw := range ns {
			n, _ := raw.(map[string]any)
			id, _ := n["id"].(string)
			nw, ok := n["wpos"].(map[string]any)
			if !ok || nw == nil {
				t.Fatalf("у соседа %s нет wpos в кадре — клиентская проекция не сможет его разместить", id)
			}
			out.nbrs[id] = wp{
				e: nw["e"].(float64), n: nw["n"].(float64), u: nw["u"].(float64),
				frame: int64(nw["frame"].(float64)), scheme: nw["scheme"].(string), v: int64(nw["v"].(float64)),
			}
			np, _ := n["pos"].(map[string]any)
			var pxy struct{ x, y float64 }
			pxy.x, _ = np["x"].(float64)
			pxy.y, _ = np["y"].(float64)
			out.nbrPos[id] = pxy
		}
		return out
	}

	before := frameOf()
	if len(before.nbrs) < 5 {
		t.Fatalf("стартовая деревня слишком пуста: %d соседей с wpos", len(before.nbrs))
	}

	// 1) wpos присутствует у всех, та же схема/версия/frame, что me.wpos, и не схлопывается в точку.
	uniqWpos := map[[2]float64]int{}
	for id, w := range before.nbrs {
		if w.scheme != before.me.scheme || w.v != before.me.v || w.frame != before.me.frame {
			t.Fatalf("сосед %s: схема/версия/frame wpos (%s/%d/%d) не совпадает с me.wpos (%s/%d/%d)",
				id, w.scheme, w.v, w.frame, before.me.scheme, before.me.v, before.me.frame)
		}
		uniqWpos[[2]float64{math.Round(w.e), math.Round(w.n)}]++
	}
	if len(uniqWpos) < 2 {
		t.Fatalf("все %d соседей делят одну и ту же глобальную позицию wpos — схлопывание", len(before.nbrs))
	}

	// 2) проекционное равенство для соседей своего поселения: me.pos + (nbr.wpos - me.wpos) == nbr.pos
	//    (допуск: 0.1м — округление pos на wire, r1; плюс связанная с ним погрешность wpos, r3).
	const tol = 0.15
	checked := 0
	for id, w := range before.nbrs {
		p, ok := before.nbrPos[id]
		if !ok {
			continue
		}
		projX := before.mePos.x + (w.e - before.me.e)
		projY := before.mePos.y + (w.n - before.me.n)
		if math.Abs(projX-p.x) > tol || math.Abs(projY-p.y) > tol {
			t.Fatalf("сосед %s: проекция (%.3f,%.3f) не совпадает с pos (%.3f,%.3f) в пределах %.2fм",
				id, projX, projY, p.x, p.y, tol)
		}
		checked++
	}
	if checked == 0 {
		t.Fatal("ни один сосед не попал в проверку равенства проекции")
	}

	// 3) реальный шаг ходьбы (как fastStep на живом сервере) — wpos должен меняться у ТЕХ ЖЕ entity ID,
	//    что и pos, а не оставаться статичным, пока pos шевелится (и наоборот).
	ctl := func(id core.ID) bool { return s.ctl[id] != 0 }
	s.mu.Lock()
	for i := 0; i < 60; i++ { // 60 * 0.4с = 24с реального времени ходьбы
		s.hot, s.warm, s.cold = s.heatTracker.Update(s.world, ctl)
		sim.StepNPCMovement(s.world, 0.4, ctl)
	}
	s.mu.Unlock()

	after := frameOf()
	movedPos, movedWpos, mismatched := 0, 0, 0
	for id, p0 := range before.nbrPos {
		p1, okPos := after.nbrPos[id]
		w0 := before.nbrs[id]
		w1, okWpos := after.nbrs[id]
		if !okPos || !okWpos {
			continue
		}
		posMoved := p1.x != p0.x || p1.y != p0.y
		wposMoved := math.Abs(w1.e-w0.e) > 1e-6 || math.Abs(w1.n-w0.n) > 1e-6
		if posMoved {
			movedPos++
		}
		if wposMoved {
			movedWpos++
		}
		if posMoved != wposMoved {
			mismatched++
		}
	}
	t.Logf("wpos: %d уникальных позиций из %d соседей; за 24с сдвинулось pos=%d, wpos=%d из %d; несовпадений=%d",
		len(uniqWpos), len(before.nbrs), movedPos, movedWpos, len(before.nbrPos), mismatched)
	if movedWpos == 0 {
		t.Fatalf("ни один сосед не сдвинул wpos за 24с, хотя pos сдвинулся у %d — wpos не отслеживает реальное движение", movedPos)
	}
	if mismatched > 0 {
		t.Fatalf("%d соседей, у которых pos и wpos расходятся в том, сдвинулись ли они — pos/wpos рассинхронизированы", mismatched)
	}
}
