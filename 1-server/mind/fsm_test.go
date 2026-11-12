package mind

import (
	"testing"

	"pogranichye/core"
)

func routineEntity(goal string) *core.Entity {
	w := core.NewWorld("routine-test")
	e := w.Spawn(&core.MindC{Goal: goal}, &core.VitalsC{Hunger: 70, Energy: 50, Morale: 50}, &core.DrivesC{Social: 70}, &core.HomeC{Status: "none"})
	return e
}

func TestRoutinePrepareThenPerform(t *testing.T) {
	e := routineEntity(GoalWork)
	if phase, done := AdvanceRoutine(e, 10, 12); phase != "prepare" || done {
		t.Fatalf("first transition = %q,%v; want prepare,false", phase, done)
	}
	if phase, done := AdvanceRoutine(e, 11, 13); phase != "perform" || done {
		t.Fatalf("second transition = %q,%v; want perform,false", phase, done)
	}
	m := core.Mind(e)
	if m.RoutineProgress != 0 {
		t.Fatalf("prepare transition must not count work progress: %d", m.RoutineProgress)
	}
	if phase, done := AdvanceRoutine(e, 12, 14); phase != "perform" || done {
		t.Fatalf("perform transition = %q,%v; want perform,false", phase, done)
	}
	if m.RoutineProgress != 1 {
		t.Fatalf("one performance hour should advance progress, got %d", m.RoutineProgress)
	}
}

func TestRoutineCompletesWhenNeedIsSatisfied(t *testing.T) {
	e := routineEntity(GoalEat)
	AdvanceRoutine(e, 1, 12) // prepare
	AdvanceRoutine(e, 2, 13) // perform
	core.Vitals(e).Hunger = 10
	if phase, done := AdvanceRoutine(e, 3, 14); phase != "complete" || !done {
		t.Fatalf("satisfied eating routine = %q,%v; want complete,true", phase, done)
	}
}

func TestRoutineInterruptsExhaustedWorker(t *testing.T) {
	e := routineEntity(GoalWork)
	AdvanceRoutine(e, 1, 12)
	AdvanceRoutine(e, 2, 13)
	core.Vitals(e).Energy = 5
	if phase, done := AdvanceRoutine(e, 3, 14); phase != "complete" || !done {
		t.Fatalf("exhausted work = %q,%v; want complete,true", phase, done)
	}
}

func TestRoutineResetsForNewGoal(t *testing.T) {
	e := routineEntity(GoalWork)
	AdvanceRoutine(e, 1, 12)
	m := core.Mind(e)
	m.Goal = GoalRest
	if phase, done := AdvanceRoutine(e, 2, 13); phase != "prepare" || done {
		t.Fatalf("new goal = %q,%v; want fresh prepare", phase, done)
	}
	if m.RoutineGoal != GoalRest || m.RoutineProgress != 0 {
		t.Fatalf("new goal did not reset routine: %+v", m)
	}
}
