package sim

import (
	"fmt"
	"sort"

	"pogranichye/core"
)

const DurakStake = 10.0

func init() {
	core.Register("durak", func() core.Component { return &DurakC{} })
}

// DurakCard is private server state; only the player's own cards and the open table are exposed.
type DurakCard struct {
	Rank int    `json:"rank"`
	Suit string `json:"suit"`
	Code string `json:"code"`
}

type DurakPair struct {
	Attack  DurakCard  `json:"attack"`
	Defence *DurakCard `json:"defence,omitempty"`
}

// DurakC stores the complete 1v1 podkidnoy game. Dealer hand and deck never enter the frame.
type DurakC struct {
	Deck     []DurakCard `json:"deck"`
	Player   []DurakCard `json:"player"`
	Dealer   []DurakCard `json:"dealer"`
	Table    []DurakPair `json:"table"`
	Trump    string      `json:"trump"`
	Attacker string      `json:"attacker"` // player | dealer
	Phase    string      `json:"phase"`    // attack | defend | over
	Loser    string      `json:"loser,omitempty"`
	Stake    float64     `json:"stake"`
	Done     bool        `json:"done"`
	Settled  bool        `json:"settled"`
	Result   string      `json:"result,omitempty"`
	Message  string      `json:"message"`
}

func (c *DurakC) Kind() string { return "durak" }

func Durak(e *core.Entity) *DurakC {
	c, _ := e.Get("durak").(*DurakC)
	return c
}

func durakDeck() []DurakCard {
	deck := make([]DurakCard, 0, 36)
	for rank := 6; rank <= 14; rank++ {
		label := fmt.Sprint(rank)
		switch rank {
		case 11:
			label = "В"
		case 12:
			label = "Д"
		case 13:
			label = "К"
		case 14:
			label = "Т"
		}
		for _, suit := range []string{"♠", "♥", "♦", "♣"} {
			deck = append(deck, DurakCard{Rank: rank, Suit: suit, Code: label + suit})
		}
	}
	return deck
}

func (g *DurakC) drawTop() DurakCard {
	card := g.Deck[0]
	g.Deck = g.Deck[1:]
	return card
}

func (g *DurakC) defender() string {
	if g.Attacker == "player" {
		return "dealer"
	}
	return "player"
}

func (g *DurakC) hand(seat string) *[]DurakCard {
	if seat == "player" {
		return &g.Player
	}
	return &g.Dealer
}

func (g *DurakC) sortHands() {
	less := func(cards []DurakCard) {
		sort.Slice(cards, func(i, j int) bool {
			it, jt := cards[i].Suit == g.Trump, cards[j].Suit == g.Trump
			if it != jt {
				return !it
			}
			return cards[i].Rank < cards[j].Rank
		})
	}
	less(g.Player)
	less(g.Dealer)
}

func (g *DurakC) firstAttacker() string {
	best := 100
	who := "player"
	for _, seat := range []string{"player", "dealer"} {
		for _, card := range *g.hand(seat) {
			if card.Suit == g.Trump && card.Rank < best {
				best, who = card.Rank, seat
			}
		}
	}
	return who
}

func (g *DurakC) beats(defence, attack DurakCard) bool {
	if defence.Suit == attack.Suit {
		return defence.Rank > attack.Rank
	}
	return defence.Suit == g.Trump && attack.Suit != g.Trump
}

func (g *DurakC) tableRanks() map[int]bool {
	ranks := map[int]bool{}
	for _, pair := range g.Table {
		ranks[pair.Attack.Rank] = true
		if pair.Defence != nil {
			ranks[pair.Defence.Rank] = true
		}
	}
	return ranks
}

func (g *DurakC) undefended() int {
	for i := range g.Table {
		if g.Table[i].Defence == nil {
			return i
		}
	}
	return -1
}

func (g *DurakC) drawToSix() {
	for _, seat := range []string{g.Attacker, g.defender()} {
		hand := g.hand(seat)
		for len(*hand) < 6 && len(g.Deck) > 0 {
			*hand = append(*hand, g.drawTop())
		}
	}
	g.sortHands()
}

func (g *DurakC) checkOver() {
	if len(g.Deck) > 0 {
		return
	}
	playerEmpty, dealerEmpty := len(g.Player) == 0, len(g.Dealer) == 0
	if !playerEmpty && !dealerEmpty {
		return
	}
	g.Done, g.Phase = true, "over"
	switch {
	case playerEmpty && dealerEmpty:
		g.Loser, g.Message = "", "Ничья — оба вышли."
	case playerEmpty:
		g.Loser, g.Message = "dealer", "Ты вышел первым — корчмарь в дураках!"
	default:
		g.Loser, g.Message = "player", "Ты остался с картами — ты дурак."
	}
}

func (g *DurakC) endBout(defended bool) {
	if defended {
		g.Table = nil
		g.Attacker = g.defender()
	} else {
		defender := g.defender()
		hand := g.hand(defender)
		for _, pair := range g.Table {
			*hand = append(*hand, pair.Attack)
			if pair.Defence != nil {
				*hand = append(*hand, *pair.Defence)
			}
		}
		g.Table = nil
	}
	g.drawToSix()
	g.Phase = "attack"
	g.checkOver()
}

