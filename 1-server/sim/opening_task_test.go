package sim

import (
	"testing"

	"pogranichye/core"
)

func assertOpeningTask(t *testing.T, got map[string]any, stage int, met, worked, drank, done bool) {
	t.Helper()
	if got["stage"] != stage || got["met"] != met || got["worked"] != worked ||
		got["drank"] != drank || got["done"] != done {
		t.Fatalf("unexpected opening task state: got=%+v want stage=%d met=%t worked=%t drank=%t done=%t",
			got, stage, met, worked, drank, done)
	}
	if text, ok := got["text"].(string); !ok || text == "" {
		t.Fatalf("opening task must always provide player-facing text, got=%+v", got["text"])
	}
}

func TestOpeningTaskAdvancesOnlyFromAuthoritativeState(t *testing.T) {
	w := core.NewWorld("opening-task-test")
	e := w.Spawn()

	// A fresh character must not appear to have met, worked, or drunk.
	assertOpeningTask(t, OpeningTask(e), 1, false, false, false, false)

	// Acquaintance is the only source of the first milestone.
	w.Attach(e, &AcquaintC{Known: map[core.ID]bool{7: true}})
	assertOpeningTask(t, OpeningTask(e), 2, true, false, false, false)

	// A real skill level advances the task; a goal string or client click cannot.
	w.Attach(e, &core.SkillsC{Skills: map[string]float64{"woodcutter": 0.5}})
	assertOpeningTask(t, OpeningTask(e), 3, true, true, false, false)

	// Drinking alone cannot complete the task before acquaintance and work.
	other := w.Spawn()
	EnsurePlayer(w, other).DrankBeer = true
	assertOpeningTask(t, OpeningTask(other), 1, false, false, true, false)

	// The final milestone is the server-owned DrankBeer fact.
	EnsurePlayer(w, e).DrankBeer = true
	assertOpeningTask(t, OpeningTask(e), 3, true, true, true, true)
}
