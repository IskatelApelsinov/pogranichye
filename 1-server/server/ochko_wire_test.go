package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

func TestOchkoIntentAndPrivateFrame(t *testing.T) {
	s := NewStarter("ochko-wire", "", []byte("x"), "")
	s.ochkoShuffle = func(cards []sim.OchkoCard) error {
		cards[51] = sim.OchkoCard{Code: "10♠", Value: 10}
		cards[50] = sim.OchkoCard{Code: "9♠", Value: 9}
		cards[49] = sim.OchkoCard{Code: "10♣", Value: 10}
		cards[48] = sim.OchkoCard{Code: "7♦", Value: 7}
		cards[47] = sim.OchkoCard{Code: "2♥", Value: 2}
		return nil
	}
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

	send := func(action, reqid string) map[string]any {
		t.Helper()
		msg := `{"v":1,"op":"card_game","action":"` + action + `","reqid":"` + reqid + `"}`
		s.applyIntent(cid, msg)
		var frame map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
			t.Fatal(err)
		}
		me := frame["me"].(map[string]any)
		result, _ := me["result"].(map[string]any)
		if result == nil || result["reqid"] != reqid {
			t.Fatalf("нет коррелируемого me.result для %s: %#v", reqid, result)
		}
		return me
	}

	me := send("start", "ochko-start")
	game, ok := me["card_game"].(map[string]any)
	if !ok || game["game"] != "ochko" || game["phase"] != "playing" {
		t.Fatalf("партия не появилась в кадре: %#v", me["card_game"])
	}
	dealer := game["dealer"].([]any)
	if len(dealer) != 1 || game["hidden"] != float64(1) || game["dtotal"] != nil {
		t.Fatalf("скрытая карта банкира утекла: %#v", game)
	}
	player := game["player"].([]any)
	if len(player) != 2 {
		t.Fatalf("на старте игрок должен получить две карты: %#v", player)
	}

	me = send("hit", "ochko-hit")
	game = me["card_game"].(map[string]any)
	player = game["player"].([]any)
	count := len(player)
	// Повтор одного reqid — ACK без второго добора.
	me = send("hit", "ochko-hit")
	game = me["card_game"].(map[string]any)
	if len(game["player"].([]any)) != count {
		t.Fatal("повтор hit с тем же reqid добрал лишнюю карту")
	}

	me = send("stand", "ochko-stand")
	game = me["card_game"].(map[string]any)
	if game["phase"] != "done" || game["hidden"] != float64(0) {
		t.Fatalf("после stand партия должна завершиться и раскрыть банкира: %#v", game)
	}
	if len(game["dealer"].([]any)) < 2 {
		t.Fatalf("банкир должен раскрыть минимум две карты: %#v", game["dealer"])
	}
}
