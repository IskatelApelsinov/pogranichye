package sim

import (
	"testing"

	"pogranichye/core"
)

func fixedOchkoDeck(set map[int]OchkoCard) func([]OchkoCard) error {
	return func(cards []OchkoCard) error {
		for i, card := range set {
			cards[i] = card
		}
		return nil
	}
}

func TestOchkoNaturalAndStartIdempotency(t *testing.T) {
	w := core.NewWorld("ochko-natural")
	e, _ := drinker(w, 100, 0)
	shuffle := fixedOchkoDeck(map[int]OchkoCard{
		51: {Code: "A♠", Value: 11}, 50: {Code: "K♠", Value: 10},
		49: {Code: "5♣", Value: 5}, 48: {Code: "7♦", Value: 7},
	})
	if ok, why := StartOchko(w, e, "start-1", shuffle); !ok {
		t.Fatalf("start: %s", why)
	}
	g := Ochko(e)
	if g == nil || !g.Done || g.Result != "natural" {
		t.Fatalf("natural 21 must finish immediately: %#v", g)
	}
	if got := core.Purse(e).Coins; got != 115 {
		t.Fatalf("legacy natural payout should return 2.5x stake: %.2f", got)
	}
	if ok, why := StartOchko(w, e, "start-1", shuffle); !ok || core.Purse(e).Coins != 115 {
		t.Fatalf("duplicate start must not charge again: ok=%t why=%q coins=%.2f", ok, why, core.Purse(e).Coins)
	}
}

func TestOchkoBustAndDuplicateHit(t *testing.T) {
	w := core.NewWorld("ochko-bust")
	e, _ := drinker(w, 100, 0)
	shuffle := fixedOchkoDeck(map[int]OchkoCard{
		51: {Code: "10♠", Value: 10}, 50: {Code: "9♠", Value: 9},
		49: {Code: "5♣", Value: 5}, 48: {Code: "6♦", Value: 6},
		47: {Code: "5♥", Value: 5},
	})
	if ok, why := StartOchko(w, e, "start", shuffle); !ok {
		t.Fatal(why)
	}
	if ok, why := HitOchko(w, e, "hit-1"); !ok {
		t.Fatal(why)
	}
	g := Ochko(e)
	if !g.Done || g.Result != "bust" || ochkoTotal(g.Player) != 24 {
		t.Fatalf("expected bust at 24: %#v total=%d", g, ochkoTotal(g.Player))
	}
	if got := core.Purse(e).Coins; got != 90 {
		t.Fatalf("bust should lose only the stake: %.2f", got)
	}
	n := len(g.Player)
	if ok, why := HitOchko(w, e, "hit-1"); !ok || len(g.Player) != n || core.Purse(e).Coins != 90 {
		t.Fatalf("duplicate hit changed game: ok=%t why=%q", ok, why)
	}
}

func TestOchkoStandPushAndPrivateDealerCard(t *testing.T) {
	w := core.NewWorld("ochko-push")
	e, _ := drinker(w, 100, 0)
	shuffle := fixedOchkoDeck(map[int]OchkoCard{
		51: {Code: "10♠", Value: 10}, 50: {Code: "7♠", Value: 7},
		49: {Code: "10♣", Value: 10}, 48: {Code: "7♦", Value: 7},
	})
	if ok, why := StartOchko(w, e, "start", shuffle); !ok {
		t.Fatal(why)
	}
	view := OchkoView(e).(map[string]any)
	if len(view["dealer"].([]map[string]any)) != 1 || view["hidden"] != 1 || view["dtotal"] != nil {
		t.Fatalf("dealer's hole card leaked: %#v", view)
	}
	if ok, why := StandOchko(w, e, "stand"); !ok {
		t.Fatal(why)
	}
	g := Ochko(e)
	if !g.Done || g.Result != "push" || core.Purse(e).Coins != 100 {
		t.Fatalf("17 vs 17 must return the stake: game=%#v coins=%.2f", g, core.Purse(e).Coins)
	}
	view = OchkoView(e).(map[string]any)
	if len(view["dealer"].([]map[string]any)) != 2 || view["hidden"] != 0 || view["dtotal"] != 17 {
		t.Fatalf("dealer hand should be revealed after settlement: %#v", view)
	}
}

func TestOchkoRejectsInsufficientFundsAndWrongPlace(t *testing.T) {
	w := core.NewWorld("ochko-reject")
	e, _ := drinker(w, 9, 0)
	shuffle := fixedOchkoDeck(nil)
	if ok, _ := StartOchko(w, e, "poor", shuffle); ok || core.Purse(e).Coins != 9 {
		t.Fatal("insufficient funds must not start or charge")
	}
	core.Purse(e).Coins = 100
	ScenePos(e).X, ScenePos(e).Y = 25, 25
	if ok, _ := StartOchko(w, e, "far", shuffle); ok || core.Purse(e).Coins != 100 {
		t.Fatal("starting away from the tavern must not charge")
	}
}

func TestOchkoRequiresCurrentTavernAndLivingPlayer(t *testing.T) {
	w := core.NewWorld("ochko-presence")
	e, _ := drinker(w, 100, 0)
	shuffle := fixedOchkoDeck(nil)
	other := w.Spawn(&core.SettlementC{Name: "Другое село", Tavern: false})
	original := core.Place(e).Settlement
	core.Place(e).Settlement = other.ID
	if ok, _ := StartOchko(w, e, "wrong-settlement", shuffle); ok || core.Purse(e).Coins != 100 {
		t.Fatal("scene coordinates alone must not allow play in a settlement without a tavern")
	}
	core.Place(e).Settlement = original
	core.Vitals(e).Health = 0
	if ok, _ := StartOchko(w, e, "dead", shuffle); ok || core.Purse(e).Coins != 100 {
		t.Fatal("a dead character must not start a card game")
	}
}
