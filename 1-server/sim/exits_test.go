package sim

import (
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

func TestWalkOutThroughExitAndArriveAtMatchingGate(t *testing.T) {
	w, e, se := starterPlayer(t)
	exits := Exits(w, se.ID)
	if len(exits) == 0 {
		t.Fatal("у деревни должны быть выезды на тракты")
	}
	var road *Exit
	passes := 0
	for i := range exits {
		if Blocked(exits[i].X, exits[i].Y) {
			t.Fatalf("выезд в стене/реке: %+v", exits[i])
		}
		if exits[i].Pass {
			passes++
		} else if road == nil {
			road = &exits[i]
		}
	}
	in, out := passPair(w)
	if in == 0 || out == 0 {
		t.Fatal("у котла должен быть перевал во внешний мир")
	}
	ctl := func(id core.ID) bool { return id == e.ID }
	hub := func(core.ID) *dice.Stream { return dice.NewStream(8) }
	// в центре деревни никто никуда не уходит
	StepExits(w, ctl, hub)
	if OnRoad(e) {
		t.Fatal("вдали от выезда на тракт не выходят")
	}
	p := ScenePos(e)
	p.X, p.Y = road.X, road.Y
	StepExits(w, ctl, hub)
	if !OnRoad(e) || journeyOf(e).To != road.To {
		t.Fatal("дошёл до выезда — вышел на тракт к соседу")
	}
	for i := 0; i < 4000 && OnRoad(e); i++ {
		StepJourneys(w, 0.1)
		if j := journeyOf(e); j != nil && j.Pending != nil {
			JourneyChoose(w, e, j.Pending.Options[len(j.Pending.Options)-1], dice.NewStream(2))
		}
	}
	if core.Place(e).Settlement != road.To {
		t.Fatal("должен дойти до соседа")
	}
	// стоит у въезда со стороны, откуда пришёл, и не уходит сразу обратно
	var back *Exit
	for _, ex := range Exits(w, road.To) {
		if ex.To == se.ID && !ex.Pass {
			ex := ex
			back = &ex
		}
	}
	if back == nil {
		t.Fatal("у соседа должен быть выезд обратно")
	}
	d := math.Hypot(p.X-back.X, p.Y-back.Y)
	if d > 20 || d <= exitReach {
		t.Fatalf("по приходе стоишь у въезда, но не на самом выезде: %.1f м", d)
	}
	StepExits(w, ctl, hub)
	if OnRoad(e) {
		t.Fatal("по приходе не должно сразу уводить обратно")
	}
	// перевал: из деревни котла во внешний мир ногами
	core.Place(e).Settlement = in
	core.Vitals(e).Energy = 100
	for _, ex := range Exits(w, in) {
		if ex.Pass {
			p.X, p.Y = ex.X, ex.Y
		}
	}
	StepExits(w, ctl, hub)
	if j := journeyOf(e); j == nil || j.To != out {
		t.Fatal("через перевал путь ведёт во внешний мир")
	}
	_ = passes
}
