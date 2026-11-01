package sim

import (
	"testing"

	"pogranichye/arcane"
	"pogranichye/core"
	"pogranichye/dice"
)

// Латент в месте силы (высокая мана) со временем набирает навык и ПРОБУЖДАЕТСЯ в мага.
func TestLatentAwakensAtPlaceOfPower(t *testing.T) {
	w := core.NewWorld("mg")
	hub := dice.NewHub("mg")
	se := w.Spawn(&core.SettlementC{Name: "Место силы", Tier: "city", Mana: 2.0,
		Store: map[string]float64{}})
	e := w.Spawn(
		&core.IdentC{Name: "ученик", Sex: "м", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Energy: 90, Morale: 60},
		&core.MindC{},
		&core.SkillsC{Skills: map[string]float64{"mage": 22}}, // почти дорос
		&core.PlaceC{Settlement: se.ID},
		&core.HomeC{Status: "none", Settlement: se.ID},
	)
	w.Attach(e, &arcane.MageC{Awakened: false})

	day := core.Clock{TotalHours: 24}
	for i := 0; i < 500; i++ { // порог высок — дар зреет не за дни, а за годы
		MagicSystem{}.Hour(w, day, hub)
		if arcane.Mage(e).Awakened {
			break
		}
	}
	if !arcane.Mage(e).Awakened {
		t.Fatalf("латент в месте силы должен был пробудиться, навык=%.1f", core.Skills(e).Level("mage"))
	}
	if core.Ident(e).Role != "mage" {
		t.Fatal("пробудившийся должен получить роль mage")
	}
}

// В мёртвой зоне без учителя латент почти не растёт — пробуждение не происходит быстро.
func TestLatentStallsInDeadZone(t *testing.T) {
	w := core.NewWorld("mg2")
	hub := dice.NewHub("mg2")
	se := w.Spawn(&core.SettlementC{Name: "Глушь", Tier: "village", Mana: 0.4,
		Store: map[string]float64{}})
	e := w.Spawn(
		&core.IdentC{Name: "глухой", Sex: "ж", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Energy: 90},
		&core.MindC{},
		&core.SkillsC{Skills: map[string]float64{"mage": 10}},
		&core.PlaceC{Settlement: se.ID},
		&core.HomeC{Status: "owned", Settlement: se.ID}, // дом есть — не сорвётся искать школу
	)
	w.Attach(e, &arcane.MageC{Awakened: false})
	day := core.Clock{TotalHours: 24}
	for i := 0; i < 60; i++ {
		MagicSystem{}.Hour(w, day, hub)
	}
	if arcane.Mage(e).Awakened {
		t.Fatal("в мёртвой зоне без учителя латент не должен быстро пробуждаться")
	}
}

// Боги даруют искру: grantSpark навешивает латентный дар случайному взрослому без дара.
func TestGrantSpark(t *testing.T) {
	w := core.NewWorld("mg3")
	se := w.Spawn(&core.SettlementC{Store: map[string]float64{}})
	for i := 0; i < 6; i++ {
		w.Spawn(
			&core.IdentC{Name: "житель", Sex: "м", Role: "farmer"},
			&core.VitalsC{Age: 30},
			&core.MindC{},
			&core.PlaceC{Settlement: se.ID},
		)
	}
	before := countMages(w)
	grantSpark(w, dice.NewStream(1))
	if countMages(w) != before+1 {
		t.Fatal("искра должна прибавить ровно одного носителя дара")
	}
}

func countMages(w *core.World) int {
	n := 0
	w.Each("mage", func(*core.Entity) { n++ })
	return n
}
