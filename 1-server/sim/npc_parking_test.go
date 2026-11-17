package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

// parkingSlot должен давать РАЗНЫЕ, непересекающиеся точки для соседних рангов (борьба со слипанием).
func TestParkingSlotsDistinct(t *testing.T) {
	const n = 30
	pts := make([][2]float64, n)
	for r := 0; r < n; r++ {
		dx, dy := parkingSlot(r)
		pts[r] = [2]float64{dx, dy}
	}
	for i := 0; i < n; i++ {
		for j := i + 1; j < n; j++ {
			d := math.Hypot(pts[i][0]-pts[j][0], pts[i][1]-pts[j][1])
			if d < 0.8 { // слоты должны отстоять друг от друга минимум ~0.8 м
				t.Fatalf("слоты ранга %d и %d слишком близко: %.2f м", i, j, d)
			}
		}
	}
}

// Жители, идущие к ОДНОМУ POI, расходятся по разным слотам и не встают в одну точку.
func TestNPCsDoNotStackAtSamePOI(t *testing.T) {
	w := core.NewWorld("t")
	var es []*core.Entity
	for i := 0; i < 8; i++ {
		es = append(es, villager(w, "smith", "work", 100)) // все к кузнице
	}
	for i := 0; i < 400; i++ {
		StepNPCMovement(w, 0.4, nil)
	}
	tx, ty := poiByKind("smithy")
	for _, e := range es { // все добрели к кузнице
		p := ScenePos(e)
		if d := math.Hypot(p.X-tx, p.Y-ty); d > 10 {
			t.Fatalf("кузнец не дошёл к кузнице: |до POI|=%.2f", d)
		}
	}
	for i := 0; i < len(es); i++ { // и стоят на РАЗНЫХ местах
		pi := ScenePos(es[i])
		for j := i + 1; j < len(es); j++ {
			pj := ScenePos(es[j])
			if d := math.Hypot(pi.X-pj.X, pi.Y-pj.Y); d < 0.5 {
				t.Fatalf("жители слиплись у POI: %.2f м между ними", d)
			}
		}
	}
}

// Слоты и застревание детерминированы: один и тот же мир даёт один и тот же результат.
func TestParkingDeterministic(t *testing.T) {
	run := func() [2]float64 {
		w := core.NewWorld("t")
		var last *core.Entity
		for i := 0; i < 6; i++ {
			last = villager(w, "farmer", "work", 100)
		}
		for i := 0; i < 50; i++ {
			StepNPCMovement(w, 0.4, nil)
		}
		p := ScenePos(last)
		return [2]float64{p.X, p.Y}
	}
	if run() != run() {
		t.Fatal("распределение по слотам должно быть детерминированным")
	}
}

// Stuck-таймер копит реальное время у преграды и обнуляется по прибытии к слоту.
func TestStuckTimerResetsOnArrival(t *testing.T) {
	w := core.NewWorld("t")
	e := villager(w, "smith", "work", 100)
	for i := 0; i < 400; i++ {
		StepNPCMovement(w, 0.4, nil)
	}
	p := ScenePos(e)
	if p.Stuck != 0 {
		t.Fatalf("дошедший до слота житель не должен числиться застрявшим: Stuck=%.2f", p.Stuck)
	}
}
