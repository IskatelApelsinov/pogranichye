package sim

import (
	"testing"

	"pogranichye/core"
)

func TestFoesViewExposesAttackWindup(t *testing.T) {
	w := core.NewWorld("combat-telegraph")
	w.Spawn(&FoeC{
		Species: "wolf", Settlement: 7, X: 3, HP: 30, Max: 30,
		State: "windup", Aware: true, Windup: 0.45,
	})

	foes := FoesView(w, 7)
	if len(foes) != 1 {
		t.Fatalf("expected one visible foe, got %d", len(foes))
	}
	if foes[0]["state"] != "windup" {
		t.Fatalf("expected windup state, got %v", foes[0]["state"])
	}
	windup, ok := foes[0]["windup"].(float64)
	if !ok || windup <= 0 {
		t.Fatalf("expected positive windup in public foe frame, got %#v", foes[0]["windup"])
	}
}
