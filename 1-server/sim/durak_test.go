package sim

import (
	"testing"

	"pogranichye/core"
)

func fixedDurakDeck(set map[int]DurakCard) func([]DurakCard) error {
	return func(cards []DurakCard) error {
		for i, card := range set {
			cards[i] = card
		}
		return nil
	}
}

func quietDurakDeck() func([]DurakCard) error {
	set := map[int]DurakCard{
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
	return fixedDurakDeck(set)
}

func TestDurakStartHidesDealerHandAndPlayerAttack(t *testing.T) {
	w := core.NewWorld("durak-start")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartDurak(w, e, "start", quietDurakDeck()); !ok {
		t.Fatalf("start: %s", why)
	}
	g := Durak(e)
	if g == nil || g.Done || g.Attacker != "player" || len(g.Player) != 6 || len(g.Dealer) != 6 || len(g.Deck) != 24 {
		t.Fatalf("unexpected opening state: %#v", g)
	}
	view := DurakView(e).(map[string]any)
	if view["dealer_cards"] != 6 || len(view["player"].([]map[string]any)) != 6 {
		t.Fatalf("private hand view is wrong: %#v", view)
	}
	if _, leaked := view["dealer"]; leaked {
		t.Fatal("dealer hand must not be exposed")
	}
	if core.Purse(e).Coins != 90 {
		t.Fatalf("stake not deducted: %.2f", core.Purse(e).Coins)
	}
	if ok, why := ActDurak(w, e, "attack", 0, "attack-1"); !ok {
		t.Fatalf("attack: %s", why)
	}
	if g.Attacker != "player" || g.Phase != "attack" || len(g.Table) != 0 || len(g.Dealer) != 7 || len(g.Player) != 6 {
		t.Fatalf("dealer should take an unanswerable card and player should retain attack: %#v", g)
	}
	playerCount := len(g.Player)
	if ok, why := ActDurak(w, e, "attack", 0, "attack-1"); !ok || len(g.Player) != playerCount {
		t.Fatalf("duplicate reqid must not repeat attack: ok=%t why=%q", ok, why)
	}
}

func TestDurakTrumpDefenseAndThrowInRankRule(t *testing.T) {
	w := core.NewWorld("durak-rules")
	e, _ := drinker(w, 100, 0)
	set := map[int]DurakCard{
		0: {Rank: 6, Suit: "♥", Code: "6♥"},
		1: {Rank: 7, Suit: "♥", Code: "7♥"},
		2: {Rank: 8, Suit: "♣", Code: "8♣"},
		3: {Rank: 6, Suit: "♦", Code: "6♦"},
		4: {Rank: 9, Suit: "♦", Code: "9♦"},
		5: {Rank: 7, Suit: "♦", Code: "7♦"},
		6: {Rank: 10, Suit: "♦", Code: "10♦"},
		7: {Rank: 8, Suit: "♦", Code: "8♦"},
		8: {Rank: 11, Suit: "♦", Code: "В♦"},
		9: {Rank: 12, Suit: "♦", Code: "Д♦"},
		10: {Rank: 13, Suit: "♦", Code: "К♦"},
		11: {Rank: 14, Suit: "♦", Code: "Т♦"},
		35: {Rank: 14, Suit: "♠", Code: "Т♠"},
	}
	if ok, why := StartDurak(w, e, "start", fixedDurakDeck(set)); !ok {
		t.Fatal(why)
	}
	g := Durak(e)
	if ok, why := ActDurak(w, e, "attack", 0, "a1"); !ok {
		t.Fatal(why)
	}
	if len(g.Table) != 1 || g.Table[0].Defence == nil || g.Table[0].Defence.Code != "7♥" {
		t.Fatalf("dealer should beat 6♥ with 7♥: %#v", g.Table)
	}
	before := len(g.Player)
	if ok, _ := ActDurak(w, e, "attack", 0, "bad-rank"); ok || len(g.Player) != before || len(g.Table) != 1 {
		t.Fatal("throw-in with a rank absent from the table must be rejected")
	}
	if ok, why := ActDurak(w, e, "done", -1, "done"); !ok {
		t.Fatalf("all cards covered; attacker should be able to say done: %s", why)
	}
	if g.Attacker != "dealer" || g.Phase != "defend" || len(g.Table) != 1 {
		t.Fatalf("dealer should take the next attack after a defended bout: %#v", g)
	}
}

func TestDurakSettlementWinPushAndLoss(t *testing.T) {
	t.Run("win", func(t *testing.T) {
		w := core.NewWorld("durak-win")
		e, _ := drinker(w, 100, 0)
		if ok, why := StartDurak(w, e, "start", quietDurakDeck()); !ok {
			t.Fatal(why)
		}
		g := Durak(e)
		g.Deck = nil
		g.Player = []DurakCard{{Rank: 6, Suit: "♥", Code: "6♥"}}
		g.Dealer = nil
		g.Table = nil
		g.Attacker, g.Phase = "player", "attack"
		if ok, why := ActDurak(w, e, "attack", 0, "finish"); !ok {
			t.Fatal(why)
		}
		if !g.Done || g.Loser != "dealer" || g.Result != "win" || !g.Settled || core.Purse(e).Coins != 110 {
			t.Fatalf("player win should return 2x stake: %#v coins=%.2f", g, core.Purse(e).Coins)
		}
		if ok, _ := ActDurak(w, e, "attack", 0, "finish"); !ok || core.Purse(e).Coins != 110 {
			t.Fatal("duplicate finishing action must not pay twice")
		}
	})
	t.Run("push", func(t *testing.T) {
		w := core.NewWorld("durak-push")
		e, _ := drinker(w, 100, 0)
		if ok, why := StartDurak(w, e, "start", quietDurakDeck()); !ok {
			t.Fatal(why)
		}
		g := Durak(e)
		g.Deck = nil
		g.Player = []DurakCard{{Rank: 6, Suit: "♥", Code: "6♥"}}
		g.Dealer = []DurakCard{{Rank: 7, Suit: "♥", Code: "7♥"}}
		g.Table = nil
		g.Attacker, g.Phase = "player", "attack"
		if ok, why := ActDurak(w, e, "attack", 0, "a"); !ok {
			t.Fatal(why)
		}
		if ok, why := ActDurak(w, e, "done", -1, "d"); !ok {
			t.Fatal(why)
		}
		if !g.Done || g.Loser != "" || g.Result != "push" || core.Purse(e).Coins != 100 {
			t.Fatalf("both players out should push: %#v coins=%.2f", g, core.Purse(e).Coins)
		}
	})
	t.Run("loss", func(t *testing.T) {
		w := core.NewWorld("durak-loss")
		e, _ := drinker(w, 100, 0)
		if ok, why := StartDurak(w, e, "start", quietDurakDeck()); !ok {
			t.Fatal(why)
		}
		g := Durak(e)
		g.Deck = nil
		g.Player = []DurakCard{{Rank: 6, Suit: "♥", Code: "6♥"}}
		g.Dealer = nil
		g.Table = []DurakPair{{Attack: DurakCard{Rank: 7, Suit: "♥", Code: "7♥"}}}
		g.Attacker, g.Phase = "dealer", "defend"
		if ok, why := ActDurak(w, e, "take", -1, "take"); !ok {
			t.Fatal(why)
		}
		if !g.Done || g.Loser != "player" || g.Result != "loss" || core.Purse(e).Coins != 90 {
			t.Fatalf("player who remains with cards must lose stake: %#v coins=%.2f", g, core.Purse(e).Coins)
		}
	})
}
