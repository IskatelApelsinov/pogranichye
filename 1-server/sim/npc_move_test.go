package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

// villager собирает минимального жителя в поселении с заданной ролью и целью.
func villager(w *core.World, role, goal string, set core.ID) *core.Entity {
	return w.Spawn(
		&core.IdentC{Name: "Тест", Role: role, Sex: "м"},
		&core.MindC{Goal: goal},
		&core.PlaceC{Settlement: set},
	)
}

// Житель с целью work (кузнец) идёт к кузнице и со временем доходит до своего места у неё.
func TestNPCWalksToActivityPOI(t *testing.T) {
	w := core.NewWorld("t")
	e := villager(w, "smith", "work", 100)
	for i := 0; i < 150; i++ { // ~60 c по 0.4 c/тик
		StepNPCMovement(w, 0.4, nil)
	}
	p := ScenePos(e)
	if p == nil {
		t.Fatal("житель должен был получить ScenePos")
	}
	tx, ty := poiByKind("smithy")
	ox, oy := idOffset(uint64(e.ID))
	if d := math.Hypot(p.X-(tx+ox), p.Y-(ty+oy)); d > npcArrive+1e-6 {
		t.Fatalf("кузнец не дошёл до кузницы: |до цели|=%.2f (pos %.1f,%.1f)", d, p.X, p.Y)
	}
}

// Смена цели НЕ телепортирует: позиция меняется непрерывно, шаг за тик ограничен скоростью.
func TestNPCNoTeleportOnGoalChange(t *testing.T) {
	w := core.NewWorld("t")
	e := villager(w, "farmer", "work", 100) // идёт к огороду
	for i := 0; i < 20; i++ {
		StepNPCMovement(w, 0.4, nil)
	}
	p := ScenePos(e)
	before := *p
	core.Mind(e).Goal = "eat" // резко в корчму на другом конце
	StepNPCMovement(w, 0.4, nil)
	moved := math.Hypot(p.X-before.X, p.Y-before.Y)
	if moved > npcWalk*0.4+1e-6 {
		t.Fatalf("смена цели не должна телепортировать: сдвиг за тик=%.2f м (лимит %.2f)", moved, npcWalk*0.4)
	}
}

// Управляемое игроком тело система ходьбы не трогает.
func TestNPCSkipsControlled(t *testing.T) {
	w := core.NewWorld("t")
	e := villager(w, "smith", "work", 100)
	controlled := func(id core.ID) bool { return id == e.ID }
	for i := 0; i < 10; i++ {
		StepNPCMovement(w, 0.4, controlled)
	}
	if ScenePos(e) != nil {
		t.Fatal("тело под игроком не должно получать авто-движение")
	}
}

// Позиции детерминированы: тот же мир/роль/цель → тот же путь при тех же dt.
func TestNPCDeterministic(t *testing.T) {
	run := func() ScenePosC {
		w := core.NewWorld("t")
		e := villager(w, "fisher", "work", 100)
		for i := 0; i < 40; i++ {
			StepNPCMovement(w, 0.4, nil)
		}
		return *ScenePos(e)
	}
	if run() != run() {
		t.Fatal("движение НПС должно быть детерминированным")
	}
}

// Жители остаются в пределах круга площадки.
func TestNPCStaysInScene(t *testing.T) {
	w := core.NewWorld("t")
	var es []*core.Entity
	for i := 0; i < 14; i++ {
		es = append(es, villager(w, "hunter", "leave", 100)) // гонит к опушке на краю
	}
	for i := 0; i < 300; i++ {
		StepNPCMovement(w, 0.4, nil)
	}
	for _, e := range es {
		p := ScenePos(e)
		if math.Hypot(p.X, p.Y) > SceneRadius+1e-6 {
			t.Fatalf("житель вышел за площадку: |pos|=%.2f", math.Hypot(p.X, p.Y))
		}
	}
}
