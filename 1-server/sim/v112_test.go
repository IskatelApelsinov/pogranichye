package sim

import (
	"testing"

	"pogranichye/arcane"
	"pogranichye/core"
	"pogranichye/dice"
)

func TestGiftGateAndLearningByLabor(t *testing.T) {
	w, e, se := starterPlayer(t)
	s := dice.NewStream(3)
	Player(e).Mana = 100
	if ok, why := WeaveCast(w, e, []string{"knot", "thread", "barb", "seal"}, 0, "g1", s); ok || why == "плетение сорвалось" {
		t.Fatalf("без дара сложный узор недоступен вовсе: ok=%v %q", ok, why)
	}
	if Player(e).Mana != 100 {
		t.Fatal("недоступный узор не должен тратить ману")
	}
	if ok, _ := weaveGate(e, 3); !ok {
		t.Fatal("лёгкий узор доступен и без дара")
	}
	// учиться негде — отказ; в месте силы — навык растёт; упорный открывает дар
	core.Settle(se).Mana = 0.5
	if ok, _ := Meditate(w, e, "m0"); ok && teacherNear(w, e) == 0 {
		t.Fatal("без места силы и мага учиться нечему")
	}
	core.Settle(se).Mana = 1.6
	for i := 0; i < 400 && !HasGift(e); i++ {
		core.Vitals(e).Energy = 100
		Meditate(w, e, "m"+roman(i%16+1)+string(rune('a'+i%26))+string(rune('a'+i/26)))
	}
	if !HasGift(e) || skillOf(e, "mage") < awakenSkill {
		t.Fatalf("упорный бездарный должен открыть дар трудом: навык %.1f", skillOf(e, "mage"))
	}
	if Stat(e, "intellect") <= statBase || Stat(e, "will") <= statBase {
		t.Fatal("учение должно растить ум и волю")
	}
	if ok, _ := weaveGate(e, 6); !ok {
		t.Fatal("с даром сложные узоры доступны")
	}
	_ = arcane.Capacity
}

func TestStatsGrowFromWorkWithoutCap(t *testing.T) {
	w, e, _ := starterPlayer(t)
	base := Stat(e, "strength")
	var prevGain float64
	for round := 0; round < 3; round++ {
		before := Stat(e, "strength")
		for i := 0; i < 200; i++ {
			trainSkill(w, e, "woodcutter", 1)
		}
		gain := Stat(e, "strength") - before
		if gain <= 0 {
			t.Fatal("труд всегда растит силу — потолка нет")
		}
		if round > 0 && gain >= prevGain {
			t.Fatal("чем выше характеристика, тем медленнее рост")
		}
		prevGain = gain
	}
	if Stat(e, "strength") <= base+1 {
		t.Fatal("сила должна заметно вырасти")
	}
}

func TestRoadsConnectAndJourneyFollowsThem(t *testing.T) {
	w, e, se := starterPlayer(t)
	roads := RoadGraph(w, false)
	var ids []core.ID
	w.Each("settlement", func(o *core.Entity) {
		if !isOuter(w, core.Settle(o)) {
			ids = append(ids, o.ID)
		}
	})
	far, farLen := core.ID(0), 0.0
	for _, id := range ids {
		if id == se.ID {
			continue
		}
		path, l := RoadRoute(roads, se.ID, id)
		if len(path) < 2 {
			t.Fatalf("сеть трактов должна быть связной: до %d пути нет", id)
		}
		if l > farLen {
			far, farLen = id, l
		}
	}
	path, _ := RoadRoute(roads, se.ID, far)
	s := dice.NewStream(5)
	if len(path) > 2 {
		if ok, _ := JourneyStart(w, e, far, "r1", s); ok {
			t.Fatal("в неизвестное дальнее село дороги не знаешь")
		}
		LearnKnown(w, e, far)
	}
	if ok, why := JourneyStart(w, e, far, "r2", s); !ok {
		t.Fatalf("в известное село путь есть: %s", why)
	}
	if got := len(journeyOf(e).Via); got != len(path)-2 {
		t.Fatalf("путь должен идти через %d сёл, идёт через %d", len(path)-2, got)
	}
	// карта края: неизвестные соседи без имени
	w.Detach(e, "journey")
	for _, n := range RegionView(w, e)["nodes"].([]map[string]any) {
		if n["state"] == "unknown" && n["name"] != "" {
			t.Fatal("имя неизвестного села не должно попадать в кадр")
		}
	}
}

func TestBeerMakesDrunkAndItPasses(t *testing.T) {
	w, e, _ := starterPlayer(t)
	addDrunk(w, e, 22)
	if Drunk(e) <= 0 {
		t.Fatal("после кружки есть хмель")
	}
	for i := 0; i < 5; i++ {
		addDrunk(w, e, 22)
	}
	if drunkPenalty(e) <= 0 {
		t.Fatal("тяжёлый хмель мешает")
	}
	SoberUp(w, 2000)
	if Drunk(e) != 0 {
		t.Fatal("хмель выветривается")
	}
}
