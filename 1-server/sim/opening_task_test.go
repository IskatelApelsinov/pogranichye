package sim

import (
	"strings"
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

func assertOpeningTask(t *testing.T, got map[string]any, stage int, met, worked, drank, done bool) {
	t.Helper()
	if got["stage"] != stage || got["met"] != met || got["worked"] != worked ||
		got["drank"] != drank || got["done"] != done {
		t.Fatalf("unexpected opening task state: got=%+v want stage=%d met=%t worked=%t drank=%t done=%t",
			got, stage, met, worked, drank, done)
	}
	if text, ok := got["text"].(string); !ok || text == "" {
		t.Fatalf("opening task must always provide player-facing text, got=%+v", got["text"])
	}
}

func TestOpeningTaskAdvancesOnlyFromAuthoritativeState(t *testing.T) {
	w := core.NewWorld("opening-task-test")
	e := w.Spawn()

	// A fresh character must not appear to have met, worked, or drunk.
	assertOpeningTask(t, OpeningTask(w, e), 1, false, false, false, false)

	// Acquaintance is the only source of the first milestone.
	w.Attach(e, &AcquaintC{Known: map[core.ID]bool{7: true}})
	assertOpeningTask(t, OpeningTask(w, e), 2, true, false, false, false)

	// A real skill level advances the task; a goal string or client click cannot.
	w.Attach(e, &core.SkillsC{Skills: map[string]float64{"woodcutter": 0.5}})
	assertOpeningTask(t, OpeningTask(w, e), 3, true, true, false, false)

	// Drinking alone cannot complete the task before acquaintance and work.
	other := w.Spawn()
	EnsurePlayer(w, other).DrankBeer = true
	assertOpeningTask(t, OpeningTask(w, other), 1, false, false, true, false)

	// The final milestone is the server-owned DrankBeer fact.
	EnsurePlayer(w, e).DrankBeer = true
	assertOpeningTask(t, OpeningTask(w, e), 3, true, true, true, true)
}

// #80: me.task carries the REAL issued errand (giver/id/acceptance/objective/progress/reward), and the
// work milestone is the server turn-in of that task — not a local click or a derived checklist.
func TestOpeningTaskSurfacesIssuedErrand(t *testing.T) {
	w, e, se := starterPlayer(t)
	var npc *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if v := core.Vitals(r); npc == nil && r.ID != e.ID && v != nil && v.Age >= 18 {
			npc = r
		}
	}
	np := EnsureScenePos(w, npc)
	p := ScenePos(e)
	np.X, np.Y = p.X+1, p.Y
	nst := ensureStashOf(w, npc)
	nst.Goods[econ.GoodWood], nst.Goods[econ.GoodFish] = 0, 10 // у жителя нет дров — настоящая нужда
	core.Purse(npc).Coins = 7

	// Нет выданной задачи — issued == nil, работа ещё не сделана по-серверному.
	task := OpeningTask(w, e)
	if task["issued"] != nil || task["errand_done"] != false {
		t.Fatalf("без поручения issued должен быть nil, errand_done false: %+v", task)
	}

	// Берём настоящее поручение у знакомого жителя через разговор.
	StartTalk(w, e, npc.ID)
	Say(w, e, "greet", "e1")
	if ok, why := Say(w, e, "work", "e2"); !ok {
		t.Fatalf("взять поручение: %s", why)
	}

	// me.task теперь несёт САМУ задачу: giver/id/accepted/objective/have/reward.
	task = OpeningTask(w, e)
	iss, ok := task["issued"].(map[string]any)
	if !ok {
		t.Fatalf("issued должен быть объектом выданной задачи: %+v", task["issued"])
	}
	if iss["giver"] != eidStr(npc.ID) || iss["accepted"] != true || iss["good"] != econ.GoodWood ||
		iss["qty"] != 2 || iss["have"] != 0 || iss["ready"] != false || iss["coins"] != 2.0 {
		t.Fatalf("issued должен описывать реальное поручение (2 дрова, 2 монеты, 0 принесено): %+v", iss)
	}
	if id, _ := iss["id"].(string); !strings.HasPrefix(id, "errand:") {
		t.Fatalf("у выданной задачи должен быть серверный id: %+v", iss["id"])
	}
	if task["errand_done"] != false {
		t.Fatal("пока не сдал — работа не засчитана")
	}

	// Принёс дрова — прогресс реальный (сколько реально в закромах), задача готова к сдаче.
	addStash(w, e, econ.GoodWood, 3)
	iss = OpeningTask(w, e)["issued"].(map[string]any)
	if iss["have"] != 3 || iss["ready"] != true {
		t.Fatalf("после доставки have=3 ready=true: %+v", iss)
	}

	// Сдал поручение (серверный turn-in) — issued очищается, worked/errand_done становятся true.
	if ok, why := Say(w, e, "work", "e3"); !ok {
		t.Fatalf("сдать поручение: %s", why)
	}
	task = OpeningTask(w, e)
	if task["issued"] != nil || task["errand_done"] != true || task["worked"] != true || task["met"] != true {
		t.Fatalf("после сдачи: issued nil, errand_done/worked/met true: %+v", task)
	}
}
