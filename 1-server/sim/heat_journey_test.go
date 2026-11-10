package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// Температура областей и дорога (comms 0059, часть C).

func heatLevel(w *core.World, id core.ID) int {
	if h := heatOf(w.Get(id)); h != nil {
		return h.Level
	}
	return -1
}

func sceneBodies(w *core.World, settlement, except core.ID) int {
	n := 0
	for _, r := range residentsOf(w, settlement) {
		if r.ID != except && Player(r) == nil && ScenePos(r) != nil {
			n++
		}
	}
	return n
}

func neighbourVillage(t *testing.T, w *core.World, from core.ID) *core.Entity {
	t.Helper()
	var dst *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if dst == nil && o.ID != from && !isOuter(w, core.Settle(o)) {
			dst = o
		}
	})
	if dst == nil {
		t.Skip("нет соседней деревни")
	}
	return dst
}

// Игрок вышел в путь: цель прогрета (жители на местах), исходное поселение остыло и не держит ни тел,
// ни врагов; по приходе всё сходится — горячо только там, где игрок.
func TestJourneyHeatsDestinationAndCoolsSource(t *testing.T) {
	w, e, se := starterPlayer(t)
	me := func(id core.ID) bool { return id == e.ID }
	s := dice.NewStream(2)
	dst := neighbourVillage(t, w, se.ID)

	UpdateHeat(w, me)
	StepCombat(w, 0.1, me, s)
	if heatLevel(w, se.ID) != 2 || sceneBodies(w, se.ID, e.ID) == 0 {
		t.Fatal("до выхода исходное поселение должно быть горячим и населённым")
	}
	if ok, why := JourneyStart(w, e, dst.ID, "hj1", s); !ok {
		t.Fatalf("путь: %s", why)
	}
	UpdateHeat(w, me)
	StepCombat(w, 0.1, me, s)
	StepNPCMovement(w, 0.1, me)
	if heatLevel(w, dst.ID) != 2 || sceneBodies(w, dst.ID, e.ID) == 0 {
		t.Fatal("цель пути должна быть прогрета заранее")
	}
	if heatLevel(w, se.ID) == 2 || sceneBodies(w, se.ID, e.ID) != 0 {
		t.Fatalf("исходное поселение должно остыть без тел: level=%d bodies=%d", heatLevel(w, se.ID), sceneBodies(w, se.ID, e.ID))
	}
	foes := 0
	w.Each("foe", func(*core.Entity) { foes++ })
	if foes != 0 {
		t.Fatalf("пока игрок в пути, врагов сцены быть не должно: %d", foes)
	}
	for i := 0; i < 3000 && OnRoad(e); i++ {
		StepJourneys(w, 0.1)
		if j := journeyOf(e); j != nil && j.Pending != nil {
			JourneyChoose(w, e, j.Pending.Options[len(j.Pending.Options)-1], s)
		}
		UpdateHeat(w, me)
	}
	hot := 0
	w.Each("settlement", func(o *core.Entity) {
		if heatLevel(w, o.ID) == 2 {
			hot++
		}
	})
	if core.Place(e).Settlement != dst.ID || hot != 1 || heatLevel(w, dst.ID) != 2 {
		t.Fatalf("по приходе горячо ровно одно поселение — где игрок: hot=%d", hot)
	}
	// разрыв связи: всё остывает, тел и врагов не остаётся
	UpdateHeat(w, func(core.ID) bool { return false })
	StepCombat(w, 0.1, func(core.ID) bool { return false }, s)
	w.Each("settlement", func(o *core.Entity) {
		if heatLevel(w, o.ID) == 2 || sceneBodies(w, o.ID, e.ID) != 0 {
			t.Fatal("после ухода игрока не должно остаться горячих поселений и тел")
		}
	})
	w.Each("foe", func(*core.Entity) {
		t.Fatal("после ухода игрока не должно остаться врагов")
	})
}

