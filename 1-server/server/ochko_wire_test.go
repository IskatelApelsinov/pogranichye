package server

import (
	"encoding/json"
	"path/filepath"
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

func TestOchkoPersistsAcrossWorldSaveLoad(t *testing.T) {
	s := NewStarter("ochko-persist", "", []byte("x"), "")
	s.ochkoShuffle = func(cards []sim.OchkoCard) error {
		cards[51] = sim.OchkoCard{Code: "10♠", Value: 10}
		cards[50] = sim.OchkoCard{Code: "8♠", Value: 8}
		cards[49] = sim.OchkoCard{Code: "10♣", Value: 10}
		cards[48] = sim.OchkoCard{Code: "7♦", Value: 7}
		return nil
	}
	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid == 0 {
			if v := core.Vitals(e); v != nil && v.Age >= 18 {
				if p := core.Place(e); p != nil && p.Settlement != 0 {
					cid = e.ID
				}
			}
		}
	})
	if cid == 0 {
		t.Fatal("нет тела для сохранения партии")
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
	s.applyIntent(cid, `{"v":1,"op":"card_game","action":"start","reqid":"persist-start"}`)
	if g := sim.Ochko(e); g == nil || g.Done {
		t.Fatalf("expected active game before save: %#v", g)
	}
	path := filepath.Join(t.TempDir(), "world.sav")
	if err := s.SaveTo(path); err != nil {
		t.Fatalf("save: %v", err)
	}
	restored := NewStarter("other-seed", "", []byte("x"), "")
	loaded, err := restored.LoadFrom(path)
	if err != nil || !loaded {
		t.Fatalf("load: loaded=%t err=%v", loaded, err)
	}
	re := restored.world.Get(cid)
	g := sim.Ochko(re)
	if g == nil || g.Done || len(g.Player) != 2 || len(g.Dealer) != 2 || len(g.Deck) != 48 {
		t.Fatalf("active private game did not survive save/load: %#v", g)
	}
	if got := core.Purse(re).Coins; got != 90 {
		t.Fatalf("stake should remain deducted exactly once after load: %.2f", got)
	}
	var frame map[string]any
	if err := json.Unmarshal([]byte(restored.stateJSON(cid)), &frame); err != nil {
		t.Fatal(err)
	}
	view := frame["me"].(map[string]any)["card_game"].(map[string]any)
	if len(view["dealer"].([]any)) != 1 || view["hidden"] != float64(1) || view["dtotal"] != nil {
		t.Fatalf("saved hidden card leaked after reconnect: %#v", view)
	}
}

// TestOchkoLeaveOverWireUnblocksAfterReconnect — task_ai 006/issue #651 (Б), сквозной сценарий:
// игрок начал партию, "вылетел" (здесь — save/load, как в TestOchkoPersistsAcrossWorldSaveLoad,
// проксирующем reconnect), партия сохранилась активной (так и должно быть — не сбрасываем из-за
// обрыва связи), но до этого PR старт новой партии оставался заблокирован навсегда ("ты уже
// играешь партию"), потому что доиграть могло только то же действие от игрока, которого он больше
// не присылал. Проверяем новый "leave" op целиком через applyIntent/me.result, как клиент его бы
// увидел, и что после него новая партия стартует.
func TestOchkoLeaveOverWireUnblocksAfterReconnect(t *testing.T) {
	s := NewStarter("ochko-leave-wire", "", []byte("x"), "")
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
	s.world.Attach(e, &core.PurseC{Coins: 100, Debts: map[string]float64{}})
	core.Settle(s.world.Get(core.Place(e).Settlement)).Tavern = true
	sim.GoTo(s.world, e, "tavern")

	s.applyIntent(cid, `{"v":1,"op":"card_game","action":"start","reqid":"w1"}`)
	if g := sim.Ochko(e); g == nil || g.Done {
		t.Fatalf("после start партия должна быть активной: %#v", g)
	}

	// "вылетел" и переподключился — сервер при reconnect просто отдаёт то же состояние, тестируем
	// это отдельно (TestOchkoPersistsAcrossWorldSaveLoad); здесь — что он реально застрял до leave.
	if ok, _, _ := s.resultOf(cid, `{"v":1,"op":"card_game","action":"start","reqid":"w2"}`, "w2"); ok {
		t.Fatal("до leave новая партия не должна стартовать (застрявшее состояние)")
	}

	if ok, why, _ := s.resultOf(cid, `{"v":1,"op":"card_game","action":"leave","reqid":"w3"}`, "w3"); !ok {
		t.Fatalf("leave должен сработать: %s", why)
	}
	if g := sim.Ochko(e); !g.Done || g.Result != "left" {
		t.Fatalf("после leave партия должна быть Done/left: %#v", g)
	}

	if ok, why, _ := s.resultOf(cid, `{"v":1,"op":"card_game","action":"start","reqid":"w4"}`, "w4"); !ok {
		t.Fatalf("после leave новая партия должна стартовать: %s", why)
	}
}

// resultOf — послать intent и вернуть его me.result (ok, reason) по reqid, как клиент бы увидел
// через me.result в кадре.
func (s *Server) resultOf(cid core.ID, raw, reqid string) (bool, string, map[string]any) {
	s.applyIntent(cid, raw)
	var frame map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
		return false, err.Error(), nil
	}
	me, _ := frame["me"].(map[string]any)
	result, _ := me["result"].(map[string]any)
	if result == nil || result["reqid"] != reqid {
		return false, "нет коррелируемого me.result", me
	}
	ok, _ := result["ok"].(bool)
	why, _ := result["reason"].(string)
	return ok, why, me
}
