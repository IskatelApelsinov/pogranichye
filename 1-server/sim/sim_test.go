package sim

import (
	"bytes"
	"encoding/json"
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/memory"
)

// Главный тест: мир ДОЛЖЕН быть живым, а не застыть в «все idler сидят в таверне».
// Прокручиваем годы и проверяем признаки жизни: оборот поколений, разброс возрастов,
// разные цели, уцелевшие деревни, вменяемое население.
func TestWorldIsAlive(t *testing.T) {
	w, _ := Generate("seed-alive", 6)
	r := core.NewRunner("seed-alive", Default()...)

	start := w.Count()
	// запомним исходные id жителей, чтобы увидеть оборот поколений
	origIDs := map[core.ID]bool{}
	w.Each("ident", func(e *core.Entity) { origIDs[e.ID] = true })
	firstNextID := w.NextID

	r.Run(w, 3000*core.HoursPerDay) // ~3000 дней

	// 1) мир не вымер и не взорвался
	people := 0
	w.Each("ident", func(e *core.Entity) { people++ })
	if people == 0 {
		t.Fatalf("мир вымер — это не живой мир")
	}
	if people > 500 {
		t.Fatalf("население взорвалось (%d) — нет баланса", people)
	}

	// 2) оборот поколений: часть исходных умерла И появились новые id (рождения)
	survivors := 0
	maxID := core.ID(0)
	w.Each("ident", func(e *core.Entity) {
		if origIDs[e.ID] {
			survivors++
		}
		if e.ID > maxID {
			maxID = e.ID
		}
	})
	if survivors >= len(origIDs) {
		t.Fatalf("никто не умер за 3000 дней — нет смертности (было %d, выжило %d)", len(origIDs), survivors)
	}
	if maxID < firstNextID {
		t.Fatalf("не родилось ни одной новой души — нет рождений")
	}
	t.Logf("старт %d → сейчас %d; из %d исходных выжило %d; оборот есть",
		start, people, len(origIDs), survivors)

	// 3) возрасты разбросаны (не клоны одного возраста)
	var ages []float64
	w.Each("vitals", func(e *core.Entity) { ages = append(ages, core.Vitals(e).Age) })
	if stddev(ages) < 3.0 {
		t.Fatalf("возрасты почти одинаковы (σ=%.2f) — мир не живёт по-настоящему", stddev(ages))
	}

	// 4) цели разные — НЕ все сидят в таверне (главная болезнь прототипа)
	goals := map[string]int{}
	w.Each("mind", func(e *core.Entity) { goals[core.Mind(e).Goal]++ })
	if len(goals) < 3 {
		t.Fatalf("у всех почти одна цель %v — опять мёртвое равновесие", goals)
	}
	t.Logf("возраст σ=%.1f; цели=%v", stddev(ages), goals)

	// 5) хоть одно поселение уцелело с жителями
	liveSettle := 0
	w.Each("settlement", func(se *core.Entity) {
		if len(residentsOf(w, se.ID)) > 0 {
			liveSettle++
		}
	})
	if liveSettle == 0 {
		t.Fatalf("все поселения опустели")
	}
}

// Память без потерь: сохранили → загрузили → мир побайтно тот же, и прокрутка совпадает.
func TestLosslessSnapshot(t *testing.T) {
	w, hub := Generate("seed-mem", 4)
	r := core.NewRunnerWith(hub, core.Clock{}, Default()...)
	r.Run(w, 300*core.HoursPerDay) // пожили, набрались состояния

	blob, err := memory.Save(w, r.Clock, r.Hub)
	if err != nil {
		t.Fatalf("Save: %v", err)
	}
	w2, clock2, hub2, err := memory.Load(blob)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}

	// 1) снимок загруженного мира побайтно равен исходному (ничего не потеряно/не переставлено)
	a, _ := w.Dump(nil)
	b, _ := w2.Dump(nil)
	ab, _ := jsonM(a)
	bb, _ := jsonM(b)
	if !bytes.Equal(ab, bb) {
		t.Fatalf("загруженный мир отличается от сохранённого — память теряет данные")
	}
	if w.Count() != w2.Count() {
		t.Fatalf("разное число сущностей после загрузки: %d vs %d", w.Count(), w2.Count())
	}

	// 2) продолжение даёт тот же результат (детерминизм костей переживает сохранение)
	r1 := core.NewRunnerWith(r.Hub, r.Clock, Default()...)
	r2 := core.NewRunnerWith(hub2, clock2, Default()...)
	r1.Run(w, 100*core.HoursPerDay)
	r2.Run(w2, 100*core.HoursPerDay)
	d1, _ := w.Dump(nil)
	d2, _ := w2.Dump(nil)
	m1, _ := jsonM(d1)
	m2, _ := jsonM(d2)
	if !bytes.Equal(m1, m2) {
		t.Fatalf("после загрузки мир развивается иначе — сломан детерминизм")
	}
}

func stddev(xs []float64) float64 {
	if len(xs) < 2 {
		return 0
	}
	mean := 0.0
	for _, x := range xs {
		mean += x
	}
	mean /= float64(len(xs))
	v := 0.0
	for _, x := range xs {
		v += (x - mean) * (x - mean)
	}
	return math.Sqrt(v / float64(len(xs)))
}

func jsonM(v any) ([]byte, error) { return json.Marshal(v) }
