package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// comms 0219 — серверное население поселений и бандиты. ChatGPT просил ПРЕДМЕТНУЮ проверку, что
// лихие люди у стартового села — настоящие сущности foe с устойчивым id, kind=bandit, x/y/hp/state
// (а не только текстовое "событие" на тракте — то отдельный, абстрактный op:journey, у которого в
// принципе нет сцены/ScenePos, так что place.foes там не применим). populateFoes (combat.go) уже
// поднимает бандитов из FoeCampC в сцену ГОРЯЧЕГО села — здесь это утверждение проверяется явно.
func TestBanditFoeEntityStableAcrossFrames(t *testing.T) {
	w, e, se := starterPlayer(t)
	ctl := func(id core.ID) bool { return id == e.ID }
	camp := &FoeCampC{Init: true, Bandits: 2, Timer: 0}
	if se.Get("foecamp") == nil {
		w.Attach(se, camp)
	} else {
		*se.Get("foecamp").(*FoeCampC) = *camp
	}
	s := dice.NewStream(7)
	StepCombat(w, 0.1, ctl, s) // первый кадр — лихие люди должны подняться в сцену

	var id core.ID
	zone := wildZones["bandit"][0]
	w.Each("foe", func(fe *core.Entity) {
		f := Foe(fe)
		if f.Species != "bandit" || f.Settlement != se.ID {
			return
		}
		id = fe.ID
		if f.HP <= 0 || f.Max <= 0 || f.State == "" {
			t.Fatalf("бандит без валидного hp/max/state: %+v", f)
		}
		if f.X < zone.x0 || f.X > zone.x1 || f.Y < zone.y0 || f.Y > zone.y1 {
			t.Fatalf("бандит появился не в своей дикой зоне %+v: (%.1f,%.1f)", zone, f.X, f.Y)
		}
	})
	if id == 0 {
		t.Fatal("ожидался хотя бы один настоящий foe-бандит рядом со стартовым селом")
	}
	before := FoesView(w, se.ID)
	if len(before) == 0 {
		t.Fatal("place.foes пуст, хотя бандит есть в мире")
	}

	// несколько следующих кадров — тот же ID не исчезает и не сменяется новым (comms 0219 п.4:
	// "same bandit stable ID persists across consecutive frames").
	for i := 0; i < 5; i++ {
		StepCombat(w, 0.1, ctl, s)
		found := false
		for _, m := range FoesView(w, se.ID) {
			if m["id"].(string) == eidStr(id) {
				found = true
				break
			}
		}
		if !found {
			t.Fatalf("бандит %s пропал из place.foes на кадре %d", eidStr(id), i)
		}
	}
}

// TestDeadBanditDoesNotResurrectSameID — comms 0219 п.5: мёртвый foe не "оживает" под тем же ID.
// Сервер оставляет труп на decay-таймер (f.Dead, combat.go), затем удаляет сущность; пополнение
// стана идёт медленно (camp.Bandits += dt/600) отдельной НОВОЙ сущностью, а не тем же ID.
func TestDeadBanditDoesNotResurrectSameID(t *testing.T) {
	w, e, se := starterPlayer(t)
	zone := wildZones["bandit"][0]
	bx, by := (zone.x0+zone.x1)/2, (zone.y0+zone.y1)/2
	fe := w.Spawn(&FoeC{Species: "bandit", Settlement: se.ID, X: bx, Y: by, HomeX: bx, HomeY: by, HP: 45, Max: 45, State: "roam"})
	w.Attach(se, &FoeCampC{Init: true, Bandits: 1, Timer: 1e9}) // не даём populateFoes поднять новых пока проверяем труп
	killFoe(w, fe, Foe(fe), e)
	if Foe(fe).State != "dead" {
		t.Fatal("killFoe должен перевести бандита в state=dead")
	}
	ctl := func(core.ID) bool { return false } // без игроков в сцене — decay идёт, populateFoes не зовётся
	for i := 0; i < 10; i++ {
		StepCombat(w, 3.0, ctl, dice.NewStream(3)) // 10*3с > таймера распада (25с)
		if w.Get(fe.ID) == nil {
			break
		}
	}
	if w.Get(fe.ID) != nil {
		t.Fatal("труп должен быть убран по истечении decay-таймера")
	}
	w.Each("foe", func(other *core.Entity) {
		if other.ID == fe.ID {
			t.Fatal("тот же ID не должен существовать повторно")
		}
	})
}
