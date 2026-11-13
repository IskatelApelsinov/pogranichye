package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

func TestFoeAggroRadiusUsesServerAuthoritativeLightAndTime(t *testing.T) {
	w := core.NewWorld("night-light-aggro")
	e := w.Spawn(&InventoryC{Items: map[string]int{"torch": 1}, Worn: map[string]string{}})
	base := 16.0

	tests := []struct {
		name string
		hour int
		want float64
	}{
		{"day", 12, base},
		{"dusk", 20, base * 0.85},
		{"late twilight", 21, base * 0.85},
		{"deep night", 23, base * 0.65},
		{"midnight", 0, base * 0.65},
		{"pre-dawn", 5, base * 0.65},
		{"dawn twilight", 6, base * 0.85},
		{"morning", 8, base},
		{"negative hour wraps", -1, base * 0.65},
		{"hour wraps to midnight", 24, base * 0.65},
		{"hour wraps to morning", 32, base},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := foeAggroRadius(e, base, tt.hour); math.Abs(got-tt.want) > 1e-9 {
				t.Fatalf("foeAggroRadius(hour=%d) = %.3f, want %.3f", tt.hour, got, tt.want)
			}
		})
	}

	inv := Inv(e)
	inv.Hand = "torch"
	if got, want := foeAggroRadius(e, base, 23), base*1.15; math.Abs(got-want) > 1e-9 {
		t.Fatalf("held torch at deep night = %.3f, want %.3f", got, want)
	}
	inv.Hand = "lantern"
	if !hasHeldLight(e) {
		t.Fatal("lantern in hand must count as an active light")
	}
	inv.Hand = ""
	c := &CombatC{Light: 2}
	w.Attach(e, c)
	if !hasHeldLight(e) {
		t.Fatal("active magical light must count as an active light")
	}
	c.Light = 0
	if hasHeldLight(e) {
		t.Fatal("expired magical light must not count as an active light")
	}
}

func TestFoeAggroRadiusDoesNotTrustInventoryOnlyLight(t *testing.T) {
	w := core.NewWorld("night-light-not-in-hand")
	e := w.Spawn(&InventoryC{
		Items: map[string]int{"torch": 1, "lantern": 1},
		Hand:  "",
		Worn:  map[string]string{},
	})
	if hasHeldLight(e) {
		t.Fatal("a light merely carried in inventory must not illuminate the player")
	}
	if got, want := foeAggroRadius(e, 16, 23), 16*0.65; math.Abs(got-want) > 1e-9 {
		t.Fatalf("inventory-only light aggro = %.3f, want %.3f", got, want)
	}
}

func TestFoeAggroRadiusCombinesWithSneakAfterLighting(t *testing.T) {
	w := core.NewWorld("night-light-sneak")
	e := w.Spawn(&InventoryC{Items: map[string]int{}, Worn: map[string]string{}})
	base := 16.0
	unlit := foeAggroRadius(e, base, 23) * 0.4
	if math.Abs(unlit-4.16) > 1e-9 {
		t.Fatalf("unlit sneaking radius = %.3f, want 4.160", unlit)
	}
	inv := Inv(e)
	inv.Hand = "torch"
	lit := foeAggroRadius(e, base, 23) * 0.4
	if math.Abs(lit-7.36) > 1e-9 {
		t.Fatalf("lit sneaking radius = %.3f, want 7.360", lit)
	}
}
