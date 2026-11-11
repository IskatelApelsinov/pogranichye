package sim

import (
	"fmt"

	"pogranichye/core"
)

const OchkoStake = 10.0

func init() {
	core.Register("ochko", func() core.Component { return &OchkoC{} })
}

// OchkoCard is a server-owned card. The deck and dealer's hidden cards are persisted,
// but only OchkoView is allowed to cross the wire.
type OchkoCard struct {
	Code  string `json:"code"`
	Value int    `json:"value"`
}

// OchkoC is the private, reconnect-safe state of one player's game against the innkeeper.
type OchkoC struct {
	Deck   []OchkoCard `json:"deck"`
	Player []OchkoCard `json:"player"`
	Dealer []OchkoCard `json:"dealer"`
	Stake  float64     `json:"stake"`
	Done   bool        `json:"done"`
	Result string      `json:"result"`
	Message string     `json:"message"`
}

func (c *OchkoC) Kind() string { return "ochko" }

func Ochko(e *core.Entity) *OchkoC {
	c, _ := e.Get("ochko").(*OchkoC)
	return c
}

func inTavernSettlement(w *core.World, e *core.Entity) bool {
	if OnRoad(e) {
		return false
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return false
	}
	se := w.Get(pl.Settlement)
	if se == nil {
		return false
	}
	st := core.Settle(se)
	return st != nil && st.Tavern
}

func ochkoTotal(hand []OchkoCard) int {
	total, aces := 0, 0
	for _, card := range hand {
		total += card.Value
		if card.Value == 11 {
			aces++
		}
	}
	for total > 21 && aces > 0 {
		total -= 10
		aces--
	}
	return total
}

func ochkoDeck() []OchkoCard {
	ranks := []struct {
		code string
		value int
	}{
		{"2", 2}, {"3", 3}, {"4", 4}, {"5", 5}, {"6", 6}, {"7", 7},
		{"8", 8}, {"9", 9}, {"10", 10}, {"J", 10}, {"Q", 10}, {"K", 10}, {"A", 11},
	}
	suits := []string{"♠", "♥", "♦", "♣"}
	deck := make([]OchkoCard, 0, 52)
	for _, rank := range ranks {
		for _, suit := range suits {
			deck = append(deck, OchkoCard{Code: rank.code + suit, Value: rank.value})
		}
	}
	return deck
}

// StartOchko starts a round. shuffle must be supplied by the server; tests inject a fixed order.
// It is called under Server.mu, so purse deduction and game creation are atomic.
func StartOchko(w *core.World, e *core.Entity, reqid string, shuffle func([]OchkoCard) error) (bool, string) {
	if e == nil {
		return false, "нет тела"
	}
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if g := Ochko(e); g != nil && !g.Done {
		return false, "ты уже играешь партию"
	}
	if !atTavern(e) {
		return false, "за карты садятся у карточного стола в корчме"
	}
	if !inTavernSettlement(w, e) {
		return false, "здесь нет корчмы"
	}
	v := core.Vitals(e)
	if v == nil || v.Health <= 0 {
		return false, "нельзя играть в таком состоянии"
	}
	pu := core.Purse(e)
	if pu == nil || pu.Coins < OchkoStake {
		return false, fmt.Sprintf("нужно %.0f монет на кон", OchkoStake)
	}
	if shuffle == nil {
		return false, "не удалось перетасовать колоду"
	}
	deck := ochkoDeck()
	if err := shuffle(deck); err != nil {
		return false, "не удалось перетасовать колоду"
	}
	// The order is fixed before any money changes. Deal from the end, like the legacy game.
	g := &OchkoC{Deck: deck, Stake: OchkoStake, Message: "Твой ход: ещё карту или хватит?"}
	g.Player = append(g.Player, g.Deal(), g.Deal())
	g.Dealer = append(g.Dealer, g.Deal(), g.Deal())
	pu.Coins -= OchkoStake
	if old := Ochko(e); old != nil {
		*old = *g
	} else {
		w.Attach(e, g)
	}
	if ochkoTotal(g.Player) == 21 {
		finishOchko(g, pu, true)
	}
	pc.markDone(reqid)
	return true, ""
}

// Deal removes the top card from the server-owned deck.
func (g *OchkoC) Deal() OchkoCard {
	i := len(g.Deck) - 1
	card := g.Deck[i]
	g.Deck = g.Deck[:i]
	return card
}

