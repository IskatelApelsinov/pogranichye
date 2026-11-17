package dice

import (
	"math"
	"testing"
)

// Одно семя — одна последовательность (воспроизводимость, иначе кости бессмысленны).
func TestDeterminism(t *testing.T) {
	a := NewStream(Seed64("world", "combat", "7"))
	b := NewStream(Seed64("world", "combat", "7"))
	for i := 0; i < 1000; i++ {
		if a.next() != b.next() {
			t.Fatalf("потоки с одним семенем разошлись на шаге %d", i)
		}
	}
}

// Разные ключи — разные потоки (добавил систему — не сдвинул чужую случайность).
func TestStreamsIndependent(t *testing.T) {
	h := NewHub("w-1")
	x := h.Stream("hunt")
	y := h.Stream("trade")
	same := 0
	for i := 0; i < 200; i++ {
		if x.Roll(6) == y.Roll(6) {
			same++
		}
	}
	// при независимости совпадений ~1/6; если потоки склеены — было бы 200
	if same > 80 {
		t.Fatalf("потоки подозрительно похожи: %d/200 совпадений", same)
	}
}

// Float держится в [0,1), Between — в границах включительно.
func TestRanges(t *testing.T) {
	s := NewStream(Seed64("w", "r"))
	for i := 0; i < 5000; i++ {
		f := s.Float()
		if f < 0 || f >= 1 {
			t.Fatalf("Float вне [0,1): %v", f)
		}
		n := s.Between(3, 9)
		if n < 3 || n > 9 {
			t.Fatalf("Between вне [3,9]: %d", n)
		}
	}
}

// Кость d6 примерно равномерна (хи-квадрат-подобная грубая проверка).
func TestRollUniform(t *testing.T) {
	s := NewStream(Seed64("w", "fair"))
	var cnt [7]int
	const N = 60000
	for i := 0; i < N; i++ {
		cnt[s.Roll(6)]++
	}
	exp := float64(N) / 6
	chi := 0.0
	for face := 1; face <= 6; face++ {
		d := float64(cnt[face]) - exp
		chi += d * d / exp
	}
	// 5 степеней свободы: критическое ~15 при p=0.01; с запасом берём 20
	if chi > 20 {
		t.Fatalf("d6 неравномерна, хи-квадрат=%.2f, распределение=%v", chi, cnt[1:])
	}
}

// Взвешенный выбор уважает веса: индекс 2 (вес 7) должен доминировать.
func TestWeighted(t *testing.T) {
	s := NewStream(Seed64("w", "wt"))
	w := []float64{1, 0, 7, 2}
	var cnt [4]int
	for i := 0; i < 10000; i++ {
		cnt[s.Weighted(w)]++
	}
	if cnt[1] != 0 {
		t.Fatalf("нулевой вес не должен выпадать, а выпал %d раз", cnt[1])
	}
	if cnt[2] < cnt[0] || cnt[2] < cnt[3] {
		t.Fatalf("тяжёлый индекс не доминирует: %v", cnt)
	}
}

// Снимок хаба возобновляет ТУ ЖЕ последовательность после «сохранения/загрузки».
func TestHubSnapshotRestore(t *testing.T) {
	h := NewHub("seed-xyz")
	a := h.Stream("sim", "0")
	for i := 0; i < 50; i++ {
		a.next()
	}
	snap := h.Snapshot()
	want := a.Roll(20)

	h2 := LoadHub(snap)
	got := h2.Stream("sim", "0").Roll(20)
	if want != got {
		t.Fatalf("после восстановления бросок разошёлся: want %d got %d", want, got)
	}
}

// Normal даёт разумный центр (не математическая строгость, просто не улетает).
func TestNormalCentered(t *testing.T) {
	s := NewStream(Seed64("w", "n"))
	sum := 0.0
	const N = 20000
	for i := 0; i < N; i++ {
		sum += s.Normal(50, 10)
	}
	mean := sum / N
	if math.Abs(mean-50) > 2 {
		t.Fatalf("Normal уехал: среднее %.2f (ждали ~50)", mean)
	}
}
