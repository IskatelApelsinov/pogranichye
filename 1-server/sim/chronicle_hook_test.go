package sim

import (
	"strings"
	"testing"

	"pogranichye/arcane"
	"pogranichye/chronicle"
	"pogranichye/core"
)

func lastChronicle(w *core.World) (kind, text string) {
	c := chronicle.Of(w)
	if c == nil || len(c.Entries) == 0 {
		return "", ""
	}
	e := c.Entries[len(c.Entries)-1]
	return e.Kind, e.Text
}

// Классификация заметной смерти: явный приоритет хозяин → маг → старец; роль не теряется.
func TestRecordNotableDeathPriority(t *testing.T) {
	w := core.NewWorld("chk")
	w.Spawn(&chronicle.ChronicleC{Tally: map[string]int{}})
	se := w.Spawn(&core.SettlementC{Name: "Село", Store: map[string]float64{}})

	// старый ХОЗЯИН села (и по возрасту старец) — должен записаться хозяином, не старцем
	keeper := w.Spawn(
		&core.IdentC{Name: "Хозяин", Role: "tavernkeeper"},
		&core.VitalsC{Age: 68},
		&core.PlaceC{Settlement: se.ID},
	)
	core.Settle(se).Keeper = keeper.ID
	recordNotableDeath(w, 10, keeper.ID)
	if _, text := lastChronicle(w); !strings.Contains(text, "хозяин") {
		t.Fatalf("старый хозяин должен записаться хозяином, а не старцем: %q", text)
	}

	// маг (не хозяин)
	mage := w.Spawn(
		&core.IdentC{Name: "Маг", Role: "mage"},
		&core.VitalsC{Age: 30},
		&core.PlaceC{Settlement: se.ID},
	)
	w.Attach(mage, &arcane.MageC{})
	recordNotableDeath(w, 11, mage.ID)
	if _, text := lastChronicle(w); !strings.Contains(text, "маг") {
		t.Fatalf("маг должен записаться магом: %q", text)
	}

	// рядовой молодой житель — в летопись не идёт
	before := len(chronicle.Of(w).Entries)
	plain := w.Spawn(
		&core.IdentC{Name: "Ждан", Role: "farmer"},
		&core.VitalsC{Age: 25},
		&core.PlaceC{Settlement: se.ID},
	)
	recordNotableDeath(w, 12, plain.ID)
	if len(chronicle.Of(w).Entries) != before {
		t.Fatal("рядовая смерть не должна попадать в летопись")
	}
}