func finishOchko(g *OchkoC, pu *core.PurseC, natural bool) {
	if g == nil || g.Done {
		return
	}
	g.Done = true
	playerTotal := ochkoTotal(g.Player)
	if natural {
		pu.Coins += g.Stake * 2.5
		g.Result = "natural"
		g.Message = fmt.Sprintf("Очко на двух картах! Выплата %.0f монет.", g.Stake*2.5)
		return
	}
	if playerTotal > 21 {
		g.Result = "bust"
		g.Message = fmt.Sprintf("Перебор (%d). Ставка ушла заведению.", playerTotal)
		return
	}
	dealerTotal := ochkoTotal(g.Dealer)
	for dealerTotal < 17 && len(g.Deck) > 0 {
		g.Dealer = append(g.Dealer, g.Deal())
		dealerTotal = ochkoTotal(g.Dealer)
	}
	switch {
	case dealerTotal > 21 || playerTotal > dealerTotal:
		pu.Coins += g.Stake * 2
		g.Result = "win"
		g.Message = fmt.Sprintf("Ты %d, корчмарь перебрал. Выигрыш %.0f монет.", playerTotal, g.Stake*2)
	case playerTotal == dealerTotal:
		pu.Coins += g.Stake
		g.Result = "push"
		g.Message = fmt.Sprintf("Ничья (%d). Ставка возвращена.", playerTotal)
	default:
		g.Result = "loss"
		g.Message = fmt.Sprintf("Ты %d, корчмарь %d. Ставка ушла заведению.", playerTotal, dealerTotal)
	}
}

// HitOchko draws exactly one card. Duplicate reqids never draw or settle twice.
func HitOchko(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return changeOchko(w, e, reqid, func(g *OchkoC, pu *core.PurseC) (bool, string) {
		if len(g.Deck) == 0 {
			return false, "колода закончилась"
		}
		g.Player = append(g.Player, g.Deal())
		if ochkoTotal(g.Player) > 21 {
			finishOchko(g, pu, false)
		} else {
			g.Message = "Ещё карту или хватит?"
		}
		return true, ""
	})
}

// StandOchko reveals the dealer's hand and settles the stake exactly once.
func StandOchko(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return changeOchko(w, e, reqid, func(g *OchkoC, pu *core.PurseC) (bool, string) {
		finishOchko(g, pu, false)
		return true, ""
	})
}

func changeOchko(w *core.World, e *core.Entity, reqid string, change func(*OchkoC, *core.PurseC) (bool, string)) (bool, string) {
	if e == nil {
		return false, "нет тела"
	}
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	g, pu := Ochko(e), core.Purse(e)
	if g == nil || g.Done {
		return false, "партия не идёт — начни заново"
	}
	if !atTavern(e) {
		return false, "вернись к карточному столу в корчме"
	}
	if !inTavernSettlement(w, e) {
		return false, "здесь нет корчмы"
	}
	if pu == nil {
		return false, "нет кошеля"
	}
	ok, why := change(g, pu)
	if ok {
		pc.markDone(reqid)
	}
	return ok, why
}

// OchkoView exposes only the player's own hand and one dealer up-card until settlement.
func OchkoView(e *core.Entity) any {
	g := Ochko(e)
	if g == nil {
		return nil
	}
	cards := func(hand []OchkoCard) []map[string]any {
		out := make([]map[string]any, 0, len(hand))
		for _, card := range hand {
			out = append(out, map[string]any{"c": card.Code, "v": card.Value})
		}
		return out
	}
	dealer := g.Dealer
	if !g.Done && len(dealer) > 1 {
		dealer = dealer[:1]
	}
	var dealerTotal any
	if g.Done {
		dealerTotal = ochkoTotal(dealer)
	}
	hidden := 0
	if !g.Done {
		hidden = len(g.Dealer) - len(dealer)
	}
	return map[string]any{
		"game": "ochko", "phase": map[bool]string{true: "done", false: "playing"}[g.Done],
		"stake": g.Stake, "done": g.Done, "result": g.Result, "message": g.Message,
		"player": cards(g.Player), "ptotal": ochkoTotal(g.Player),
		"dealer": cards(dealer), "dtotal": dealerTotal, "hidden": hidden,
	}
}
