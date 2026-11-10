package server

import (
	"encoding/json"
	"fmt"
	"math"
	"net"
	"net/http/httptest"
	"path/filepath"
	"testing"
	"time"

	"pogranichye/core"
	"pogranichye/econ"
	"pogranichye/sim"
)

// Воспроизводимый сквозной сценарий вертикального среза (comms 0059, часть D) через НАСТОЯЩИЕ
// HTTP + WebSocket: вход → адресный разговор → отказ → работа → пиво (с повтором платного reqid) →
// выпить → летопись/задача → выход и очистка сессии. Фиксированное семя, временный файл аккаунтов.
// Каждый шаг проверяется дважды: по ответу сервера клиенту (me.result в кадре) и по фактическому
// состоянию мира.

type sliceClient struct {
	t    *testing.T
	s    *Server
	conn net.Conn
	cid  core.ID
	n    int
}

func (c *sliceClient) send(format string, a ...any) {
	writeClientFrame(c.t, c.conn, fmt.Sprintf(format, a...))
}

// frame читает следующий кадр (полный или быстрый).
func (c *sliceClient) frame() map[string]any {
	c.t.Helper()
	_ = c.conn.SetReadDeadline(time.Now().Add(5 * time.Second))
	var f map[string]any
	if err := json.Unmarshal(readServerFrame(c.t, c.conn), &f); err != nil {
		c.t.Fatalf("кадр не JSON: %v", err)
	}
	return f
}

// full ждёт следующий ПОЛНЫЙ кадр.
func (c *sliceClient) full() map[string]any {
	c.t.Helper()
	for i := 0; i < 80; i++ {
		if f := c.frame(); f["fast"] != true {
			return f
		}
	}
	c.t.Fatal("полный кадр не пришёл")
	return nil
}

// result шлёт намерение с новым reqid и ждёт me.result ИМЕННО с этим reqid (полный или быстрый кадр).
func (c *sliceClient) result(intent string) (bool, string, string) {
	c.t.Helper()
	c.n++
	reqid := fmt.Sprintf("e2e-%d", c.n)
	return c.resultWith(intent, reqid)
}

func (c *sliceClient) resultWith(intent, reqid string) (bool, string, string) {
	c.t.Helper()
	c.send(`{"v":1,%s,"reqid":"%s"}`, intent, reqid)
	for i := 0; i < 120; i++ {
		me, _ := c.frame()["me"].(map[string]any)
		if r, _ := me["result"].(map[string]any); r != nil && r["reqid"] == reqid {
			ok, _ := r["ok"].(bool)
			reason, _ := r["reason"].(string)
			return ok, reason, reqid
		}
	}
	c.t.Fatalf("нет me.result для %s (%s)", reqid, intent)
	return false, "", reqid
}

// world выполняет проверку фактического состояния мира под замком сервера.
func (c *sliceClient) world(fn func(e *core.Entity)) {
	c.s.mu.Lock()
	defer c.s.mu.Unlock()
	fn(c.s.world.Get(c.cid))
}

