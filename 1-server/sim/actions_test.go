package sim

import (
	"testing"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

func actor(w *core.World, x, y, energy float64) *core.Entity {
	e := w.Spawn(
		&core.IdentC{Name: "Игрок", Role: "farmer", Sex: "м"},
		&core.VitalsC{Energy: energy, Health: 100, Morale: 50},
	)
	w.Attach(e, &ScenePosC{X: x, Y: y, Stam: 1})
	return e
}

// Рубка у леса даёт бревно и тратит силы; вдали от леса — отказ.
func TestChopWood(t *testing.T) {
	w := core.NewWorld("t")
	wx, wy := poiByKind("woods")
	e := actor(w, wx, wy, 100)
	ok, why := ChopWood(w, e, "c1")
	if !ok {
		t.Fatalf("у леса рубка должна пройти: %s", why)
	}
	if econ.Stash(e).Goods[econ.GoodWood] != chopYield {
		t.Fatal("бревно не попало в закрома")
	}
	if core.Vitals(e).Energy != 100-chopEnergy {
		t.Fatal("силы не потрачены")
	}
	// повтор того же reqid — без второго бревна и повторной траты сил.
	ChopWood(w, e, "c1")
	if econ.Stash(e).Goods[econ.GoodWood] != chopYield {
		t.Fatal("повтор reqid не должен давать второе бревно")
	}
	if core.Vitals(e).Energy != 100-chopEnergy {
		t.Fatal("повтор reqid не должен повторно тратить силы")
	}
	// Неудачный запрос не фиксируется как выполненный: тот же reqid можно
	// безопасно повторить после того, как игрок подойдёт к лесу.
	far := actor(w, 60, 60, 100)
	if ok, _ := ChopWood(w, far, "retry-chop"); ok {
		t.Fatal("вдали от леса рубить нельзя")
	}
	pos := ScenePos(far)
	pos.X, pos.Y = wx, wy
	if ok, why := ChopWood(w, far, "retry-chop"); !ok {
		t.Fatalf("после подхода тот же reqid должен сработать: %s", why)
	}
	if econ.Stash(far).Goods[econ.GoodWood] != chopYield {
		t.Fatal("повтор после отказа должен дать ровно одно бревно")
	}
}

// Рыбалка у реки даёт рыбу; не у воды — отказ.
func TestFish(t *testing.T) {
	w := core.NewWorld("t")
	rx, ry := poiByKind("river")
	e := actor(w, rx, ry, 100)
	if ok, why := FishCatch(w, e, "f1"); !ok {
		t.Fatalf("у реки лов должен пройти: %s", why)
	}
	if econ.Stash(e).Goods[econ.GoodFish] != fishYield {
		t.Fatal("рыба не попала в закрома")
	}
	// Повтор успешного reqid не добавляет рыбу и не тратит силы второй раз.
	FishCatch(w, e, "f1")
	if econ.Stash(e).Goods[econ.GoodFish] != fishYield {
		t.Fatal("повтор reqid не должен давать вторую рыбу")
	}
	if core.Vitals(e).Energy != 100-fishEnergy {
		t.Fatal("повтор reqid не должен повторно тратить силы")
	}
	// Отказ из-за расстояния не блокирует повтор того же reqid после подхода.
	far := actor(w, 60, 60, 100)
	if ok, _ := FishCatch(w, far, "retry-fish"); ok {
		t.Fatal("не у воды ловить нельзя")
	}
	pos := ScenePos(far)
	pos.X, pos.Y = rx, ry
	if ok, why := FishCatch(w, far, "retry-fish"); !ok {
		t.Fatalf("после подхода тот же reqid должен сработать: %s", why)
	}
	if econ.Stash(far).Goods[econ.GoodFish] != fishYield {
		t.Fatal("повтор после отказа должен дать ровно одну рыбу")
	}
}

// Магический разряд тратит ману и прореживает опасного зверя; без маны — отказ.
func TestCastSpark(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Store: map[string]float64{}})
	f := &battle.FaunaC{Pop: map[string]float64{"wolf": 5}, Cap: map[string]float64{}, Base: map[string]float64{}}
	w.Attach(se, f)
	e := actor(w, 0, 0, 100)
	w.Attach(e, &core.PlaceC{Settlement: se.ID})
	EnsurePlayer(w, e)
	before := f.Pop["wolf"]
	if ok, why := CastSpark(w, e, "s1"); !ok {
		t.Fatalf("разряд должен пройти при полной мане: %s", why)
	}
	if Player(e).Mana != sparkManaMax-sparkCost {
		t.Fatalf("мана не списана: %.0f", Player(e).Mana)
	}
	if f.Pop["wolf"] >= before {
		t.Fatal("разряд должен прореживать опасного зверя")
	}
	// высадим ману — откажет
	Player(e).Mana = 1
	if ok, _ := CastSpark(w, e, "s2"); ok {
		t.Fatal("без маны разряда нет")
	}
}

// Мана восстанавливается в реальном времени.
func TestManaRegen(t *testing.T) {
	w := core.NewWorld("t")
	e := actor(w, 0, 0, 100)
	p := EnsurePlayer(w, e)
	p.Mana = 0
	RegenMana(w, 2.0)
	if p.Mana <= 0 || p.Mana > sparkManaMax {
		t.Fatalf("мана должна восстанавливаться: %.1f", p.Mana)
	}
}
