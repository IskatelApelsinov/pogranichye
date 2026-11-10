package server

import (
	"fmt"
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


// #40: the WebSocket intent dispatcher must expose both accepted and rejected talk
// requests through the existing correlated me.result, without adding wire fields.
func TestTalkIntentCorrelatesResult(t *testing.T) {
	s := NewStarter("talk-ack", "", []byte("x"), "")
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
	settlement := core.Place(e).Settlement
	playerPos := sim.EnsureScenePos(s.world, e)

	var npc *core.Entity
	s.world.Each("place", func(candidate *core.Entity) {
		if npc != nil || candidate.ID == cid {
			return
		}
		pl := core.Place(candidate)
		v := core.Vitals(candidate)
		if pl == nil || pl.Settlement != settlement || core.Mind(candidate) == nil || v == nil || v.Age < 18 {
			return
		}
		np := sim.EnsureScenePos(s.world, candidate)
		np.X, np.Y = playerPos.X+1, playerPos.Y
		npc = candidate
	})
	if npc == nil {
		t.Fatal("нет взрослого жителя в поселении игрока")
	}

	s.applyIntent(cid, fmt.Sprintf(`{"v":1,"op":"talk","target":"%d","reqid":"talk-accepted"}`, npc.ID))
	var accepted map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &accepted); err != nil {
		t.Fatal(err)
	}
	me := accepted["me"].(map[string]any)
	result := me["result"].(map[string]any)
	if result["reqid"] != "talk-accepted" || result["ok"] != true {
		t.Fatalf("успешный talk должен вернуть коррелируемый ack: %#v", result)
	}

	// A second request to an out-of-range target must overwrite the previous ack
	// with a correlated rejection and a useful reason.
	sim.EndTalk(e)
	npcPos := sim.EnsureScenePos(s.world, npc)
	npcPos.X = playerPos.X + 60
	s.applyIntent(cid, fmt.Sprintf(`{"v":1,"op":"talk","target":"%d","reqid":"talk-rejected"}`, npc.ID))
	var rejected map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &rejected); err != nil {
		t.Fatal(err)
	}
	me = rejected["me"].(map[string]any)
	result = me["result"].(map[string]any)
	if result["reqid"] != "talk-rejected" || result["ok"] != false || result["reason"] == "" {
		t.Fatalf("отклонённый talk должен вернуть reqid, ok=false и причину: %#v", result)
	}
}
