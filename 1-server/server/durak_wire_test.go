package server

import (
	"encoding/json"
	"fmt"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

func TestDurakIntentAndPrivateFrame(t *testing.T) {
	s := NewStarter("durak-wire", "", []byte("x"), "")
	s.durakShuffle = func(cards []sim.DurakCard) error {
		set := map[int]sim.DurakCard{
			0: {Rank: 6, Suit: "♥", Code: "6♥"},
			1: {Rank: 6, Suit: "♦", Code: "6♦"},
			2: {Rank: 7, Suit: "♥", Code: "7♥"},
			3: {Rank: 7, Suit: "♦", Code: "7♦"},
			4: {Rank: 8, Suit: "♣", Code: "8♣"},
			5: {Rank: 8, Suit: "♦", Code: "8♦"},
			6: {Rank: 9, Suit: "♣", Code: "9♣"},
			7: {Rank: 9, Suit: "♦", Code: "9♦"},
			8: {Rank: 10, Suit: "♣", Code: "10♣"},
			9: {Rank: 10, Suit: "♦", Code: "10♦"},
			10: {Rank: 11, Suit: "♣", Code: "В♣"},
			11: {Rank: 11, Suit: "♦", Code: "В♦"},
			35: {Rank: 14, Suit: "♠", Code: "Т♠"},
		}
		for i, card := range set {
			cards[i] = card
		}
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
		t.Fatal("нет тела для дурака")
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

	send := func(action string, index int, reqid string) map[string]any {
		t.Helper()
		msg := `{"v":1,"op":"card_game","game":"durak","action":"` + action + `","index":` + fmt.Sprint(index) + `, "reqid":"` + reqid + `"}`
		s.applyIntent(cid, msg)
		var frame map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
			t.Fatal(err)
		}
		me := frame["me"].(map[string]any)
		result, _ := me["result"].(map[string]any)
		if result == nil || result["reqid"] != reqid {
			t.Fatalf("нет me.result для %s: %#v", reqid, result)
		}
		return me
	}

	me := send("start", -1, "durak-start")
	game, ok := me["card_game"].(map[string]any)
	if !ok || game["game"] != "durak" || game["phase"] != "attack" {
		t.Fatalf("партия дурака не появилась: %#v", me["card_game"])
	}
	if len(game["player"].([]any)) != 6 || game["dealer_cards"] != float64(6) {
		t.Fatalf("раздача должна дать по шесть карт: %#v", game)
	}
	if _, leaked := game["dealer"]; leaked {
		t.Fatal("закрытая рука корчмаря не должна попасть в кадр")
	}
	if core.Purse(e).Coins != 90 {
		t.Fatalf("ставка не списана: %.2f", core.Purse(e).Coins)
	}

	me = send("attack", 0, "durak-attack")
	game = me["card_game"].(map[string]any)
	if game["phase"] != "attack" || game["attacker"] != "player" || len(game["table"].([]any)) != 0 {
		t.Fatalf("корчмарь должен взять непобиваемую карту, сохранив игроку заход: %#v", game)
	}
	if game["dealer_cards"] != float64(7) || len(game["player"].([]any)) != 6 {
		t.Fatalf("карты после взятия и добора неверны: %#v", game)
	}
	me = send("attack", 0, "durak-attack")
	game = me["card_game"].(map[string]any)
	if game["dealer_cards"] != float64(7) || len(game["player"].([]any)) != 6 {
		t.Fatal("повтор reqid не должен повторять ход")
	}
}
