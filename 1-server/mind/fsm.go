package mind

import "pogranichye/core"

// AdvanceRoutine records a deterministic execution phase for an NPC's current
// utility-selected goal. Effects remain owned by existing systems during this
// compatibility step; phase gating will be enabled goal-by-goal with E2E coverage.
func AdvanceRoutine(e *core.Entity, totalHours int64, hour int) (phase string, completed bool) {
	m := core.Mind(e)
	v := core.Vitals(e)
	if m == nil || v == nil || m.Goal == "" { return "", false }
	if m.RoutineGoal != m.Goal || m.RoutinePhase == "" {
		m.RoutineGoal, m.RoutinePhase = m.Goal, "prepare"
		m.RoutineSince, m.RoutineProgress = totalHours, 0
		return m.RoutinePhase, false
	}
	switch m.RoutinePhase {
	case "prepare":
		if totalHours > m.RoutineSince { m.RoutinePhase = "perform" }
	case "perform":
		m.RoutineProgress++
		switch m.Goal {
		case GoalEat:
			if v.Hunger < 25 { m.RoutinePhase = "complete" }
		case GoalRest:
			if v.Energy >= 85 || (hour < 22 && hour >= 6 && v.Energy >= 65) { m.RoutinePhase = "complete" }
		case GoalWork:
			if v.Energy <= 8 { m.RoutinePhase = "complete" }
		case GoalSocialize:
			if d := core.Drives(e); d != nil && d.Social <= 20 { m.RoutinePhase = "complete" }
		case GoalWander:
			if m.RoutineProgress >= 2 { m.RoutinePhase = "complete" }
		}
	case "complete":
		return m.RoutinePhase, true
	default:
		m.RoutinePhase, m.RoutineSince, m.RoutineProgress = "prepare", totalHours, 0
	}
	return m.RoutinePhase, m.RoutinePhase == "complete"
}
