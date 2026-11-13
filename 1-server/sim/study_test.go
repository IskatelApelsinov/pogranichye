package sim

import (
	"testing"

	"pogranichye/arcane"
	"pogranichye/core"
)

// setupStudy — поселение, игрок и, по флагу, маг-наставник (старец или обычный) в том же селе.
func setupStudy(gift bool, teacherAge float64, withTeacher bool) (*core.World, *core.Entity) {
	w := core.NewWorld("study")
	se := w.Spawn(&core.SettlementC{Name: "Село", Tier: "village", Mana: 0.5, Store: map[string]float64{}})
	e := w.Spawn(
		&core.IdentC{Name: "ученик", Sex: "м", Role: "farmer"},
		&core.VitalsC{Age: 25, Health: 100, Energy: 100, Morale: 60},
		&core.MindC{},
		&core.SkillsC{Skills: map[string]float64{"mage": 0}},
		&core.PlaceC{Settlement: se.ID},
		&core.HomeC{Status: "none", Settlement: se.ID},
	)
	if gift {
		w.Attach(e, &arcane.MageC{Awakened: true})
	}
	if withTeacher {
		t := w.Spawn(
			&core.IdentC{Name: "наставник", Sex: "м", Role: "mage"},
			&core.VitalsC{Age: teacherAge, Health: 100, Energy: 100},
			&core.MindC{},
			&core.PlaceC{Settlement: se.ID},
		)
		w.Attach(t, &arcane.MageC{Awakened: true})
	}
	return w, e
}

func knownCount(w *core.World, e *core.Entity) int { return len(grimoire(w, e).Known) }

// Без мага рядом учиться у наставника нельзя — отказ без затрат сил.
func TestStudyRefusedWithoutTeacher(t *testing.T) {
	w, e := setupStudy(true, 40, false)
	before := knownCount(w, e)
	if ok, why := StudyWeave(w, e, "s1"); ok || why == "" {
		t.Fatalf("без мага рядом урок должен быть отклонён, got ok=%v why=%q", ok, why)
	}
	if core.Vitals(e).Energy != 100 {
		t.Fatalf("отказ должен быть без затрат сил, energy=%.1f", core.Vitals(e).Energy)
	}
	if knownCount(w, e) != before {
		t.Fatal("при отказе схемы учиться не должны")
	}
}

// Одарённый ученик у мага открывает именованную схему; повтор того же reqid идемпотентен (без доп. затрат).
func TestStudyLearnsSchemaIdempotent(t *testing.T) {
	w, e := setupStudy(true, 40, true)
	before := knownCount(w, e)
	ok, why := StudyWeave(w, e, "s1")
	if !ok {
		t.Fatalf("у мага одарённый должен выучить узор: %q", why)
	}
	if knownCount(w, e) != before+1 {
		t.Fatalf("должна прибавиться ровно одна схема, было %d стало %d", before, knownCount(w, e))
	}
	energyAfter := core.Vitals(e).Energy
	if energyAfter != 100-studyEnergy {
		t.Fatalf("урок должен стоить %.0f сил, energy=%.1f", studyEnergy, energyAfter)
	}
	// идемпотентный повтор — ни новой схемы, ни новых затрат
	if ok, _ := StudyWeave(w, e, "s1"); !ok {
		t.Fatal("повтор принятого reqid должен быть ok")
	}
	if knownCount(w, e) != before+1 || core.Vitals(e).Energy != energyAfter {
		t.Fatal("повтор того же reqid не должен учить снова или тратить силы")
	}
}

// Прогрессия: от простого к сложному — простейшая неизвестная схема первой. Обычный маг доводит до
// сложности ≤ ёмкость+1; схемы сложности 6 (Цепь/Круг/Копьё) требуют старца или навыка — их обычный
// наставник на старте ещё не даёт.
func TestStudyProgressionAndTeacherStrength(t *testing.T) {
	// обычный маг: учим, пока учит, и проверяем, что все выученные схемы в пределах его потолка
	w, e := setupStudy(true, 40, true)
	ceil := teachCeiling(e, 1) // обычный маг — вклад 1; навык 0 → ёмкость 4 → потолок 5
	learned := 0
	for i := 0; i < 20; i++ {
		core.Vitals(e).Energy = 100 // не упираться в силы
		ok, _ := StudyWeave(w, e, "p"+roman(i+1))
		if !ok {
			break
		}
		learned++
	}
	if learned == 0 {
		t.Fatal("обычный маг должен научить хотя бы одному узору")
	}
	for _, s := range grimoire(w, e).Known {
		sp, _ := ReadWeave(s.Nodes)
		if sp.Complexity > ceil+0.001 {
			t.Fatalf("выучена схема %q сложности %.1f сверх потолка обычного наставника %.1f", s.Name, sp.Complexity, ceil)
		}
	}
	// старец (вклад 2) поднимает потолок — должен дать строго не меньше схем, чем обычный маг
	w2, e2 := setupStudy(true, 65, true)
	elder := 0
	for i := 0; i < 20; i++ {
		core.Vitals(e2).Energy = 100
		ok, _ := StudyWeave(w2, e2, "e"+roman(i+1))
		if !ok {
			break
		}
		elder++
	}
	if elder < learned {
		t.Fatalf("старец (потолок выше) должен научить не меньше: обычный=%d старец=%d", learned, elder)
	}
}

// Без дара ум держит лишь простые узоры (≤3): ученик без дара выучит только их и упрётся в «нечему учить».
func TestStudyGiftlessLimitedToSimple(t *testing.T) {
	w, e := setupStudy(false, 65, true) // старец, но ученик без дара
	for i := 0; i < 20; i++ {
		core.Vitals(e).Energy = 100
		if ok, _ := StudyWeave(w, e, "g"+roman(i+1)); !ok {
			break
		}
	}
	for _, s := range grimoire(w, e).Known {
		sp, _ := ReadWeave(s.Nodes)
		if sp.Complexity > giftlessMax+0.001 {
			t.Fatalf("без дара не должны открываться узоры сложнее %.0f, а есть %q (%.1f)", giftlessMax, s.Name, sp.Complexity)
		}
	}
	// учить больше нечему — отказ без затрат
	core.Vitals(e).Energy = 100
	if ok, why := StudyWeave(w, e, "gX"); ok || why == "" {
		t.Fatalf("исчерпав доступные простые узоры, бездарный должен получить отказ, got ok=%v", ok)
	}
	if core.Vitals(e).Energy != 100 {
		t.Fatal("отказ «нечему учить» — без затрат сил")
	}
}

// Пустой reqid отклоняется (как у прочих расходных действий).
func TestStudyNeedsReqid(t *testing.T) {
	w, e := setupStudy(true, 40, true)
	if ok, why := StudyWeave(w, e, ""); ok || why == "" {
		t.Fatalf("пустой reqid должен быть отклонён, got ok=%v why=%q", ok, why)
	}
}

// can_study в me.weave: истинно у мага при наличии узора и ложно без наставника.
func TestCanStudySignal(t *testing.T) {
	w, e := setupStudy(true, 40, true)
	if !canStudyHere(w, e) {
		t.Fatal("у мага с доступным узором can_study должно быть true")
	}
	w2, e2 := setupStudy(true, 40, false)
	if canStudyHere(w2, e2) {
		t.Fatal("без наставника can_study должно быть false")
	}
}
