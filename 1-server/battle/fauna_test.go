package battle

import (
	"testing"

	"pogranichye/dice"
)

// Плотность опасного зверья растёт с маной: место силы кишит медведями, мёртвая зона — почти нет.
func TestSeedFaunaManaScaling(t *testing.T) {
	s := dice.NewStream(1)
	dead := SeedFauna("forest", 0.5, s)
	power := SeedFauna("forest", 1.8, s)
	if power.Cap["bear"] <= dead.Cap["bear"] {
		t.Fatalf("у места силы медведей должно быть больше: мёртвая=%.1f сила=%.1f",
			dead.Cap["bear"], power.Cap["bear"])
	}
	// мелкой дичи — заметно больше, чем крупного зверя, в любом краю
	if dead.Cap["hare"] < dead.Cap["bear"] {
		t.Fatal("зайцев должно быть больше, чем медведей")
	}
}

// Встреча невозможна, если опасного зверя тут реально нет; возможна, когда он водится.
func TestEncounterNeedsRealBeasts(t *testing.T) {
	s := dice.NewStream(7)
	empty := &FaunaC{Pop: map[string]float64{"hare": 50, "fox": 10}, Cap: map[string]float64{}}
	for i := 0; i < 200; i++ {
		if _, dangerous := empty.Encounter(0, s); dangerous {
			t.Fatal("без крупного зверя опасных встреч быть не должно")
		}
	}
	full := &FaunaC{Pop: map[string]float64{"wolf": 60, "bear": 30}, Cap: map[string]float64{}}
	met := 0
	for i := 0; i < 200; i++ {
		if _, dangerous := full.Encounter(0, s); dangerous {
			met++
		}
	}
	if met == 0 {
		t.Fatal("там, где густо волков и медведей, охотник обязан иногда нарываться")
	}
}

// Оберег снижает число опасных встреч.
func TestWardReducesEncounters(t *testing.T) {
	base := map[string]float64{"wolf": 60, "bear": 30}
	noWard, warded := 0, 0
	s1 := dice.NewStream(3)
	s2 := dice.NewStream(3)
	for i := 0; i < 400; i++ {
		f1 := &FaunaC{Pop: map[string]float64{"wolf": base["wolf"], "bear": base["bear"]}}
		f2 := &FaunaC{Pop: map[string]float64{"wolf": base["wolf"], "bear": base["bear"]}}
		if _, d := f1.Encounter(0, s1); d {
			noWard++
		}
		if _, d := f2.Encounter(0.7, s2); d {
			warded++
		}
	}
	if warded >= noWard {
		t.Fatalf("оберег должен снижать встречи: без=%d с оберегом=%d", noWard, warded)
	}
}

// Добыча убавляет поголовье и не уводит в минус; приплод возвращает выбитое угодье к жизни.
func TestTakeAndBreed(t *testing.T) {
	f := &FaunaC{
		Pop: map[string]float64{"boar": 10},
		Cap: map[string]float64{"boar": 40},
	}
	f.Take("boar", 3)
	if f.Pop["boar"] != 7 {
		t.Fatalf("добыча должна убавить поголовье: %.1f", f.Pop["boar"])
	}
	f.Take("boar", 100) // нельзя добыть больше, чем есть
	if f.Pop["boar"] < 0 {
		t.Fatal("поголовье не уходит в минус")
	}
	// немного зверя осталось — за годы угодье снова наполняется
	f.Pop["boar"] = 2
	for i := 0; i < 300; i++ {
		f.Breed()
	}
	if f.Pop["boar"] <= 2 {
		t.Fatalf("уцелевшее стадо должно размножиться: %.1f", f.Pop["boar"])
	}
	// а выбитый под ноль вид сам не воскресает
	z := &FaunaC{Pop: map[string]float64{"wolf": 0}, Cap: map[string]float64{"wolf": 20}}
	for i := 0; i < 100; i++ {
		z.Breed()
	}
	if z.Pop["wolf"] != 0 {
		t.Fatalf("выбитый под ноль вид не должен воскресать сам: %.1f", z.Pop["wolf"])
	}
}
