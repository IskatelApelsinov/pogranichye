package mind

import (
 "testing"
 "pogranichye/core"
)
func TestRoutinePrepareThenPerform(t *testing.T) {
 w:=core.NewWorld("routine-test")
 e:=w.Spawn(&core.MindC{Goal:GoalWork},&core.VitalsC{Hunger:70,Energy:50,Morale:50},&core.DrivesC{Social:70})
 if p,d:=AdvanceRoutine(e,10,12);p!="prepare"||d {t.Fatalf("first = %q,%v",p,d)}
 if p,d:=AdvanceRoutine(e,11,13);p!="perform"||d {t.Fatalf("second = %q,%v",p,d)}
 m:=core.Mind(e); if m.RoutineProgress!=0 {t.Fatalf("prepare counted progress: %d",m.RoutineProgress)}
 if p,d:=AdvanceRoutine(e,12,14);p!="perform"||d {t.Fatalf("third = %q,%v",p,d)}
 if m.RoutineProgress!=1 {t.Fatalf("progress = %d",m.RoutineProgress)}
}
func TestRoutineCompletesOnNeedAndExhaustion(t *testing.T) {
 w:=core.NewWorld("routine-test")
 e:=w.Spawn(&core.MindC{Goal:GoalEat},&core.VitalsC{Hunger:70,Energy:50},&core.DrivesC{})
 AdvanceRoutine(e,1,12); AdvanceRoutine(e,2,13); core.Vitals(e).Hunger=10
 if p,d:=AdvanceRoutine(e,3,14);p!="complete"||!d {t.Fatalf("eat = %q,%v",p,d)}
 core.Mind(e).Goal=GoalWork; AdvanceRoutine(e,4,14); AdvanceRoutine(e,5,15); core.Vitals(e).Energy=5
 if p,d:=AdvanceRoutine(e,6,16);p!="complete"||!d {t.Fatalf("work = %q,%v",p,d)}
}
func TestRoutineResetsOnGoalChange(t *testing.T) {
 w:=core.NewWorld("routine-test")
 e:=w.Spawn(&core.MindC{Goal:GoalWork},&core.VitalsC{Energy:50},&core.DrivesC{})
 AdvanceRoutine(e,1,12); core.Mind(e).Goal=GoalRest
 if p,d:=AdvanceRoutine(e,2,13);p!="prepare"||d {t.Fatalf("new goal = %q,%v",p,d)}
 if m:=core.Mind(e);m.RoutineGoal!=GoalRest||m.RoutineProgress!=0 {t.Fatalf("not reset: %+v",m)}
}
