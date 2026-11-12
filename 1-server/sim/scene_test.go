package sim

import (
	"math"
	"testing"
)

// Шаг на восток за один тик сдвигает на walkSpeed*dt; взгляд и seq обновляются.
// dt клампится до moveMaxDt (защита от читерских dt), поэтому сверяемся именно с ним.
func TestApplyMovePaceSpeeds(t *testing.T) {
	walk := &ScenePosC{Stam: 1}
	jog := &ScenePosC{Stam: 1}
	sprint := &ScenePosC{Stam: 1}
	ApplyMove(walk, 1, 0, false, 0, 0.2, 1)
	ApplyMove(jog, 1, 0, true, 0, 0.2, 1)
	ApplyMove(sprint, 1, 0, true, 0, 0.2, 1, true)
	if !(walk.X < jog.X && jog.X < sprint.X) {
		t.Fatalf("expected walk < jog < sprint, got %.3f, %.3f, %.3f", walk.X, jog.X, sprint.X)
	}
	if walk.X < 0.27 || walk.X > 0.29 {
		t.Fatalf("walking speed changed unexpectedly: %.3f m", walk.X)
	}
	if math.Abs(jog.X-0.64) > 1e-6 {
		t.Fatalf("light run should be 3.2 m/s: %.3f m in 0.2 s", jog.X)
	}
	if math.Abs(sprint.X-1.2) > 1e-6 {
		t.Fatalf("fast run should be 6.0 m/s: %.3f m in 0.2 s", sprint.X)
	}
}

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
func TestRunStaminaSupportsLongerSprint(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	for i := int64(1); i <= 20; i++ {
		ApplyMove(p, 1, 0, true, 0, moveMaxDt, i)
	}
	// Five seconds of uninterrupted running should consume about half the bar.
	if p.Stam < 0.49 || p.Stam > 0.51 {
		t.Fatalf("ожидали около половины запала после 5 с бега, получили %.3f", p.Stam)
	}
}

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

// Движение держится в границах мира (v1.3: прямоугольник [-WorldHalf,WorldHalf]).
func TestSceneBounds(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	for i := 0; i < 20000; i++ {
		ApplyMove(p, 1, 0, false, 0, moveMaxDt, int64(i+1))
	}
	if p.X > WorldHalf+1e-6 {
		t.Fatalf("за край мира выходить нельзя: x=%.3f (half=%.0f)", p.X, WorldHalf)
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

// #16: устаревший И равный seq не меняют НИ ОДНО поле состояния (не только Seq).
func TestStaleSeqNoStateChange(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	ApplyMove(p, 1, 0, true, 33, moveMaxDt, 5) // приняли seq=5
	snap := *p
	ApplyMove(p, 0, 1, true, 180, moveMaxDt, 5) // дубль (равный seq) — всё игнор
	if *p != snap {
		t.Fatalf("равный seq не должен менять состояние: было %+v стало %+v", snap, *p)
	}
	ApplyMove(p, 0, 1, true, 180, moveMaxDt, 2) // опоздавший (меньший) — тоже игнор
	if *p != snap {
		t.Fatalf("меньший seq не должен менять состояние: было %+v стало %+v", snap, *p)
	}
}

// #16: при начальном seq=0 первый ввод с seq=0 отвергается (первый принимаемый — строго >0).
func TestInitialSeqZeroRejected(t *testing.T) {
	p := &ScenePosC{Stam: 1}
	ApplyMove(p, 1, 0, false, 10, moveMaxDt, 0) // seq=0 при p.Seq=0 → отказ
	if p.X != 0 || p.Y != 0 || p.Facing != 0 || p.Seq != 0 {
		t.Fatalf("seq=0 не должен применяться первым: %+v", *p)
	}
}

// v1.3: игрок НЕ проходит сквозь здание — упираясь в стену корчмы, остаётся снаружи.
func TestNoWalkThroughWall(t *testing.T) {
	// корчма: X[-40,-26] Y[14,26]. Встаём точно под ней и ломимся на север сквозь стену.
	p := &ScenePosC{X: -33, Y: 10, Stam: 1}
	for i := 0; i < 400; i++ {
		ApplyMove(p, 0, 1, false, 90, moveMaxDt, int64(i+1))
	}
	if Blocked(p.X, p.Y) {
		t.Fatalf("тело оказалось внутри здания: (%.2f,%.2f)", p.X, p.Y)
	}
	if p.Y >= 14 { // не пробрался за южную стену корчмы
		t.Fatalf("прошёл сквозь стену корчмы: y=%.2f (стена с y=14)", p.Y)
	}
}

// v1.3: двигаясь вдоль стены (на север у края корчмы), тело скользит, а не застревает намертво.
func TestSlideAlongWall(t *testing.T) {
	p := &ScenePosC{X: -33, Y: 10, Stam: 1}
	// идём на северо-восток: север блокируется стеной, но на восток вдоль стены — скользим
	startX := p.X
	for i := 0; i < 200; i++ {
		ApplyMove(p, 1, 1, false, 45, moveMaxDt, int64(i+1))
	}
	if p.X <= startX {
		t.Fatalf("вдоль стены тело должно скользить на восток: x %.2f→%.2f", startX, p.X)
	}
	if Blocked(p.X, p.Y) {
		t.Fatalf("скольжение завело внутрь преграды: (%.2f,%.2f)", p.X, p.Y)
	}
}
