package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// task_ai 006 A, сквозной сценарий клиента-менеджера по проводу: выбрать корчму → сервер ведёт тело →
// me.at подтверждает прибытие → купить пиво → выпить. Без телепорта и без локального «прибытия».
func TestThinClientWalksToTavernAndBuysBeer(t *testing.T) {
	s := NewStarter("walk-wire", "", []byte("x"), "")
	cid, _, st := s.reserveBody(1)
	if st != "ok" {
		t.Fatalf("нет тела: %s", st)
	}
	e := s.world.Get(cid)
	sim.EnsurePlayer(s.world, e)
	sim.EnsureScenePos(s.world, e)
	core.Purse(e).Coins = 9
	core.Vitals(e).Energy = 100
	se := s.world.Get(core.Place(e).Settlement)
	if k := s.world.Get(core.Settle(se).Keeper); k != nil && core.Mind(k) != nil {
		core.Mind(k).ActCode = "keep_tavern"
	}
	frame := func() map[string]any {
		var f map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &f); err != nil {
			t.Fatal(err)
		}
		return f["me"].(map[string]any)
	}
	if me := frame(); me["walk"] != nil || me["at"] == nil {
		t.Fatalf("до команды тело стоит: walk=%v at=%v", me["walk"], me["at"])
	}
	start := frame()["pos"].(map[string]any)

	s.applyIntent(cid, `{"v":1,"op":"goto","location":"tavern"}`)
	me := frame()
	if w, _ := me["walk"].(map[string]any); w == nil || w["to"] != "tavern" || w["left"].(float64) <= 0 {
		t.Fatalf("после команды сервер должен вести тело к корчме: %v", me["walk"])
	}
	if p := me["pos"].(map[string]any); p["x"] != start["x"] || p["y"] != start["y"] {
		t.Fatal("команда не переносит тело мгновенно")
	}
	// пиво до прибытия не продают
	s.applyIntent(cid, `{"v":1,"op":"buy","item":"beer","reqid":"early"}`)
	if r, _ := frame()["result"].(map[string]any); r == nil || r["ok"] != false {
		t.Fatalf("в пути пиво не продают: %v", r)
	}
	arrived := false
	for i := 0; i < 900 && !arrived; i++ { // до 90 с реального времени
		s.mu.Lock()
		s.fastStep(0.1)
		s.mu.Unlock()
		me = frame()
		arrived = me["walk"] == nil
	}
	if !arrived || me["at"] != "tavern" {
		t.Fatalf("тело должно дойти до корчмы: walk=%v at=%v", me["walk"], me["at"])
	}
	s.applyIntent(cid, `{"v":1,"op":"buy","item":"beer","reqid":"b1"}`)
	if r, _ := frame()["result"].(map[string]any); r == nil || r["ok"] != true {
		t.Fatalf("у стойки пиво продают: %v", r)
	}
	s.applyIntent(cid, `{"v":1,"op":"consume","item":"beer","reqid":"d1"}`)
	if r, _ := frame()["result"].(map[string]any); r == nil || r["ok"] != true {
		t.Fatalf("купленное пиво можно выпить: %v", r)
	}
	// ручной шаг отменяет самоход
	s.applyIntent(cid, `{"v":1,"op":"goto","location":"woods"}`)
	s.applyIntent(cid, `{"v":1,"op":"move","mx":1,"my":0,"dt":0.1,"seq":1}`)
	if frame()["walk"] != nil {
		t.Fatal("ручной шаг должен отменить самоход")
	}
}
