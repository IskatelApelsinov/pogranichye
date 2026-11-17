package sim

import (
	"math"
	"testing"

	"pogranichye/core"
	"pogranichye/mind"
)

// Запал копится по серверному времени, даже если клиент не шлёт ни одного ввода (меню открыто),
// не копится полсекунды после бега и у мёртвых; на нуле бег не ускоряет.
func TestStaminaRegensWithoutInput(t *testing.T) {
	w, _ := GenerateStarter("vigor")
	e := w.Spawn(&ScenePosC{Stam: 0.2}, &core.VitalsC{Health: 100, Energy: 50})
	for i := 0; i < 30; i++ { // 3 с без единого move-ввода
		RegenStamina(w, 0.1)
	}
	if got := ScenePos(e).Stam; math.Abs(got-0.5) > 0.001 {
		t.Fatalf("за 3 с запал должен вырасти на 0,3: %.3f", got)
	}
	p := ScenePos(e)
	ApplyMove(nil, p, 1, 0, true, 0, 0.1, 1) // бег
	after := p.Stam
	for i := 0; i < 4; i++ { // 0,4 с — ещё пауза после бега
		RegenStamina(w, 0.1)
	}
	if p.Stam != after {
		t.Fatalf("сразу после бега запал не копится: %.3f → %.3f", after, p.Stam)
	}
	for i := 0; i < 200; i++ {
		RegenStamina(w, 0.1)
	}
	if p.Stam != 1 {
		t.Fatalf("запал не дорос до полного: %.3f", p.Stam)
	}
	p.Stam, p.X = 0, 0
	ApplyMove(nil, p, 1, 0, true, 0, 0.1, 2)
	if p.X > walkSpeed*0.1+1e-9 {
		t.Fatalf("на нуле запала бег не должен ускорять: %.3f", p.X)
	}
	core.Vitals(e).Health = 0
	p.RunHold = 0
	RegenStamina(w, 1)
	if p.Stam != 0 {
		t.Fatal("мёртвому запал не копится")
	}
}

// Режим жизни: выбранный держится в часах мира, "auto" возвращает решение разуму, валящийся с ног
// работник отдыхает, кривой режим не принимается; отдых правда восполняет силы.
func TestLifeModeHoldsAndRestRestores(t *testing.T) {
	w, hub := GenerateStarter("modes")
	var e *core.Entity
	w.Each("mind", func(x *core.Entity) {
		if e == nil && core.Vitals(x) != nil && core.Ident(x) != nil && core.Vitals(x).Age >= 18 {
			e = x
		}
	})
	if ok, _ := SetLifeMode(w, e, "fly", 0, 12); ok {
		t.Fatal("неизвестный режим принят")
	}
	if ok, _ := SetLifeMode(w, e, "rest", 0, 12); !ok {
		t.Fatal("режим «Отдых» не принят")
	}
	r := core.NewRunnerWith(hub, core.Clock{TotalHours: 10}, Default()...) // днём
	core.Vitals(e).Energy = 40
	for i := 0; i < 5; i++ {
		r.Step(w)
	}
	m := core.Mind(e)
	if m.Pinned != "rest" || m.Goal != mind.GoalRest {
		t.Fatalf("режим не держится: pinned=%q goal=%q", m.Pinned, m.Goal)
	}
	if got := core.Vitals(e).Energy; got < 40+restRate*4 {
		t.Fatalf("за 5 ч отдыха днём силы должны прибавить: %.1f", got)
	}
	SetLifeMode(w, e, "work", r.Clock.TotalHours, r.Clock.Hour())
	core.Vitals(e).Energy = 5
	r.Step(w)
	if m.Goal != mind.GoalRest || m.Pinned != "work" {
		t.Fatalf("валящийся с ног работник должен отдыхать, не меняя выбора: %q/%q", m.Goal, m.Pinned)
	}
	if v, _ := LifeModeView(e).(map[string]any); v["selected"] != "work" || v["deviates"] != true {
		t.Fatalf("me.mode: %v", v)
	}
	SetLifeMode(w, e, "auto", r.Clock.TotalHours, r.Clock.Hour())
	if m.Pinned != "" || m.GoalUntil != 0 {
		t.Fatal("«Сам решает» должно вернуть решение разуму")
	}
	r.Step(w)
	if m.Goal == "" {
		t.Fatal("разум не выбрал цель после «Сам решает»")
	}
	if VigorView(w, e, 3).(map[string]any)["per_hour"].(float64) <= 0 {
		t.Fatal("ночью силы должны копиться")
	}
}