func (g *DurakC) attackLimit() int {
	limit := 6
	if n := len(*g.hand(g.defender())) + 1; n < limit {
		limit = n
	}
	return limit
}

// Act applies one player action and then runs the dealer until it must wait for the player.
func (g *DurakC) Act(action string, index int) (bool, string) {
	if g == nil || g.Done {
		return false, "партия уже завершена"
	}
	if g.Attacker == "player" {
		if g.Phase != "attack" {
			return false, "сейчас не твой ход"
		}
		switch action {
		case "attack":
			if index < 0 || index >= len(g.Player) {
				return false, "выбери карту из руки"
			}
			if len(g.Table) >= g.attackLimit() {
				return false, "больше нельзя подкинуть"
			}
			card := g.Player[index]
			if len(g.Table) > 0 && !g.tableRanks()[card.Rank] {
				return false, "подкинуть можно только карту ранга, что уже на столе"
			}
			g.Player = append(g.Player[:index], g.Player[index+1:]...)
			g.Table = append(g.Table, DurakPair{Attack: card})
			g.Phase = "defend"
		case "done":
			if len(g.Table) == 0 || g.undefended() >= 0 {
				return false, "сначала нужно отбиться от всех карт"
			}
			g.endBout(true)
			g.Message = "Бито — теперь твой соперник заходит."
		default:
			return false, "неизвестный ход атакующего"
		}
	} else {
		if g.Phase != "defend" {
			return false, "сейчас не твой ход"
		}
		switch action {
		case "take":
			g.endBout(false)
			g.Message = "Ты взял карты — корчмарь продолжает заход."
		case "defend":
			pairIndex := g.undefended()
			if pairIndex < 0 {
				return false, "нет карты, от которой нужно отбиться"
			}
			if index < 0 || index >= len(g.Player) {
				return false, "выбери карту из руки"
			}
			card := g.Player[index]
			if !g.beats(card, g.Table[pairIndex].Attack) {
				return false, "этой картой не побить"
			}
			g.Player = append(g.Player[:index], g.Player[index+1:]...)
			g.Table[pairIndex].Defence = &card
			g.Phase = "attack"
		default:
			return false, "неизвестный ход защитника"
		}
	}
	if !g.Done {
		g.botTurn()
	}
	return true, ""
}

func (g *DurakC) botTurn() {
	for guard := 0; guard < 40 && !g.Done; guard++ {
		if g.Attacker == "dealer" && g.Phase == "attack" {
			if !g.botAttack() {
				return
			}
			continue
		}
		if g.defender() == "dealer" && g.Phase == "defend" {
			if !g.botDefend() {
				return
			}
			continue
		}
		return
	}
}

func (g *DurakC) botAttack() bool {
	hand := g.Dealer
	if len(g.Table) == 0 {
		if len(hand) == 0 {
			g.Done, g.Phase = true, "over"
			g.checkOver()
			return false
		}
		pick := 0
		for i, card := range hand {
			if card.Suit != g.Trump && (hand[pick].Suit == g.Trump || card.Rank < hand[pick].Rank) {
				pick = i
			} else if hand[pick].Suit == g.Trump && card.Rank < hand[pick].Rank {
				pick = i
			}
		}
		card := hand[pick]
		g.Dealer = append(g.Dealer[:pick], g.Dealer[pick+1:]...)
		g.Table = append(g.Table, DurakPair{Attack: card})
		g.Phase = "defend"
		g.Message = "Корчмарь зашёл: " + card.Code
		return true
	}
	if g.undefended() < 0 && len(g.Table) < g.attackLimit() {
		ranks := g.tableRanks()
		pick := -1
		for i, card := range hand {
			if ranks[card.Rank] && card.Suit != g.Trump {
				pick = i
				break
			}
		}
		if pick < 0 {
			for i, card := range hand {
				if ranks[card.Rank] {
					pick = i
					break
				}
			}
		}
		if pick >= 0 {
			card := hand[pick]
			g.Dealer = append(g.Dealer[:pick], g.Dealer[pick+1:]...)
			g.Table = append(g.Table, DurakPair{Attack: card})
			g.Phase = "defend"
			g.Message = "Корчмарь подкинул: " + card.Code
			return true
		}
	}
	if g.undefended() < 0 {
		g.endBout(true)
		g.Message = "Корчмарь говорит «бито»."
		return true
	}
	return false
}

func (g *DurakC) botDefend() bool {
	pairIndex := g.undefended()
	if pairIndex < 0 {
		g.Phase = "attack"
		return true
	}
	pair := g.Table[pairIndex]
	pick := -1
	for i, card := range g.Dealer {
		if g.beats(card, pair.Attack) && (pick < 0 ||
			(g.Dealer[pick].Suit == g.Trump && card.Suit != g.Trump) ||
			((g.Dealer[pick].Suit == g.Trump) == (card.Suit == g.Trump) && card.Rank < g.Dealer[pick].Rank)) {
			pick = i
		}
	}
	if pick >= 0 {
		card := g.Dealer[pick]
		g.Dealer = append(g.Dealer[:pick], g.Dealer[pick+1:]...)
		g.Table[pairIndex].Defence = &card
		g.Phase = "attack"
		g.Message = "Корчмарь отбился: " + card.Code
		return true
	}
	g.endBout(false)
	g.Message = "Корчмарь забрал карты. Твой заход."
	return true
}

