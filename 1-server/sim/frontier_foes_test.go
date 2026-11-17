package sim

import (
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// Игрок у дикого места в поле — место населено; зверьё бродит широко, а не в 14 м; зачищенное место
// пустует два дня; перешедший межу зверь попадает в земли соседнего села без скачка в мире.
func TestFrontierFoesLiveInWildPlacesAndRoam(t *testing.T) {
	w, e, se := starterPlayer(t)
	SetSeamless(w, true)
	tr := terrainFor(w)
	ae, an := Anchor(w, core.Settle(se))
	// ищем в землях Починка урочище, где кто-то живёт днём
	var spot WildPlace
	for _, p := range WildPlacesNear(w, tr, ae, an, 6000) {
		if pk, ok := frontierFauna[p.Kind]; ok && !pk.night && nearestSettlementTo(w, p.E, p.N) == se.ID {
			spot = p
			break
		}
	}
	if spot.ID == "" {
		t.Skip("в землях стартового села нет дневных урочищ со зверьём")
	}
	p := ScenePos(e)
	p.X, p.Y = spot.E-ae+30, spot.N-an // игрок в 30 м от места
	ctl := func(id core.ID) bool { return id == e.ID }
	g := dice.NewStream(7)
	StepCombatAtHour(w, 0.1, 12, ctl, g) // первый шаг ставит таймер подсева
	var pack []*FoeC
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Wild == spot.ID {
			pack = append(pack, f)
		}
	})
	want := frontierFauna[spot.Kind]
	if len(pack) != want.n || pack[0].Species != want.species {
		t.Fatalf("в месте %s (%s) ждали %d×%s, нашли %d", spot.Name, spot.Kind, want.n, want.species, len(pack))
	}
	// бродит: игрок уходит подальше, чтобы его не гнали; за пару минут зверь уходит от логова дальше 14 м
	p.X, p.Y = spot.E-ae+400, spot.N-an+400
	far := 0.0
	for i := 0; i < 1800; i++ {
		StepCombatAtHour(w, 0.1, 12, ctl, g)
		far = math.Max(far, math.Hypot(pack[0].X-pack[0].HomeX, pack[0].Y-pack[0].HomeY))
	}
	if far < 20 {
		t.Fatalf("зверь фронтира не отходит от логова: %.1f м", far)
	}
	if far > want.roam+90 {
		t.Fatalf("зверь ушёл слишком далеко: %.1f м при круге %.0f", far, want.roam)
	}
	// зачистка: все убиты → место помнит; пока не прошло 2 дня — не населяется заново
	u := wildUse(w)
	u.Today = 10
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Wild == spot.ID {
			killFoe(w, fe, f, e)
		}
	})
	if u.Cleared[spot.ID] != 10 {
		t.Fatalf("место не запомнило зачистку: %v", u.Cleared)
	}
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Wild == spot.ID {
			w.Remove(fe.ID)
		}
	})
	p.X, p.Y = spot.E-ae+30, spot.N-an
	populateFrontierFoes(w, se, []*core.Entity{e}, 12)
	n := 0
	w.Each("foe", func(fe *core.Entity) {
		if Foe(fe).Wild == spot.ID {
			n++
		}
	})
	if n != 0 {
		t.Fatal("зачищенное место населилось на следующий же день")
	}
	u.Today = 12
	populateFrontierFoes(w, se, []*core.Entity{e}, 12)
	w.Each("foe", func(fe *core.Entity) {
		if Foe(fe).Wild == spot.ID {
			n++
		}
	})
	if n == 0 {
		t.Fatal("через два дня место должно населиться снова")
	}
}

func TestFrontierFoeCrossesLandsWithoutJump(t *testing.T) {
	w, _, se := starterPlayer(t)
	SetSeamless(w, true)
	var other *core.Entity
	w.Each("settlement", func(x *core.Entity) {
		if other == nil && x.ID != se.ID {
			other = x
		}
	})
	ae, an := Anchor(w, core.Settle(se))
	be, bn := Anchor(w, core.Settle(other))
	// зверь Починка стоит у самого центра соседнего села — должен перейти в его земли
	fe := w.Spawn(&FoeC{Species: "wolf", Settlement: se.ID, X: be - ae + 5, Y: bn - an, HomeX: 0, HomeY: 0, HP: 30, Max: 30,
		State: "roam", Wild: "w:test", Roam: 200})
	f := Foe(fe)
	StepFoeLands(w)
	if f.Settlement != other.ID {
		t.Fatalf("зверь не перешёл в земли соседнего села: %d", f.Settlement)
	}
	if math.Abs(be+f.X-(be+5)) > 1e-6 || math.Abs(bn+f.Y-bn) > 1e-6 {
		t.Fatalf("мировая точка сдвинулась: (%.2f,%.2f)", f.X, f.Y)
	}
	if math.Abs(be+f.HomeX-ae) > 1e-6 || math.Abs(bn+f.HomeY-an) > 1e-6 {
		t.Fatal("логово зверя уехало вместе с межой")
	}
}

// Травник на промысле идёт за околицу к урочищу своего ремесла, а не стоит в селе.
func TestVillagerWalksToWildPlaceForForaging(t *testing.T) {
	w, _, se := starterPlayer(t)
	SetSeamless(w, true)
	var npc *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if npc == nil && uint64(r.ID)%2 == 0 && core.Mind(r) != nil && Player(r) == nil {
			npc = r
		}
	}
	m := core.Mind(npc)
	m.Goal, m.ActCode = "work", "forage_herbs"
	tx, ty, ok := forayTarget(w, npc, se.ID, "forage_herbs")
	if !ok {
		t.Skip("рядом с селом нет травного луга или ягодника")
	}
	if math.Hypot(tx, ty) < 250 {
		t.Fatalf("урочище должно быть за околицей: %.0f м", math.Hypot(tx, ty))
	}
	p := EnsureScenePos(w, npc)
	start := math.Hypot(tx-p.X, ty-p.Y)
	for i := 0; i < 3000; i++ { // 5 минут реального времени
		m.Goal, m.ActCode = "work", "forage_herbs"
		StepNPCMovement(w, 0.1, nil)
	}
	if left := math.Hypot(tx-p.X, ty-p.Y); left > start-150 {
		t.Fatalf("травник не идёт к урочищу: было %.0f м, осталось %.0f", start, left)
	}
}