// Два игрока в разных поселениях: горячи оба, уход одного не трогает сцену другого.
func TestTwoPlayersInDifferentSettlements(t *testing.T) {
	w, a, se := starterPlayer(t)
	dst := neighbourVillage(t, w, se.ID)
	var b *core.Entity
	for _, r := range residentsOf(w, dst.ID) {
		if v := core.Vitals(r); b == nil && v != nil && v.Age >= 18 {
			b = r
		}
	}
	if b == nil {
		t.Skip("в соседней деревне нет взрослого")
	}
	EnsureScenePos(w, b)
	EnsurePlayer(w, b)
	both := func(id core.ID) bool { return id == a.ID || id == b.ID }
	s := dice.NewStream(6)
	hot, _, _ := UpdateHeat(w, both)
	StepCombat(w, 0.1, both, s)
	if hot != 2 || sceneBodies(w, se.ID, 0) == 0 || sceneBodies(w, dst.ID, 0) == 0 {
		t.Fatalf("оба поселения должны быть горячими и населёнными: hot=%d", hot)
	}
	// враги одного поселения не видны в другом
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Settlement != se.ID && f.Settlement != dst.ID {
			t.Fatal("враг в поселении без игрока")
		}
	})
	onlyA := func(id core.ID) bool { return id == a.ID }
	UpdateHeat(w, onlyA)
	StepCombat(w, 0.1, onlyA, s)
	if heatLevel(w, dst.ID) == 2 || sceneBodies(w, dst.ID, 0) != 0 {
		t.Fatal("поселение ушедшего игрока должно остыть")
	}
	if heatLevel(w, se.ID) != 2 || sceneBodies(w, se.ID, a.ID) == 0 {
		t.Fatal("поселение оставшегося игрока должно остаться горячим")
	}
	w.Each("foe", func(fe *core.Entity) {
		if Foe(fe).Settlement == dst.ID {
			t.Fatal("враги остывшего поселения должны быть сняты")
		}
	})
}

// Клиент не может назначить себе попадание/урон/дальность: сервер ограничивает acc и power,
// проверяет дальность и принадлежность цели.
func TestClientCannotInflateStrike(t *testing.T) {
	strike := func(acc, power float64, y float64, style string) float64 {
		w, e, se := starterPlayer(t)
		p := ScenePos(e)
		p.X, p.Y, p.Facing = -100, 0, 90
		inv := ensureInv(w, e)
		inv.Items["sword"], inv.Hand = 1, "sword"
		fe := w.Spawn(&FoeC{Species: "bear", Settlement: se.ID, X: -100, Y: y, HP: 85, Max: 85, State: "roam", Aware: true})
		Strike(w, e, StrikeInput{Target: fe.ID, Aim: "torso", Style: style, Aimed: true, Acc: acc, Power: power}, dice.NewStream(1))
		return 85 - Foe(fe).HP
	}
	honest := strike(1, 1, 1.5, "slash")
	if honest <= 0 {
		t.Fatal("честный удар должен ранить")
	}
	if inflated := strike(1e9, 1e9, 1.5, "slash"); inflated != honest {
		t.Fatalf("завышенные acc/power не должны увеличивать урон: %.2f против %.2f", inflated, honest)
	}
	if strike(-5, 1, 1.5, "slash") != 0 {
		t.Fatal("отрицательная точность — промах")
	}
	if strike(1, 1, 30, "slash") != 0 {
		t.Fatal("мечом за 30 м не достать, что бы ни прислал клиент")
	}
	if strike(1, 1, 1.5, "godmode") != honest {
		t.Fatal("неизвестный стиль должен считаться обычным ударом")
	}
	// цель из другого поселения недоступна
	w, e, se := starterPlayer(t)
	other := neighbourVillage(t, w, se.ID)
	p := ScenePos(e)
	fe := w.Spawn(&FoeC{Species: "wolf", Settlement: other.ID, X: p.X, Y: p.Y + 1, HP: 30, Max: 30, State: "roam"})
	if ok, _ := Strike(w, e, StrikeInput{Target: fe.ID, Aimed: true, Acc: 1}, dice.NewStream(1)); ok || Foe(fe).HP != 30 {
		t.Fatal("врага из другого поселения ударить нельзя")
	}
}
