package server

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Ядро v1.10 через сетевой слой: намерение плетения даёт коррелируемый результат, шаг реального
// времени греет поселение игрока, быстрый кадр — лёгкий срез без полного снимка.
func TestCoreIntentsFastFrameAndHeat(t *testing.T) {
	s := NewStarter("core-net", "", []byte("x"), "")
	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid != 0 {
			return
		}
		if v := core.Vitals(e); v != nil && v.Age >= 18 && core.Mind(e) != nil && core.Home(e) != nil {
			if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
				cid = e.ID
			}
		}
	})
	if cid == 0 {
		t.Fatal("нет тела для игрока")
	}
	e := s.world.Get(cid)
	s.ctl[cid] = 1
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)

	s.fastStep(0.1)
	if s.hot != 1 || s.cold == 0 {
		t.Fatalf("поселение игрока должно быть горячим, дальние — холодными: hot=%d cold=%d", s.hot, s.cold)
	}

	s.applyIntent(cid, `{"v":1,"op":"weave","nodes":["knot","seal"],"reqid":"w-1"}`)
	var full map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &full); err != nil {
		t.Fatal(err)
	}
	me := full["me"].(map[string]any)
	res, _ := me["result"].(map[string]any)
	if res == nil || res["reqid"] != "w-1" {
		t.Fatalf("плетение должно дать результат по reqid: %v", me["result"])
	}
	if feed, _ := me["feed"].([]any); len(feed) == 0 {
		t.Fatal("плетение должно оставить событие в ленте")
	}

	s.applyIntent(cid, `{"v":1,"op":"stance","stance":"defensive","reqid":"s-1"}`)
	var fast map[string]any
	if err := json.Unmarshal([]byte(s.fastJSON(cid)), &fast); err != nil {
		t.Fatal(err)
	}
	if fast["fast"] != true {
		t.Fatal("быстрый кадр должен быть помечен fast:true")
	}
	fme := fast["me"].(map[string]any)
	if _, has := fme["id"]; has {
		t.Fatal("быстрый кадр — не полный снимок: me.id в нём быть не должно")
	}
	if fme["combat"].(map[string]any)["stance"] != "defensive" {
		t.Fatal("стойка должна прийти в быстром кадре")
	}

	// игрок ушёл — всё остывает
	delete(s.ctl, cid)
	s.fastStep(0.1)
	if s.hot != 0 {
		t.Fatal("без игроков горячих поселений быть не должно")
	}
}
