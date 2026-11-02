package sim

import (
	"math"
	"testing"
)

// Шаг на восток за один тик сдвигает на walkSpeed*dt; взгляд и seq обновляются.
// dt клампится до moveMaxDt (защита от читерских dt), поэтому сверяемся именно с ним.
func TestApplyMoveWalks(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	ApplyMove(p, 1, 0, false, 45, moveMaxDt, 1)
	if math.Abs(p.X-walkSpeed*moveMaxDt) > 1e-6 || p.Y != 0 {
		t.Fatalf("шаг на восток ≈ walkSpeed*dt: x=%.3f y=%.3f", p.X, p.Y)
	}
	if p.Facing != 45 || p.Seq != 1 {
		t.Fatalf("взгляд/seq должны обновиться: facing=%.0f seq=%d", p.Facing, p.Seq)
	}
}

// Большой dt клампится: за один вызов нельзя «проскочить» дальше walkSpeed*moveMaxDt.
func TestMoveClampsDt(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	ApplyMove(p, 1, 0, false, 0, 100.0, 1) // якобы 100 секунд за один кадр
	if p.X > walkSpeed*moveMaxDt+1e-6 {
		t.Fatalf("огромный dt должен клампиться: x=%.3f", p.X)
	}
}

// Длина вектора ввода не влияет на скорость (нельзя «разогнаться» большим вектором).
func TestMoveNormalized(t *testing.T) {
	a := &ScenePosC{Stam: 1}
	b := &ScenePosC{Stam: 1}
	ApplyMove(a, 1, 0, false, 0, 1.0, 1)
	ApplyMove(b, 99, 0, false, 0, 1.0, 1) // огромный вектор
	if math.Abs(a.X-b.X) > 1e-9 {
		t.Fatalf("скорость не должна зависеть от длины ввода: %.3f vs %.3f", a.X, b.X)
	}
}

// Бег тратит запал и быстрее шага; стоя — запал восстанавливается.
func TestRunDrainsStamina(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	ApplyMove(p, 1, 0, true, 0, moveMaxDt, 1)
	if p.X <= walkSpeed*moveMaxDt {
		t.Fatalf("бег должен быть быстрее шага: x=%.3f", p.X)
	}
	if p.Stam >= 1 {
		t.Fatalf("бег должен тратить запал: stam=%.3f", p.Stam)
	}
	before := p.Stam
	ApplyMove(p, 0, 0, false, 0, moveMaxDt, 2) // стоим
	if p.Stam <= before {
		t.Fatal("стоя запал должен восстанавливаться")
	}
}

// Движение держится в границах площадки (простая «коллизия» границами).
func TestSceneBounds(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	for i := 0; i < 1000; i++ {
		ApplyMove(p, 1, 0, false, 0, moveMaxDt, int64(i+1))
	}
	if p.X > SceneRadius+1e-6 {
		t.Fatalf("за границу площадки выходить нельзя: x=%.3f (R=%.0f)", p.X, SceneRadius)
	}
}

// Устаревший (меньший) seq не откатывает подтверждённый номер ввода.
func TestSeqMonotonic(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	ApplyMove(p, 1, 0, false, 0, 0.1, 10)
	ApplyMove(p, 1, 0, false, 0, 0.1, 3) // пришёл с опозданием
	if p.Seq != 10 {
		t.Fatalf("seq не должен откатываться: %d", p.Seq)
	}
}
