package sim

import (
	"testing"

	"pogranichye/core"
)

// Несколько лошадей: купить вторую можно; у каждой видно село и державу, где она осталась; выбрать
// можно любую свою; на чужую — нельзя; садясь в другом селе, всадник берёт ту, что стоит здесь.
func TestManyHorsesAndWhereTheyStay(t *testing.T) {
	w, e, se := starterPlayer(t)
	s := core.Settle(se)
	stableStock(s)
	s.Store[horseGood] = 5
	core.Purse(e).Coins = 500
	p := ScenePos(e)
	p.X, p.Y = layoutFor(w, e).POI(stablePOI)
	if ok, why := Horse(w, e, "buy", "", 0, "b1"); !ok {
		t.Fatalf("первая лошадь: %q", why)
	}
	if ok, why := Horse(w, e, "buy", "", 0, "b2"); !ok {
		t.Fatalf("вторую лошадь купить нельзя: %q", why)
	}
	hs := HorsesView(w, e)
	if len(hs) != 2 {
		t.Fatalf("ждали 2 лошади, в кадре %d", len(hs))
	}
	for _, h := range hs {
		if h["settlement_name"] != s.Name || h["settlement_id"] != eidStr(se.ID) {
			t.Fatalf("у лошади не видно села: %v", h)
		}
		if _, ok := h["realm_name"]; !ok {
			t.Fatal("у лошади нет поля державы")
		}
	}
	if hs[0]["active"] != true || hs[1]["active"] != false {
		t.Fatalf("ведомой должна остаться первая: %v / %v", hs[0]["active"], hs[1]["active"])
	}
	second := parseIDForTest(hs[1]["id"].(string))
	if ok, why := Horse(w, e, "choose", "", 0, "c1", second); !ok {
		t.Fatalf("выбрать вторую: %q", why)
	}
	if HorseView(w, e).(map[string]any)["id"] != hs[1]["id"] {
		t.Fatal("ведомой не стала выбранная лошадь")
	}
	// первая «осталась в другом селе»: переносим её, садимся — берётся та, что здесь
	first := w.Get(parseIDForTest(hs[0]["id"].(string)))
	var other core.ID
	w.Each("settlement", func(x *core.Entity) {
		if other == 0 && x.ID != se.ID {
			other = x.ID
		}
	})
	first.Get("horse").(*HorseC).Settlement = other
	Horse(w, e, "choose", "", 0, "c2", first.ID)
	if ok, why := Horse(w, e, "mount", "", 0, "m1"); !ok || riderOf(e).Horse != second {
		t.Fatalf("в седло должна попасть лошадь, что стоит здесь: %v %q", ok, why)
	}
	for _, h := range HorsesView(w, e) {
		if h["id"] == eidStr(first.ID) && (h["here"] != false || h["settlement_name"] != settleName(w, other)) {
			t.Fatalf("оставленная лошадь должна показывать чужое село: %v", h)
		}
	}
	// чужая лошадь
	stranger := w.Spawn(&HorseC{Owner: 999999, Settlement: se.ID, Stam: 100})
	Horse(w, e, "dismount", "", 0, "d1")
	if ok, _ := Horse(w, e, "choose", "", 0, "c3", stranger.ID); ok {
		t.Fatal("чужую лошадь выбрать нельзя")
	}
	// предел табуна
	for i := 0; i < 10; i++ {
		Horse(w, e, "buy", "", 0, "bb"+string(rune('a'+i)))
	}
	if n := len(HorsesView(w, e)); n != MaxHorses {
		t.Fatalf("табун больше предела: %d", n)
	}
}

func parseIDForTest(s string) core.ID {
	var id core.ID
	for _, c := range s {
		id = id*10 + core.ID(c-'0')
	}
	return id
}

// Предложенные просьбы тоже говорят, где их дают: село и держава.
func TestQuestOffersCarryPlace(t *testing.T) {
	w, e, se := starterPlayer(t)
	v := QuestsView(w, e, 3)
	offers := v["offers"].([]map[string]any)
	if len(offers) == 0 {
		t.Skip("в селе сейчас нет просьб")
	}
	for _, o := range offers {
		if o["settlement_name"] != core.Settle(se).Name || o["id"] == nil || o["text"] == nil {
			t.Fatalf("предложенная просьба без села или без своих полей: %v", o)
		}
		if _, ok := o["realm_name"]; !ok {
			t.Fatal("нет поля державы у предложенной просьбы")
		}
	}
}
