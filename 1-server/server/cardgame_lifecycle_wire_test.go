package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Через серверный обработчик: партия, брошенная после обрыва связи (нет ходов дольше порога), не
// блокирует новую — сервер сам закрывает осиротевшую перед стартом (issue #651, часть B).
func TestCardGameStaleStartSelfHealsOverWire(t *testing.T) {
	s := NewStarter("cg-wire", "", []byte("x"), "")
	playing := func(cards []sim.OchkoCard) error {
		cards[51] = sim.OchkoCard{Code: "10♠", Value: 10}
		cards[50] = sim.OchkoCard{Code: "5♣", Value: 5} // игрок: 15, партия идёт
		cards[49] = sim.OchkoCard{Code: "10♦", Value: 10}
		cards[48] = sim.OchkoCard{Code: "7♥", Value: 7} // банкир: 17
		return nil
	}
	s.ochkoShuffle = playing

	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid != 0 {
			return
		}
		if v := core.Vitals(e); v != nil && v.Age >= 18 {
			if p := core.Place(e); p != nil && p.Settlement != 0 {
				cid = e.ID
			}
		}
	})
	if cid == 0 {
		t.Fatal("нет тела для карточной игры")
	}
	e := s.world.Get(cid)
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	if core.Purse(e) == nil {
		s.world.Attach(e, &core.PurseC{Coins: 100, Debts: map[string]float64{}})
	} else {
		core.Purse(e).Coins = 100
	}
	core.Settle(s.world.Get(core.Place(e).Settlement)).Tavern = true
	sim.GoTo(s.world, e, "tavern")

	gameFrame := func(reqid string) map[string]any {
		t.Helper()
		var frame map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
			t.Fatal(err)
		}
		me := frame["me"].(map[string]any)
		result, _ := me["result"].(map[string]any)
		if result == nil || result["reqid"] != reqid {
			t.Fatalf("нет коррелируемого me.result для %s: %#v", reqid, result)
		}
		g, _ := me["card_game"].(map[string]any)
		return g
	}

	// Старт на игровом часе 0: партия идёт, Touched зафиксирован обработчиком.
	s.runner.Clock.TotalHours = 0
	s.applyIntent(cid, `{"v":1,"op":"card_game","action":"start","reqid":"r1"}`)
	if g := gameFrame("r1"); g == nil || g["phase"] != "playing" {
		t.Fatalf("первая партия должна идти: %#v", g)
	}

	// Пока партия свежая, новый старт отклоняется — игрок уже играет.
	s.applyIntent(cid, `{"v":1,"op":"card_game","action":"start","reqid":"r2"}`)
	var frame map[string]any
	_ = json.Unmarshal([]byte(s.stateJSON(cid)), &frame)
	me := frame["me"].(map[string]any)
	if res := me["result"].(map[string]any); res["ok"] != false {
		t.Fatalf("поверх свежей партии старт должен отклоняться: %#v", res)
	}

	// Время ушло за порог без ходов (как при обрыве связи) — следующий старт должен сам закрыть
	// осиротевшую партию и начать новую.
	s.runner.Clock.TotalHours = sim.CardGameAbandonHours + 1
	s.applyIntent(cid, `{"v":1,"op":"card_game","action":"start","reqid":"r3"}`)
	g := gameFrame("r3")
	if g == nil || g["phase"] != "playing" {
		t.Fatalf("после таймаута заброшенной партии новая должна начаться: %#v", g)
	}
}
