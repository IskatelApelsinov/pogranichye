package server

import (
	"encoding/json"
	"net/http/httptest"
	"testing"
	"time"

	"pogranichye/core"
)

func devSession(t *testing.T, s *Server, base, login string) core.ID {
	t.Helper()
	post(t, base+"/api/register", `{"login":"`+login+`","pass":"secret1"}`)
	lr := post(t, base+"/api/login", `{"login":"`+login+`","pass":"secret1"}`)
	uid, err := s.acc.Validate(lr["token"].(string))
	if err != nil {
		t.Fatal(err)
	}
	cid, _, status := s.reserveBody(uid)
	if status != "ok" {
		t.Fatalf("тело: %s", status)
	}
	s.mu.Lock()
	s.devLocked().uidOf[cid] = uid
	s.mu.Unlock()
	return cid
}

func frameMe(t *testing.T, s *Server, cid core.ID) map[string]any {
	t.Helper()
	var f map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &f); err != nil {
		t.Fatal(err)
	}
	return f["me"].(map[string]any)
}

// Режим разработчика: только аккаунту из списка (по логину — по решению владельца, или по id);
// чужой аккаунт не видит me.dev и получает отказ; время останавливается, ускоряется, крутится вперёд;
// шаг своего персонажа ускоряется в пределах; повтор reqid не крутит мир дважды; reset всё вернёт.
func TestDevModeTimeAndWalkOnlyForAllowlisted(t *testing.T) {
	s := NewStarter("dev-mode", "", []byte("secret"), "")
	s.SetDevAccounts("user@user.user")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()
	admin := devSession(t, s, ts.URL, "User@User.user")
	other := devSession(t, s, ts.URL, "guest1")

	if frameMe(t, s, other)["dev"] != nil {
		t.Fatal("обычный игрок видит me.dev")
	}
	s.applyIntent(other, `{"op":"dev","action":"pause","reqid":"g1"}`)
	if s.dev.paused {
		t.Fatal("обычный игрок остановил время")
	}
	dev, _ := frameMe(t, s, admin)["dev"].(map[string]any)
	if dev == nil || dev["enabled"] != true {
		t.Fatalf("у владельца нет me.dev: %v", frameMe(t, s, admin)["dev"])
	}

	// пауза: часы не идут, сколько бы реального времени ни прошло
	s.applyIntent(admin, `{"op":"dev","action":"pause","reqid":"a1"}`)
	h0 := s.runner.Clock.TotalHours
	s.mu.Lock()
	n := s.hoursDue(100 * s.tickEach)
	s.mu.Unlock()
	if n != 0 || s.runner.Clock.TotalHours != h0 {
		t.Fatal("на паузе время идёт")
	}
	// ускорение ×10: за десятую часть часа реального времени — один игровой час
	s.applyIntent(admin, `{"op":"dev","action":"resume","reqid":"a2"}`)
	s.applyIntent(admin, `{"op":"dev","action":"time_scale","value":10,"reqid":"a3"}`)
	s.mu.Lock()
	n = s.hoursDue(s.tickEach / 10)
	s.mu.Unlock()
	if n != 1 {
		t.Fatalf("при ×10 за 1/10 часа должен пройти 1 час мира, прошло %d", n)
	}
	s.applyIntent(admin, `{"op":"dev","action":"time_scale","value":500,"reqid":"a4"}`)
	if s.dev.scale != devScaleMax {
		t.Fatalf("множитель времени не ограничен: %v", s.dev.scale)
	}
	// шаг вперёд и повтор того же reqid
	h0 = s.runner.Clock.TotalHours
	s.applyIntent(admin, `{"op":"dev","action":"step_hours","value":5,"reqid":"a5"}`)
	s.applyIntent(admin, `{"op":"dev","action":"step_hours","value":5,"reqid":"a5"}`)
	if got := s.runner.Clock.TotalHours - h0; got != 5 {
		t.Fatalf("шаг на 5 ч дал %d ч", got)
	}
	s.applyIntent(admin, `{"op":"dev","action":"set_hour","value":7,"reqid":"a6"}`)
	if s.runner.Clock.Hour() != 7 {
		t.Fatalf("set_hour: %d", s.runner.Clock.Hour())
	}
	// шаг персонажа
	s.applyIntent(admin, `{"op":"dev","action":"walk_speed","value":3,"reqid":"a7"}`)
	s.applyIntent(admin, `{"op":"move","mx":1,"my":0,"dt":0.1,"seq":1}`)
	mv := frameMe(t, s, admin)["movement"].(map[string]any)
	if mv["dev_mult"].(float64) != 3 || mv["walk"].(float64) < 4.1 {
		t.Fatalf("шаг разработчика: %v", mv)
	}
	s.applyIntent(other, `{"op":"move","mx":1,"my":0,"dt":0.1,"seq":1}`)
	if frameMe(t, s, other)["movement"].(map[string]any)["dev_mult"].(float64) != 1 {
		t.Fatal("прибавка шага досталась чужому персонажу")
	}
	s.applyIntent(admin, `{"op":"dev","action":"reset","reqid":"a8"}`)
	if s.dev.scale != 1 || s.dev.paused || s.walkMultLocked(admin) != 1 {
		t.Fatal("reset не вернул обычный ход")
	}
	res := frameMe(t, s, admin)["result"]
	if res == nil {
		t.Fatal("нет исхода команды в me.result")
	}

	// по числовому id тоже работает, а пустой список — режима нет ни у кого
	uid := s.dev.uidOf[other]
	s.SetDevAccounts(itoaU(uid))
	if frameMe(t, s, other)["dev"] == nil || frameMe(t, s, admin)["dev"] != nil {
		t.Fatal("список по id работает неверно")
	}
	s.SetDevAccounts("")
	if frameMe(t, s, other)["dev"] != nil {
		t.Fatal("пустой список должен выключать режим")
	}
	_ = time.Second
}

func itoaU(v uint64) string {
	b, _ := json.Marshal(v)
	return string(b)
}
