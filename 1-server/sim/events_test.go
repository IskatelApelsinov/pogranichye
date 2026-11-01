package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// небольшое село с K жителями (ниже порога скученности), без лекаря и снадобья.
func smallVillage(w *core.World, k int) *core.Entity {
	se := w.Spawn(&core.SettlementC{Name: "село", Tier: "village", Store: map[string]float64{}})
	for i := 0; i < k; i++ {
		w.Spawn(
			&core.IdentC{Name: "ж", Sex: "м", Role: "farmer"},
			&core.VitalsC{Age: 30, Health: 100, Energy: 80},
			&core.MindC{},
			&core.PlaceC{Settlement: se.ID},
		)
	}
	return se
}

// Беда (война/климат подняли Strain) при нехватке лекарей зажигает хворь даже в НЕскученном селе.
func TestStrainTriggersDisease(t *testing.T) {
	w := core.NewWorld("dis")
	hub := dice.NewHub("dis")
	se := smallVillage(w, 10)  // 10 душ — скученности (порог 20) нет
	core.Settle(se).Strain = 4 // но грянула беда: раненые/беженцы/непогода

	outbreak := false
	day := core.Clock{TotalHours: 24}
	for i := 0; i < 400 && !outbreak; i++ {
		core.Settle(se).Strain = 4 // беда держится (идёт война/лютует непогода)
		DiseaseSystem{}.Hour(w, day, hub)
		if core.Settle(se).Plague > 0 {
			outbreak = true
		}
	}
	if !outbreak {
		t.Fatal("при реальной беде и без лекарей хворь должна вспыхнуть и в малом селе")
	}
}

// Без беды и без скученности мор НЕ возникает сам по себе — никакой сезонной эпидемии.
func TestNoStrainNoSeasonalDisease(t *testing.T) {
	w := core.NewWorld("dis2")
	hub := dice.NewHub("dis2")
	se := smallVillage(w, 12) // ниже порога скученности, Strain = 0

	day := core.Clock{TotalHours: 24}
	for i := 0; i < 1000; i++ { // целые годы — сменяются все сезоны
		DiseaseSystem{}.Hour(w, day, hub)
		if core.Settle(se).Plague > 0 {
			t.Fatal("без реальной причины (теснота/беда) мор возникать не должен — сезон ни при чём")
		}
	}
}

// Снадобье и лекарь гасят почву: при той же беде, но с медициной, вспышка куда менее вероятна.
func TestMedicineSuppressesOutbreak(t *testing.T) {
	w := core.NewWorld("dis3")
	hub := dice.NewHub("dis3")
	se := smallVillage(w, 10)
	day := core.Clock{TotalHours: 24}
	outbreak := false
	for i := 0; i < 400 && !outbreak; i++ {
		s := core.Settle(se)
		s.Strain = 4
		s.Store[econ.GoodRemedy] = 100 // вдоволь снадобья
		DiseaseSystem{}.Hour(w, day, hub)
		if s.Plague > 0 {
			outbreak = true
		}
	}
	if outbreak {
		t.Fatal("при достатке снадобья та же беда не должна зажигать мор")
	}
}