func TestVerticalSliceTalkWorkBeer(t *testing.T) {
	s := NewStarter("slice-e2e-seed-1", filepath.Join(t.TempDir(), "accounts.json"), []byte("slice-secret"), "")
	// реальный шаг мира и реального времени, как на живом сервере — но управляемый: по выходу из теста
	// петля останавливается и мы ДОЖИДАЕМСЯ завершения её горутины (никаких вечных тикеров после теста)
	loopDone := make(chan struct{})
	go func() { s.tickLoop(); close(loopDone) }()
	ts := httptest.NewServer(s.Handler())
	defer func() {
		ts.Close()
		s.Close()
		select {
		case <-loopDone:
		case <-time.After(3 * time.Second):
			t.Error("тик-петля не остановилась после Close")
		}
	}()

	if r := post(t, ts.URL+"/api/register", `{"login":"slice","pass":"secret1"}`); r["ok"] != true {
		t.Fatalf("регистрация: %v", r)
	}
	lr := post(t, ts.URL+"/api/login", `{"login":"slice","pass":"secret1"}`)
	token, _ := lr["token"].(string)
	if token == "" {
		t.Fatalf("вход: %v", lr)
	}
	conn := wsDial(t, ts.URL, "/ws?token="+token+"&fast=15")
	c := &sliceClient{t: t, s: s, conn: conn}
	first := c.full()
	me := first["me"].(map[string]any)
	c.cid = parseID(me["id"].(string))
	if c.cid == 0 || first["alive"] != true {
		t.Fatalf("не вселился: %v", first["alive"])
	}
	// стартовые условия сценария: силы и монеты на кружку (это подготовка стенда, не часть проверяемого цикла)
	c.world(func(e *core.Entity) {
		core.Vitals(e).Energy = 100
		if p := core.Purse(e); p.Coins < 5 {
			p.Coins = 5
		}
	})

	// --- 1. адресный разговор с конкретным жителем ---
	// отказ: несуществующая цель
	if ok, reason, _ := c.result(`"op":"talk","target":"99999999"`); ok || reason == "" {
		t.Fatalf("разговор с несуществующей целью должен быть отклонён с причиной: ok=%v %q", ok, reason)
	}
	var npc string
search:
	for _, loc := range []string{"well", "tavern", "market", "hearth", "smithy", "granary"} {
		c.send(`{"v":1,"op":"goto","location":"%s"}`, loc)
		for try := 0; try < 3 && npc == ""; try++ {
			f := c.full()
			pos := f["me"].(map[string]any)["pos"].(map[string]any)
			place, _ := f["place"].(map[string]any)
			for _, raw := range place["neighbors"].([]any) {
				nb := raw.(map[string]any)
				np := nb["pos"].(map[string]any)
				if nb["player"] == true || nb["known"] == true {
					continue
				}
				if math.Hypot(np["x"].(float64)-pos["x"].(float64), np["y"].(float64)-pos["y"].(float64)) <= 4 {
					if ok, _, _ := c.result(fmt.Sprintf(`"op":"talk","target":"%s"`, nb["id"])); ok {
						npc = nb["id"].(string)
						break search
					}
				}
			}
		}
	}
	if npc == "" {
		t.Fatal("не нашлось жителя рядом ни в одной локации")
	}
	if ok, reason, _ := c.result(`"op":"say","line":"greet"`); !ok {
		t.Fatalf("приветствие отклонено: %s", reason)
	}
	c.world(func(e *core.Entity) {
		if !sim.Knows(e, parseID(npc)) {
			t.Fatal("после приветствия житель должен стать знакомым (сервер)")
		}
	})
	c.send(`{"v":1,"op":"endtalk"}`)

	// --- 2. отказ: работа вне места ---
	c.send(`{"v":1,"op":"goto","location":"well"}`)
	c.full()
	var wood0 float64
	c.world(func(e *core.Entity) { wood0 = stashOf(e, econ.GoodWood) })
	if ok, reason, _ := c.result(`"op":"act","action":"chop"`); ok || reason == "" {
		t.Fatalf("рубка у колодца должна быть отклонена с причиной: ok=%v %q", ok, reason)
	}
	c.world(func(e *core.Entity) {
		if stashOf(e, econ.GoodWood) != wood0 {
			t.Fatal("отклонённая работа не должна давать дров")
		}
	})

	// --- 3. работа у нужного места с подтверждением ---
	c.send(`{"v":1,"op":"goto","location":"woods"}`)
	c.full()
	var skill0 float64
	c.world(func(e *core.Entity) { skill0 = core.Skills(e).Level("woodcutter") })
	ok, reason, chopReq := c.result(`"op":"act","action":"chop"`)
	if !ok {
		t.Fatalf("рубка в лесу отклонена: %s", reason)
	}
	var wood1 float64
	c.world(func(e *core.Entity) {
		wood1 = stashOf(e, econ.GoodWood)
		if wood1 <= wood0 || core.Skills(e).Level("woodcutter") <= skill0 {
			t.Fatalf("работа должна дать дрова и навык: дрова %.1f→%.1f", wood0, wood1)
		}
	})
	// повтор того же reqid — второй раз дров не даёт
	if ok, _, _ := c.resultWith(`"op":"act","action":"chop"`, chopReq); !ok {
		t.Fatal("идемпотентный повтор должен отвечать успехом")
	}
	c.world(func(e *core.Entity) {
		if stashOf(e, econ.GoodWood) != wood1 {
			t.Fatal("повтор reqid не должен рубить второй раз")
		}
	})

	// --- 4. корчма: купить пиво, повторить платный запрос, выпить ---
	// отказ: купить вне корчмы
	if ok, _, _ := c.result(`"op":"buy","item":"beer"`); ok {
		t.Fatal("пиво в лесу купить нельзя")
	}
	c.send(`{"v":1,"op":"goto","location":"tavern"}`)
	c.full()
	var coins0 float64
	c.world(func(e *core.Entity) { coins0 = core.Purse(e).Coins })
	ok, reason, buyReq := c.result(`"op":"buy","item":"beer"`)
	if !ok {
		t.Fatalf("покупка пива отклонена: %s", reason)
	}
	var coins1 float64
	c.world(func(e *core.Entity) {
		coins1 = core.Purse(e).Coins
		if math.Abs(coins0-coins1-sim.BeerPrice) > 1e-9 {
			t.Fatalf("за пиво должно списаться %.2f: было %.2f, стало %.2f", sim.BeerPrice, coins0, coins1)
		}
	})
	if ok, _, _ := c.resultWith(`"op":"buy","item":"beer"`, buyReq); !ok {
		t.Fatal("повтор платного запроса с тем же reqid должен отвечать успехом")
	}
	c.world(func(e *core.Entity) {
		if core.Purse(e).Coins != coins1 {
			t.Fatalf("повтор платного reqid списал деньги второй раз: %.2f → %.2f", coins1, core.Purse(e).Coins)
		}
	})
	if ok, reason, _ := c.result(`"op":"consume","item":"beer"`); !ok {
		t.Fatalf("выпить не удалось: %s", reason)
	}
	// пустую кружку второй раз не выпить
	if ok, _, _ := c.result(`"op":"consume","item":"beer"`); ok {
		t.Fatal("без кружки пить нечего")
	}

	// --- 5. подтверждённые факты в полном кадре: задача, летопись ---
	f := c.full()
	me = f["me"].(map[string]any)
	task := me["task"].(map[string]any)
	if task["met"] != true || task["worked"] != true || task["drank"] != true || task["done"] != true {
		t.Fatalf("задача должна быть закрыта подтверждёнными фактами: %v", task)
	}
	if me["mug"] != false {
		t.Fatal("кружка должна быть пуста")
	}
	journal, _ := me["journal"].([]any)
	seen := map[string]int{}
	var lastSeq float64
	for _, raw := range journal {
		j := raw.(map[string]any)
		seen[j["kind"].(string)]++
		if j["seq"].(float64) <= lastSeq {
			t.Fatal("seq летописи должен строго расти")
		}
		lastSeq = j["seq"].(float64)
	}
	for _, kind := range []string{"met", "worked", "drank"} {
		if seen[kind] != 1 {
			t.Fatalf("веха %q должна быть в летописи ровно один раз: %v", kind, seen)
		}
	}

	// --- 6. выход: сессия снята, тело свободно ---
	conn.Close()
	deadline := time.Now().Add(3 * time.Second)
	for {
		s.mu.Lock()
		held := s.ctl[c.cid]
		s.mu.Unlock()
		if held == 0 {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("после закрытия соединения тело осталось под управлением сессии")
		}
		time.Sleep(20 * time.Millisecond)
	}
}

func stashOf(e *core.Entity, good string) float64 {
	if st := econ.Stash(e); st != nil {
		return st.Goods[good]
	}
	return 0
}