func DurakView(e *core.Entity) any {
	g := Durak(e)
	if g == nil {
		return nil
	}
	cards := func(hand []DurakCard) []map[string]any {
		out := make([]map[string]any, 0, len(hand))
		for _, card := range hand {
			out = append(out, map[string]any{"c": card.Code, "rank": card.Rank, "suit": card.Suit})
		}
		return out
	}
	table := make([]map[string]any, 0, len(g.Table))
	for _, pair := range g.Table {
		var defence any
		if pair.Defence != nil {
			defence = map[string]any{"c": pair.Defence.Code, "rank": pair.Defence.Rank, "suit": pair.Defence.Suit}
		}
		table = append(table, map[string]any{
			"a": map[string]any{"c": pair.Attack.Code, "rank": pair.Attack.Rank, "suit": pair.Attack.Suit},
			"d": defence,
		})
	}
	var trumpCard any
	if len(g.Deck) > 0 {
		trumpCard = g.Deck[len(g.Deck)-1].Code
	}
	yourTurn := !g.Done && ((g.Attacker == "player" && g.Phase == "attack") ||
		(g.Attacker == "dealer" && g.Phase == "defend"))
	role := "defend"
	if g.Attacker == "player" {
		role = "attack"
	}
	return map[string]any{
		"game": "durak", "phase": g.Phase, "trump": g.Trump, "trump_card": trumpCard,
		"deck": len(g.Deck), "player": cards(g.Player), "dealer_cards": len(g.Dealer),
		"table": table, "attacker": g.Attacker, "role": role, "your_turn": yourTurn,
		"done": g.Done, "loser": g.Loser, "result": g.Result, "message": g.Message,
		"stake": g.Stake,
	}
}

func StartDurak(w *core.World, e *core.Entity, reqid string, shuffle func([]DurakCard) error) (bool, string) {
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
	if g := Durak(e); g != nil && !g.Done {
		return false, "ты уже играешь партию"
	}
	if g := Ochko(e); g != nil && !g.Done {
		return false, "сначала закончи партию в очко"
	}
	if !atTavern(e) {
		return false, "за карты садятся в корчме"
	}
	if !inTavernSettlement(w, e) {
		return false, "здесь нет корчмы"
	}
	v := core.Vitals(e)
	if v == nil || v.Health <= 0 {
		return false, "нельзя играть в таком состоянии"
	}
	pu := core.Purse(e)
	if pu == nil || pu.Coins < DurakStake {
		return false, fmt.Sprintf("нужно %.0f монет на кон", DurakStake)
	}
	if shuffle == nil {
		return false, "не удалось перетасовать колоду"
	}
	deck := durakDeck()
	if err := shuffle(deck); err != nil {
		return false, "не удалось перетасовать колоду"
	}
	g := &DurakC{Deck: deck, Trump: deck[len(deck)-1].Suit, Stake: DurakStake, Phase: "attack"}
	for i := 0; i < 6; i++ {
		g.Player = append(g.Player, g.drawTop())
		g.Dealer = append(g.Dealer, g.drawTop())
	}
	g.sortHands()
	g.Attacker = g.firstAttacker()
	if g.Attacker == "player" {
		g.Message = "Твой заход."
	} else {
		g.Message = "Корчмарь заходит."
	}
	pu.Coins -= DurakStake
	if old := Durak(e); old != nil {
		*old = *g
	} else {
		w.Attach(e, g)
	}
	pc.CardGame = "durak"
	if g.Attacker == "dealer" {
		g.botTurn()
	}
	pc.markDone(reqid)
	return true, ""
}

func ActDurak(w *core.World, e *core.Entity, action string, index int, reqid string) (bool, string) {
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
	g, pu := Durak(e), core.Purse(e)
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
	ok, why := g.Act(action, index)
	if !ok {
		return false, why
	}
	if g.Done && !g.Settled {
		g.Settled = true
		switch g.Loser {
		case "dealer":
			pu.Coins += g.Stake * 2
			g.Result = "win"
			g.Message += fmt.Sprintf(" Выигрыш %.0f монет.", g.Stake*2)
		case "":
			pu.Coins += g.Stake
			g.Result = "push"
			g.Message += fmt.Sprintf(" Ставка %.0f монет возвращена.", g.Stake)
		default:
			g.Result = "loss"
			g.Message += fmt.Sprintf(" Ставка %.0f монет ушла заведению.", g.Stake)
		}
	}
	pc.markDone(reqid)
	return true, ""
}

 // CardGameView exposes the last selected personal table, without exposing any server-only state.
func CardGameView(e *core.Entity) any {
	if pc := Player(e); pc != nil && pc.CardGame == "durak" {
		return DurakView(e)
	}
	return OchkoView(e)
}
