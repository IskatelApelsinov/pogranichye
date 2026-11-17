package sim

import (
	"math"

	"pogranichye/core"
	"pogranichye/mind"
)

// Силы, запал и режим жизни персонажа (v1.29; task_ai/024, comms 0271/0282).
//
// Запал (ScenePosC.Stam, 0..1) — дыхание на бегу. Раньше он копился только в ApplyMove, то есть пока
// клиент шлёт ввод: открыл меню — дыхание «замирало». Теперь бег тратит запал в ApplyMove, а копится
// он здесь, в серверном шаге реального времени, у всех тел, независимо от ввода.
//
// Силы (VitalsC.Energy, 0..100) — усталость за день. Тратятся трудом и бодрствованием, копятся сном
// и отдыхом. Правило и его причина теперь видны игроку (me.vigor), а режим «Отдых» реально восполняет.

const (
	restRate      = 3.0 // сил в час: сон ночью или отдых (как и раньше у всех жителей)
	roomRestRate  = 8.0 // сил в час: сон/отдых в своей снятой комнате над корчмой
	exhaustedAt   = 10  // ниже — персонаж с выбранным режимом всё равно отдыхает: валится с ног
	starvingAt    = 75  // выше голод — идёт поесть, что бы ни было выбрано
	pinnedForever = 100000
)

// LifeModes — режимы, которые игрок может выбрать персонажу (порядок — для меню).
var LifeModes = []struct{ ID, Name, Hint string }{
	{"auto", "Сам решает", "живёт как житель: работает днём, спит ночью, ест, когда голоден"},
	{"work", "Работа", "трудится по ремеслу и зарабатывает; ночью всё равно спит"},
	{"rest", "Отдых", "отдыхает и спит — силы восполняются"},
	{"eat", "Еда", "ест из своих запасов, иначе покупает"},
	{"socialize", "Общение", "проводит время с людьми в корчме"},
	{"wander", "Бродить", "гуляет по селу и окрестностям"},
}

func lifeModeName(id string) string {
	for _, m := range LifeModes {
		if m.ID == id {
			return m.Name
		}
	}
	return id
}

// SetLifeMode — выбор режима жизни (op:"goal"). Не одноразовое действие у места, а долгий приоритет
// персонажа, в том числе пока игрок не в сети. "auto" (или "") возвращает решение разуму.
func SetLifeMode(w *core.World, e *core.Entity, mode string, totalHours int64, hour int) (bool, string) {
	m := core.Mind(e)
	if m == nil {
		return false, "у персонажа нет разума"
	}
	if mode == "" {
		mode = "auto"
	}
	known := false
	for _, lm := range LifeModes {
		known = known || lm.ID == mode
	}
	if !known {
		return false, "такого режима нет"
	}
	m.RoutineGoal, m.RoutinePhase, m.RoutineSince, m.RoutineProgress = "", "", 0, 0
	if mode == "auto" {
		m.Pinned, m.GoalUntil = "", 0 // разум выберет цель в ближайший игровой час
		m.LastAct = "живёт своим умом"
		return true, "режим: сам решает"
	}
	m.Pinned, m.Goal, m.GoalUntil = mode, mode, totalHours+pinnedForever
	m.LastAct = "выбрано: " + lifeModeName(mode)
	return true, "режим: " + lifeModeName(mode)
}

// pinnedGoal — цель для персонажа с выбранным режимом в этот час: режим, кроме житейских исключений.
func pinnedGoal(m *core.MindC, v *core.VitalsC, hour int) string {
	switch {
	case v.Energy < exhaustedAt && m.Pinned != mind.GoalRest:
		return mind.GoalRest // валится с ног — сначала сон
	case v.Hunger >= starvingAt && m.Pinned != mind.GoalEat:
		return mind.GoalEat
	case m.Pinned == mind.GoalWork && (hour >= 22 || hour < 6):
		return mind.GoalRest // ночью работник спит
	case m.Pinned == mind.GoalEat && v.Hunger < 25:
		return mind.GoalRest // сыт — ждёт следующей еды отдыхом, а не проедает запас
	}
	return m.Pinned
}

// LifeModeView — me.mode: что выбрано, чем персонаж занят сейчас и какие режимы есть.
func LifeModeView(e *core.Entity) any {
	m := core.Mind(e)
	if m == nil {
		return nil
	}
	sel := m.Pinned
	if sel == "" {
		sel = "auto"
	}
	opts := make([]map[string]string, 0, len(LifeModes))
	for _, lm := range LifeModes {
		opts = append(opts, map[string]string{"id": lm.ID, "name": lm.Name, "hint": lm.Hint})
	}
	return map[string]any{"selected": sel, "goal": m.Goal, "act": m.LastAct, "act_code": m.ActCode,
		"deviates": m.Pinned != "" && m.Goal != m.Pinned, "options": opts}
}

