package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// #40: нельзя говорить с жителем из ДРУГОГО поселения, даже если локальные координаты совпали.
func TestTalkCrossSettlementBlocked(t *testing.T) {
	w := core.NewWorld("t")
	tx, ty := poiByKind("tavern")
	p := playerAt(w, tx, ty, 10, 100)
	other := villagerAt(w, "smith", tx, ty, 200) // те же координаты, другое поселение
	if ok, _ := StartTalk(w, p, other.ID); ok {
		t.Fatal("разговор между поселениями недопустим")
	}
}

// #40: повтор одного reqid у drink не списывает дважды.
func TestDrinkReqidDedup(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Tavern: true, Store: map[string]float64{BeerGood: 10}})
	tx, ty := poiByKind("tavern")
	p := playerAt(w, tx, ty, 10, se.ID)
	npc := villagerAt(w, "fisher", tx+1, ty, se.ID)
	StartTalk(w, p, npc.ID)
	Say(w, p, "greet", "g1")
	coins := core.Purse(p).Coins
	Say(w, p, "drink", "d1")
	Say(w, p, "drink", "d1") // повтор того же reqid
	spent := coins - core.Purse(p).Coins
	if spent > BeerPrice*2+1e-6 {
		t.Fatalf("повтор drink reqid списал дважды: потрачено %.2f", spent)
	}
}

// co-drink: оба игрока должны быть в одном поселении.
func TestCoDrinkSameSettlement(t *testing.T) {
	w := core.NewWorld("t")
	tx, ty := poiByKind("tavern")
	a := playerAt(w, tx, ty, 10, 100)
	b := playerAt(w, tx+1, ty, 10, 200) // другое поселение, те же координаты
	if ok, _ := InviteCoDrink(w, a, b.ID, func(id core.ID) bool { return true }); ok {
		t.Fatal("co-drink между поселениями недопустим")
	}
}

// trade: повтор reqid не повторяет сделку.
func TestTradeReqidDedup(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Store: map[string]float64{"bread": 100}})
	p := playerAt(w, 0, 0, 100, se.ID)
	Trade(w, p, "bread", "buy", 1, "tr1")
	Trade(w, p, "bread", "buy", 1, "tr1") // повтор того же reqid
	got := 0.0
	if s := econ.Stash(p); s != nil {
		got = s.Goods["bread"]
	}
	if got > 1.0001 {
		t.Fatalf("повтор trade reqid купил дважды: bread=%.2f", got)
	}
}

// навграф: до всех ключевых POI есть маршрут от колодца, и он не пересекает здания/реку вне моста.
func TestNavReachAllPOI(t *testing.T) {
	start := navIdx["well"]
	for _, key := range []string{"tavern-gate", "smithy-gate", "granary-gate", "woods", "garden", "mine", "fishing-bank", "south-road"} {
		g, ok := navIdx[key]
		if !ok {
			t.Fatalf("нет узла %s", key)
		}
		if navRoute(start, g) == nil {
			t.Fatalf("нет маршрута well -> %s", key)
		}
	}
	// рёбра не пересекают препятствия (грубая выборка середин)
	for _, e := range navEdgeNames {
		a, b := navNodes[navIdx[e[0]]], navNodes[navIdx[e[1]]]
		for s := 0.0; s <= 1.0; s += 0.1 {
			x, y := a.x+(b.x-a.x)*s, a.y+(b.y-a.y)*s
			if Blocked(x, y) {
				t.Fatalf("ребро %s-%s проходит через препятствие в (%.0f,%.0f)", e[0], e[1], x, y)
			}
		}
	}
}

