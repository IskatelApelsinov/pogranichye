package server

import (
	"net/http/httptest"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Владелец (2026-11-13): «я вышел с сервера, мой перс никуда не передвигается... после того, как
// он становился NPC, вообще ничего не изменялось — ни деньги, ни предметы, ни занятия». Проверяем
// ровно это на настоящей стартовой деревне: игрок заходит, его тело получает реальную сессию, затем
// сессия ЗАКРЫВАЕТСЯ (release — ровно то, что делает disconnect/logout), и дальше идёт мир РЕАЛЬНЫМИ
// часовыми тиками (`s.Step`, то же, что `tickLoop` зовёт каждый `defaultWorldTick`), БЕЗ какого-либо
// подключённого игрока. Если через много часов навык/деньги/занятие тела не меняются ни разу — баг
// подтверждён; если меняются — симуляция идёт, и дело в другом (например, defaultWorldTick ~43
// реальных минуты на игровой час — слишком долго для короткой ручной проверки).
func TestFormerPlayerBodyKeepsSimulatingAfterLogout(t *testing.T) {
	s := NewStarter("offline-sim-demo-20261113", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	if r := post(t, ts.URL+"/api/register", `{"login":"kate","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация: %v", r)
	}
	lr := post(t, ts.URL+"/api/login", `{"login":"kate","pass":"secret1"}`)
	token, _ := lr["token"].(string)
	if token == "" {
		t.Fatalf("вход: %v", lr)
	}
	uid, err := s.acc.Validate(token)
	if err != nil {
		t.Fatalf("валидация токена: %v", err)
	}
	cid, sid, status := s.reserveBody(uid)
	if status != "ok" {
		t.Fatalf("не удалось занять тело: %s", status)
	}

	var role string
	var startCoins, startSkill float64
	s.mu.Lock()
	e := s.world.Get(cid)
	if e == nil {
		s.mu.Unlock()
		t.Fatal("тело не найдено после входа")
	}
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	sim.RollGift(s.world, e, s.runner.Hub.Stream("gift", eid(e.ID)))
	if pl := core.Place(e); pl != nil {
		sim.LearnVisited(s.world, e, pl.Settlement)
	}
	if id := core.Ident(e); id != nil {
		role = id.Role
	}
	if p := core.Purse(e); p != nil {
		startCoins = p.Coins
	}
	if sk := core.Skills(e); sk != nil && role != "" {
		startSkill = sk.Level(role)
	}
	s.mu.Unlock()
	t.Logf("тело: role=%q, старт coins=%.2f skill(%s)=%.2f", role, startCoins, role, startSkill)

	// Logout: ровно то, что handleWS делает в defer при закрытии соединения.
	s.release(cid, sid)

	type sample struct {
		coins, skill float64
		actCode      string
	}
	seenActCodes := map[string]bool{}
	var last sample
	// 30 РЕАЛЬНЫХ игровых часов тиков мира (s.Step = то же, что вызывает tickLoop на каждый
	// defaultWorldTick), без единого подключённого игрока в мире.
	for h := 0; h < 30; h++ {
		s.Step()
		s.mu.Lock()
		e := s.world.Get(cid)
		if e == nil {
			s.mu.Unlock()
			t.Fatalf("тело исчезло из мира после logout на часу %d", h)
		}
		m := core.Mind(e)
		if m != nil {
			seenActCodes[m.ActCode] = true
			last.actCode = m.ActCode
		}
		if p := core.Purse(e); p != nil {
			last.coins = p.Coins
		}
		if sk := core.Skills(e); sk != nil && role != "" {
			last.skill = sk.Level(role)
		}
		s.mu.Unlock()
	}

	t.Logf("после 30ч без игрока: coins=%.2f skill(%s)=%.2f act_codes=%v", last.coins, role, last.skill, seenActCodes)

	if len(seenActCodes) <= 1 {
		t.Fatalf("ВОСПРОИЗВЕДЕНА жалоба владельца: за 30 игровых часов без подключённого игрока act_code тела не менялся (видели только %v) — AI не управляет телом после logout", seenActCodes)
	}
	if last.coins == startCoins && last.skill == startSkill {
		t.Fatalf("ВОСПРОИЗВЕДЕНА жалоба владельца: за 30 игровых часов без игрока ни деньги (%.2f→%.2f), ни навык %s (%.2f→%.2f) не изменились — экономика/навыки не симулируются для бывшего игрока",
			startCoins, last.coins, role, startSkill, last.skill)
	}
}
