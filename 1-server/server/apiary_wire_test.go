package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
	"pogranichye/sim"
)

// v1.17 по проводу: act:"apiary" и op:"unwear" отвечают коррелированным me.result — и отказом
// с причиной, и успехом; мёд при успехе реально переезжает из улья на склад села.
func TestApiaryAndUnwearOverWire(t *testing.T) {
	s := NewStarter("apiary-wire", "", []byte("x"), "")
	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if v := core.Vitals(e); cid == 0 && v != nil && v.Age >= 18 && core.Mind(e) != nil && core.Home(e) != nil {
			if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
				cid = e.ID
			}
		}
	})
	if cid == 0 {
		t.Fatal("нет тела для игрока")
	}
	e := s.world.Get(cid)
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	core.Vitals(e).Energy = 100
	seID := core.Place(e).Settlement

	result := func(intent, reqid string) (bool, string) {
		t.Helper()
		s.applyIntent(cid, intent)
		var f map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &f); err != nil {
			t.Fatal(err)
		}
		r, _ := f["me"].(map[string]any)["result"].(map[string]any)
		if r == nil || r["reqid"] != reqid {
			t.Fatalf("нет me.result для %s: %v", reqid, r)
		}
		ok, _ := r["ok"].(bool)
		why, _ := r["reason"].(string)
		return ok, why
	}

	if ok, why := result(`{"v":1,"op":"unwear","item":"cloak","reqid":"u1"}`, "u1"); ok || why == "" {
		t.Fatalf("снять ненадетое: ожидался отказ с причиной, ok=%t why=%q", ok, why)
	}

	var hive *econ.ResourceNodeC
	s.world.Each("resnode", func(ne *core.Entity) {
		if n := econ.Node(ne); n != nil && n.Node == econ.NodeApiary && n.Settlement == seID {
			hive = n
		}
	})
	if hive == nil {
		t.Skip("в селе игрока нет пасеки")
	}
	hive.Stock = 6
	// где бы ни стоял житель — у корчмы точно не пасека
	sim.GoTo(s.world, e, "tavern")
	if ok, why := result(`{"v":1,"op":"act","action":"apiary","reqid":"a1"}`, "a1"); ok || why == "" {
		t.Fatalf("вдали от пасеки: ожидался отказ с причиной, ok=%t why=%q", ok, why)
	}
	if s.runner.Clock.Season() == core.Winter {
		return // зимой успех невозможен по правилам; отказ уже проверен
	}
	sim.GoTo(s.world, e, "garden")
	store := core.Settle(s.world.Get(seID)).Store
	before := store[econ.GoodHoney]
	if ok, why := result(`{"v":1,"op":"act","action":"apiary","reqid":"a2"}`, "a2"); !ok {
		t.Fatalf("у пасеки с мёдом в сотах должно получиться: %s", why)
	}
	if hive.Stock >= 6 || core.Settle(s.world.Get(seID)).Store[econ.GoodHoney] <= before {
		t.Fatal("мёд должен переехать из улья на склад села")
	}
}
