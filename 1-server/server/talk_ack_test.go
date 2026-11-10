package server

import (
	"encoding/json"
	"fmt"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Узкий тест диспетчера (перенесён из PR #197, автор — ChatGPT): принятый и отклонённый op:talk дают
// коррелируемый me.result. Сквозной сценарий проверяет то же через сеть, но этот тест точнее
// локализует поломку в обработчике намерения.
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
