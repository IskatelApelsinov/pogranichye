package mind

import "pogranichye/core"

// AdvanceRoutine advances the persistent, server-side NPC finite-state routine.
// Goal selection remains utility-based (Decide); this FSM owns execution phases,
// interruption and completion. It is intentionally not part of the client wire frame.
func AdvanceRoutine(e *core.Entity, totalHours int64, hour int) (phase string, completed bool) {
	m := core.Mind(e)
	v := core.Vitals(e)
	if m == nil || v == nil || m.Goal == "" {
		return "", false
	}
	if m.RoutineGoal != m.Goal || m.RoutinePhase == "" {
		m.RoutineGoal = m.Goal
		m.RoutinePhase = "prepare"
		m.RoutineSince = totalHours
		m.RoutineProgress = 0
		return m.RoutinePhase, false
	}

	switch m.RoutinePhase {
	case "prepare":
		if totalHours > m.RoutineSince {
			m.RoutinePhase = "perform"
		}
	case "perform":
		m.RoutineProgress++
		switch m.Goal {
		case GoalEat:
			if v.Hunger < 25 { m.RoutinePhase = "complete" }
		case GoalRest:
			night := hour >= 22 || hour < 6
			if v.Energy >= 85 || (!night && v.Energy >= 65) { m.RoutinePhase = "complete" }
		case GoalWork:
			if v.Energy <= 8 { m.RoutinePhase = "complete" }
		case GoalSocialize:
			if d := core.Drives(e); d != nil && d.Social <= 20 { m.RoutinePhase = "complete" }
		case GoalSeekHome:
			if h := core.Home(e); h != nil && h.Status != "none" { m.RoutinePhase = "complete" }
		case GoalWander:
			if m.RoutineProgress >= 2 { m.RoutinePhase = "complete" }
		}
	case "break":
		if v.Energy >= 25 {
			m.RoutinePhase = "perform"
		}
	case "complete":
		return m.RoutinePhase, true
	default:
		m.RoutinePhase = "prepare"
		m.RoutineSince = totalHours
		m.RoutineProgress = 0
	}
	if m.RoutinePhase == "complete" {
		return m.RoutinePhase, true
	}
	return m.RoutinePhase, false
}