func inRentedRoom(w *core.World, e *core.Entity) bool {
	sp := ScenePos(e)
	if sp == nil || sp.Indoor == "" {
		return false
	}
	in := false
	w.Each("room", func(re *core.Entity) {
		if r := Room(re); !in && r != nil && r.Tenant == e.ID && sp.Indoor == indoorTag(re.ID) {
			in = true
		}
	})
	return in
}

// EnergyRate — сколько сил в час персонаж сейчас набирает (+) или теряет (−) и почему.
func EnergyRate(w *core.World, e *core.Entity, hour int) (float64, string) {
	m := core.Mind(e)
	night := hour >= 22 || hour < 6
	resting := night || (m != nil && m.Goal == mind.GoalRest)
	switch {
	case resting && core.Place(e) != nil && core.Ident(e) != nil && inRentedRoom(w, e):
		return roomRestRate, "спит в своей комнате"
	case resting && night:
		return restRate, "ночной сон"
	case resting:
		return restRate, "отдыхает"
	}
	return -energyPerHour, "бодрствует и трудится"
}

// RegenStamina — запал копится по серверному времени у всех, кто не бежал последние полсекунды.
// Мёртвым — нет.
func RegenStamina(w *core.World, dt float64) {
	if dt <= 0 {
		return
	}
	w.Each("scenepos", func(e *core.Entity) {
		p := ScenePos(e)
		if p == nil || p.Stam >= 1 {
			return
		}
		if p.RunHold > 0 {
			p.RunHold -= dt
			return
		}
		if v := core.Vitals(e); v != nil && v.Health <= 0 {
			return
		}
		p.Stam = math.Min(1, p.Stam+stamRegen*dt)
	})
}

// VigorView — me.vigor: силы и запал простыми словами — сколько, куда идут и как восполнить.
func VigorView(w *core.World, e *core.Entity, hour int) any {
	v := core.Vitals(e)
	if v == nil {
		return nil
	}
	rate, why := EnergyRate(w, e, hour)
	out := map[string]any{"energy": math.Round(v.Energy), "per_hour": rate, "why": why,
		"tips": []string{
			"ночной сон или режим «Отдых»: +3 сил в час",
			"сон в своей снятой комнате над корчмой: +8 в час",
			"привал у родника или стоянки за околицей: сразу +30",
			"горячая еда (похлёбка, жаркое, каша): сразу прибавка",
		}}
	switch {
	case rate > 0 && v.Energy < 100:
		out["full_in_hours"] = math.Ceil((100 - v.Energy) / rate)
	case rate < 0 && v.Energy > 0:
		out["empty_in_hours"] = math.Floor(v.Energy / -rate)
	}
	if v.Energy < exhaustedAt {
		out["warning"] = "валится с ног: работать не может, пока не отдохнёт"
	}
	if p := ScenePos(e); p != nil {
		st := map[string]any{"now": math.Round(p.Stam*100) / 100, "regen_per_s": stamRegen, "drain_per_s": stamDrain}
		if p.Stam < 1 {
			st["full_in_s"] = math.Ceil((1-p.Stam)/stamRegen + math.Max(0, p.RunHold))
		}
		out["stamina"] = st
	}
	return out
}

// MeMovementView — me.movement: действующие скорости и можно ли сейчас бежать (comms 0271 §2).
// Склон и тропа меняются от шага к шагу — их правило клиент берёт из /api/world.movement.
func MeMovementView(e *core.Entity) any {
	p := ScenePos(e)
	if p == nil {
		return nil
	}
	mult, dev := 1.0, 1.0
	if p.Mult > 0 {
		mult = p.Mult
	}
	if p.Dev > 0 {
		dev = p.Dev
	}
	canRun := p.Stam > 0 && !(p.Mult > 0 && p.Mult < 1)
	runMult := 1.0
	if mult > 1 {
		runMult = mult
	}
	return map[string]any{"walk": r3(walkSpeed * mult * dev), "jog": r3(jogSpeed * runMult * dev), "run": r3(runSpeed * runMult * dev),
		"mult": r3(mult), "dev_mult": r3(dev), "can_run": canRun}
}
