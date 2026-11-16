package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

// Самоход игрока-менеджера (task_ai 006 A): тело идёт к месту само, по серверу, без телепорта.

func walkUntil(w *core.World, e *core.Entity, maxSeconds float64) (seconds float64, maxStep float64) {
	ctl := func(id core.ID) bool { return id == e.ID }
	p := ScenePos(e)
	for seconds < maxSeconds && autoWalkOf(e) != nil {
		px, py := p.X, p.Y
		StepAutoWalk(w, 0.1, ctl)
		maxStep = math.Max(maxStep, math.Hypot(p.X-px, p.Y-py))
		seconds += 0.1
	}
	return
}

func TestWalkToReachesEveryPlaceWithoutTeleport(t *testing.T) {
	w, e, se := starterPlayer(t)
	var sets []core.ID
	w.Each("settlement", func(o *core.Entity) {
		if !isOuter(w, core.Settle(o)) {
			sets = append(sets, o.ID)
		}
	})
	_ = se
	for _, sidv := range sets { // и стартовое село, и сгенерированные
		core.Place(e).Settlement = sidv
		l := LayoutOf(w, sidv)
		p := EnsureScenePos(w, e)
		p.X, p.Y = l.Entry()
		for _, loc := range []string{"tavern", "woods", "smithy", "river", "market", "mine", "garden", "granary", "hearth", "well"} {
			if AtPOI(w, e) == loc {
				continue
			}
			if ok, why := WalkTo(w, e, loc); !ok {
				t.Fatalf("село %d: к %s не пошёл: %s", sidv, loc, why)
			}
			if v := WalkView(w, e); v == nil || v.(map[string]any)["to"] != loc {
				t.Fatalf("me.walk должен называть цель: %v", v)
			}
			sec, maxStep := walkUntil(w, e, 90)
			if autoWalkOf(e) != nil {
				t.Fatalf("село %d (%s): до %s не дошёл за %.0f с, стоит в (%.1f,%.1f)", sidv, l.Plan, loc, sec, p.X, p.Y)
			}
			if maxStep > autoWalkSpeed*0.1+1e-6 && sec < 8 {
				t.Fatalf("село %d: к %s был скачок на %.2f м за шаг — это телепорт", sidv, loc, maxStep)
			}
			if !atPoi(w, e, loc) || AtPOI(w, e) == "" {
				t.Fatalf("село %d: дошёл, но сервер не считает, что он у %s (%.1f,%.1f)", sidv, loc, p.X, p.Y)
			}
			if l.Blocked(p.X, p.Y) {
				t.Fatalf("село %d: у %s стоит в стене", sidv, loc)
			}
		}
	}
}

func TestWalkToRetargetCancelAndRefusals(t *testing.T) {
	w, e, _ := starterPlayer(t)
	ctl := func(id core.ID) bool { return id == e.ID }
	p := EnsureScenePos(w, e)
	if ok, why := WalkTo(w, e, "нет-такого"); ok || why == "" {
		t.Fatal("неизвестное место — отказ с причиной")
	}
	WalkTo(w, e, "woods")
	for i := 0; i < 20; i++ {
		StepAutoWalk(w, 0.1, ctl)
	}
	if AtPOI(w, e) == "woods" {
		t.Fatal("за две секунды до леса не дойти")
	}
	// смена цели в пути: одна цель, без двойного движения
	WalkTo(w, e, "tavern")
	if c := autoWalkOf(e); c == nil || c.Loc != "tavern" {
		t.Fatal("новая цель заменяет прежнюю")
	}
	// без управления (обрыв связи) тело не идёт, но цель помнит
	x, y := p.X, p.Y
	StepAutoWalk(w, 1, func(core.ID) bool { return false })
	if p.X != x || p.Y != y || autoWalkOf(e) == nil {
		t.Fatal("без игрока самоход стоит и ждёт")
	}
	if _, _ = walkUntil(w, e, 60); !atPoi(w, e, "tavern") {
		t.Fatal("после возвращения тело доходит до новой цели")
	}
	// у корчмы обычные дела доступны: пиво покупается
	core.Purse(e).Coins = 5
	if k := w.Get(core.Settle(settlementEnt(w, e)).Keeper); k != nil && core.Mind(k) != nil {
		core.Mind(k).ActCode = "keep_tavern" // корчмарь за стойкой (днём так и есть)
	}
	if ok, why := BuyBeer(w, e, "b1"); !ok {
		t.Fatalf("дошёл до корчмы — пиво должно продаваться: %s", why)
	}
	// отмена
	WalkTo(w, e, "mine")
	WalkTo(w, e, "")
	if autoWalkOf(e) != nil || WalkView(w, e) != nil {
		t.Fatal("пустая цель отменяет самоход")
	}
	// в пути по тракту самоход не начинается
	w.Attach(e, &JourneyC{From: 1, To: 2, Total: 10})
	if ok, _ := WalkTo(w, e, "tavern"); ok {
		t.Fatal("на тракте к месту села не пойти")
	}
}
