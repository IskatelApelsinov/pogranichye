package server

import (
	"bufio"
	"crypto/rand"
	"encoding/base64"
	"encoding/binary"
	"encoding/json"
	"io"
	"math"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"strconv"
	"time"

	"pogranichye/core"
	"pogranichye/sim"
)

// Сквозной тест сетевого слоя: регистрация → вход → websocket → кадр состояния → намерение.
// Проверяет, что игрок реально вселяется в тело и видит живой мир (а не «ничего не запускается»).
func TestEndToEnd(t *testing.T) {
	s := New("test-world", 3, "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	// 1) регистрация
	if r := post(t, ts.URL+"/api/register", `{"login":"kate","pass":"secret1"}`); !r["ok"].(bool) {
		t.Fatalf("регистрация не удалась: %v", r)
	}
	// 2) вход → токен
	lr := post(t, ts.URL+"/api/login", `{"login":"kate","pass":"secret1"}`)
	if !lr["ok"].(bool) {
		t.Fatalf("вход не удался: %v", lr)
	}
	token, _ := lr["token"].(string)
	if token == "" {
		t.Fatal("не выдан токен")
	}

	// 3) websocket: вселяемся в тело и получаем кадр состояния
	conn := wsDial(t, ts.URL, "/ws?token="+token)
	defer conn.Close()
	state := readServerFrame(t, conn)
	var st map[string]any
	if err := json.Unmarshal(state, &st); err != nil {
		t.Fatalf("состояние не JSON: %v (%s)", err, state)
	}
	if alive, _ := st["alive"].(bool); !alive {
		t.Fatalf("игрок не вселился в живое тело: %v", st)
	}
	me, ok := st["me"].(map[string]any)
	if !ok || me["name"] == "" {
		t.Fatalf("нет персонажа в состоянии: %v", st)
	}
	t.Logf("вселился в: %v (%v), деревня %v", me["name"], me["role"], st["place"])

	// 4) намерение: выбрать цель — не должно падать
	writeClientFrame(t, conn, `{"op":"goal","goal":"work"}`)
}

// --- helpers: http ---

func post(t *testing.T, url, body string) map[string]any {
	t.Helper()
	resp, err := http.Post(url, "application/json", strings.NewReader(body))
	if err != nil {
		t.Fatalf("POST %s: %v", url, err)
	}
	defer resp.Body.Close()
	var m map[string]any
	json.NewDecoder(resp.Body).Decode(&m)
	return m
}

// --- helpers: минимальный ws-клиент (рукопожатие + кадры) ---

func wsDial(t *testing.T, tsURL, path string) net.Conn {
	t.Helper()
	host := strings.TrimPrefix(tsURL, "http://")
	c, err := net.Dial("tcp", host)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	keyRaw := make([]byte, 16)
	rand.Read(keyRaw)
	key := base64.StdEncoding.EncodeToString(keyRaw)
	req := "GET " + path + " HTTP/1.1\r\nHost: " + host + "\r\n" +
		"Upgrade: websocket\r\nConnection: Upgrade\r\n" +
		"Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n"
	if _, err := c.Write([]byte(req)); err != nil {
		t.Fatalf("handshake write: %v", err)
	}
	br := bufio.NewReader(c)
	status, _ := br.ReadString('\n')
	if !strings.Contains(status, "101") {
		t.Fatalf("ожидали 101, получили: %q", status)
	}
	for { // дочитать заголовки до пустой строки
		line, _ := br.ReadString('\n')
		if line == "\r\n" || line == "" {
			break
		}
	}
	return &bufConn{Conn: c, r: br}
}

type bufConn struct {
	net.Conn
	r *bufio.Reader
}

func (b *bufConn) Read(p []byte) (int, error) { return b.r.Read(p) }

// readServerFrame читает один НЕмаскированный текстовый кадр (как шлёт сервер).
func readServerFrame(t *testing.T, c net.Conn) []byte {
	t.Helper()
	h := make([]byte, 2)
	io.ReadFull(c, h)
	n := int(h[1] & 0x7f)
	switch n {
	case 126:
		ext := make([]byte, 2)
		io.ReadFull(c, ext)
		n = int(binary.BigEndian.Uint16(ext))
	case 127:
		ext := make([]byte, 8)
		io.ReadFull(c, ext)
		n = int(binary.BigEndian.Uint64(ext))
	}
	payload := make([]byte, n)
	io.ReadFull(c, payload)
	return payload
}

// writeClientFrame шлёт маскированный текстовый кадр (клиент ОБЯЗАН маскировать).
func writeClientFrame(t *testing.T, c net.Conn, s string) {
	t.Helper()
	p := []byte(s)
	frame := []byte{0x81, byte(0x80 | len(p))} // FIN+text, mask=1, len<126
	mask := []byte{1, 2, 3, 4}
	frame = append(frame, mask...)
	for i, b := range p {
		frame = append(frame, b^mask[i%4])
	}
	if _, err := c.Write(frame); err != nil {
		t.Fatalf("write frame: %v", err)
	}
}


// TestTwoPlayersShareOneWorld verifies the minimum multiplayer demo contract:
// two independent accounts own different bodies and each receives the other
// player in the same settlement's live neighbor view.
func TestTwoPlayersShareOneWorld(t *testing.T) {
	s := NewStarter("two-player-demo", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	login := func(name string) string {
		t.Helper()
		if r := post(t, ts.URL+"/api/register", `{"login":"`+name+`","pass":"secret1"}`); !r["ok"].(bool) {
			t.Fatalf("register %s failed: %v", name, r)
		}
		r := post(t, ts.URL+"/api/login", `{"login":"`+name+`","pass":"secret1"}`)
		token, _ := r["token"].(string)
		if token == "" { t.Fatalf("login %s returned no token: %v", name, r) }
		return token
	}
	a := wsDial(t, ts.URL, "/ws?token="+login("player-a"))
	defer a.Close()
	_ = readServerFrame(t, a) // A's initial frame is sent before B joins.
	b := wsDial(t, ts.URL, "/ws?token="+login("player-b"))
	defer b.Close()

	read := func(c net.Conn) map[string]any {
		t.Helper()
		var frame map[string]any
		if err := json.Unmarshal(readServerFrame(t, c), &frame); err != nil {
			t.Fatalf("invalid player frame: %v", err)
		}
		return frame
	}
	bf := read(b) // B's first frame is built after A is already present.
	af := read(a) // A's next full frame includes B.
	ame, _ := af["me"].(map[string]any)
	bme, _ := bf["me"].(map[string]any)
	if ame == nil || bme == nil || ame["id"] == bme["id"] {
		t.Fatalf("players must control distinct bodies: A=%v B=%v", ame, bme)
	}
	aid, _ := ame["id"].(string)
	bid, _ := bme["id"].(string)
	containsPlayer := func(frame map[string]any, wanted string) bool {
		place, _ := frame["place"].(map[string]any)
		neighbors, _ := place["neighbors"].([]any)
		for _, item := range neighbors {
			n, _ := item.(map[string]any)
			if n["id"] == wanted && n["player"] == true { return true }
		}
		return false
	}
	if !containsPlayer(af, bid) || !containsPlayer(bf, aid) {
		t.Fatalf("players must see each other as players in shared settlement: A sees B=%v, B sees A=%v", containsPlayer(af, bid), containsPlayer(bf, aid))
	}
}


// TestTwoPlayerCoDrinkWebSocket covers invite -> accept -> retained social_event for both real WS clients.
func TestTwoPlayerCoDrinkWebSocket(t *testing.T) {
	s := NewStarter("two-player-codrink", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	login := func(name string) string {
		t.Helper()
		if r := post(t, ts.URL+"/api/register", "{\"login\":\""+name+"\",\"pass\":\"secret1\"}"); !r["ok"].(bool) {
			t.Fatalf("register %s failed: %v", name, r)
		}
		r := post(t, ts.URL+"/api/login", "{\"login\":\""+name+"\",\"pass\":\"secret1\"}")
		token, _ := r["token"].(string)
		if token == "" { t.Fatalf("login %s returned no token: %v", name, r) }
		return token
	}
	a := wsDial(t, ts.URL, "/ws?token="+login("codrink-a"))
	defer a.Close()
	_ = readServerFrame(t, a)
	b := wsDial(t, ts.URL, "/ws?token="+login("codrink-b"))
	defer b.Close()
	_ = readServerFrame(t, b)

	readState := func(conn net.Conn) map[string]any {
		t.Helper()
		if err := conn.SetReadDeadline(time.Now().Add(6 * time.Second)); err != nil { t.Fatal(err) }
		defer conn.SetReadDeadline(time.Time{})
		var frame map[string]any
		if err := json.Unmarshal(readServerFrame(t, conn), &frame); err != nil {
			t.Fatalf("invalid state frame: %v", err)
		}
		return frame
	}
	readUntil := func(conn net.Conn, accept func(map[string]any) bool, what string) map[string]any {
		t.Helper()
		deadline := time.Now().Add(6 * time.Second)
		for time.Now().Before(deadline) {
			frame := readState(conn)
			if accept(frame) { return frame }
		}
		t.Fatalf("timed out waiting for %s", what)
		return nil
	}
	meID := func(frame map[string]any) core.ID {
		t.Helper()
		me, _ := frame["me"].(map[string]any)
		id, _ := me["id"].(string)
		n, err := strconv.ParseUint(id, 10, 64)
		if err != nil { t.Fatalf("invalid me.id %q: %v", id, err) }
		return core.ID(n)
	}
	firstA := readState(a)
	firstB := readState(b)
	aID, bID := meID(firstA), meID(firstB)
	if aID == bID { t.Fatal("two clients must control different bodies") }

	// Put both independently controlled bodies at the same real tavern POI and fund them.
	tx, ty := sim.StarterLayout().POI("tavern")
	s.mu.Lock()
	aEntity, bEntity := s.world.Get(aID), s.world.Get(bID)
	if aEntity == nil || bEntity == nil { s.mu.Unlock(); t.Fatal("reserved player body missing") }
	settlementID := core.Place(aEntity).Settlement
	core.Place(bEntity).Settlement = settlementID
	for _, e := range []*core.Entity{aEntity, bEntity} {
		pos := sim.EnsureScenePos(s.world, e)
		pos.X, pos.Y = tx, ty
		purse := core.Purse(e)
		if purse == nil { purse = &core.PurseC{Debts: map[string]float64{}}; s.world.Attach(e, purse) }
		purse.Coins = 10
	}
	settlement := core.Settle(s.world.Get(settlementID))
	settlement.Tavern = true
	settlement.Store[sim.BeerGood] = 4
	s.mu.Unlock()

	writeClientFrame(t, a, "{\"op\":\"invite\",\"target\":\""+strconv.FormatUint(uint64(bID), 10)+"\",\"reqid\":\"codrink-invite-1\"}")
	inviteAck := readUntil(a, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); result, _ := me["result"].(map[string]any)
		return result != nil && result["reqid"] == "codrink-invite-1"
	}, "invite ACK")
	inviteResult := inviteAck["me"].(map[string]any)["result"].(map[string]any)
	if inviteResult["ok"] != true { t.Fatalf("invite rejected: %v", inviteResult) }
	inviteFrame := readUntil(b, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); return me != nil && me["invite_from"] != nil
	}, "incoming co-drink invitation")
	if inviteFrame["me"].(map[string]any)["invite_from"] == nil { t.Fatal("invitee did not receive invitation") }

	// Declining clears the invitation for both players without spending beer or coins.
	writeClientFrame(t, b, "{\"op\":\"declinedrink\",\"reqid\":\"codrink-decline-1\"}")
	declineFrame := readUntil(b, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); result, _ := me["result"].(map[string]any)
		return result != nil && result["reqid"] == "codrink-decline-1"
	}, "decline ACK")
	if declineFrame["me"].(map[string]any)["result"].(map[string]any)["ok"] != true {
		t.Fatalf("decline rejected: %v", declineFrame["me"].(map[string]any)["result"])
	}
	s.mu.Lock()
	declinedA, declinedB := sim.CoDrink(s.world.Get(aID)), sim.CoDrink(s.world.Get(bID))
	declineCoinsA, declineCoinsB := core.Purse(s.world.Get(aID)).Coins, core.Purse(s.world.Get(bID)).Coins
	declineBeerLeft := core.Settle(s.world.Get(settlementID)).Store[sim.BeerGood]
	clear := declinedA != nil && declinedB != nil && declinedA.State == "" && declinedB.State == ""
	s.mu.Unlock()
	if !clear || declineCoinsA != 10 || declineCoinsB != 10 || declineBeerLeft != 4 {
		t.Fatalf("decline must clear both invitations without cost: clear=%v coins=(%.2f,%.2f) beer=%.1f", clear, declineCoinsA, declineCoinsB, declineBeerLeft)
	}

	// The same pair can start a fresh invitation after a decline.
	writeClientFrame(t, a, "{\"op\":\"invite\",\"target\":\""+strconv.FormatUint(uint64(bID), 10)+"\",\"reqid\":\"codrink-invite-2\"}")
	inviteAck = readUntil(a, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); result, _ := me["result"].(map[string]any)
		return result != nil && result["reqid"] == "codrink-invite-2"
	}, "second invite ACK")
	if inviteAck["me"].(map[string]any)["result"].(map[string]any)["ok"] != true {
		t.Fatalf("second invite rejected: %v", inviteAck["me"].(map[string]any)["result"])
	}
	_ = readUntil(b, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); return me != nil && me["invite_from"] != nil
	}, "second incoming co-drink invitation")
	writeClientFrame(t, b, "{\"op\":\"accept\",\"reqid\":\"codrink-accept-1\"}")
	acceptFrame := readUntil(b, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); event, _ := me["social_event"].(map[string]any)
		return event != nil && event["type"] == "codrink"
	}, "invitee co-drink social event")
	partnerFrame := readUntil(a, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); event, _ := me["social_event"].(map[string]any)
		return event != nil && event["type"] == "codrink"
	}, "inviter co-drink social event")
	assertEvent := func(frame map[string]any, expectedWith core.ID) {
		t.Helper()
		me := frame["me"].(map[string]any)
		event := me["social_event"].(map[string]any)
		if event["with"] != strconv.FormatUint(uint64(expectedWith), 10) || event["seq"].(float64) < 1 {
			t.Fatalf("wrong co-drink event: got %v, expected partner %d", event, expectedWith)
		}
	}
	assertEvent(acceptFrame, aID)
	assertEvent(partnerFrame, bID)
	if acceptFrame["me"].(map[string]any)["result"].(map[string]any)["reqid"] != "codrink-accept-1" {
		t.Fatalf("accept ACK missing: %v", acceptFrame["me"].(map[string]any)["result"])
	}

	s.mu.Lock()
	coinsA, coinsB := core.Purse(s.world.Get(aID)).Coins, core.Purse(s.world.Get(bID)).Coins
	beerLeft := core.Settle(s.world.Get(settlementID)).Store[sim.BeerGood]
	s.mu.Unlock()
	if coinsA != 10-sim.BeerPrice || coinsB != 10-sim.BeerPrice || beerLeft != 2 {
		t.Fatalf("co-drink must charge both once and consume two beers: coins=(%.2f,%.2f), beer=%.1f", coinsA, coinsB, beerLeft)
	}

	// A second accept without a fresh invite must be rejected and cannot charge either player again.
	writeClientFrame(t, b, "{\"op\":\"accept\",\"reqid\":\"codrink-accept-2\"}")
	rejected := readUntil(b, func(frame map[string]any) bool {
		me, _ := frame["me"].(map[string]any); result, _ := me["result"].(map[string]any)
		return result != nil && result["reqid"] == "codrink-accept-2"
	}, "duplicate accept rejection")
	result := rejected["me"].(map[string]any)["result"].(map[string]any)
	if result["ok"] != false { t.Fatalf("accept without invitation must reject: %v", result) }
	s.mu.Lock()
	coinsA, coinsB = core.Purse(s.world.Get(aID)).Coins, core.Purse(s.world.Get(bID)).Coins
	beerLeft = core.Settle(s.world.Get(settlementID)).Store[sim.BeerGood]
	s.mu.Unlock()
	if coinsA != 10-sim.BeerPrice || coinsB != 10-sim.BeerPrice || beerLeft != 2 {
		t.Fatalf("rejected accept changed economy: coins=(%.2f,%.2f), beer=%.1f", coinsA, coinsB, beerLeft)
	}
}

// TestTakeTradeWebSocket verifies that equipment purchases and market trades are distinct,
// server-authoritative transactions with reqid idempotency over the real WebSocket endpoint.
func TestTakeTradeWebSocket(t *testing.T) {
	s := NewStarter("take-trade-ws", "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()
	if r := post(t, ts.URL+"/api/register", "{\"login\":\"market-player\",\"pass\":\"secret1\"}"); r["ok"] != true {
		t.Fatalf("register failed: %v", r)
	}
	login := post(t, ts.URL+"/api/login", "{\"login\":\"market-player\",\"pass\":\"secret1\"}")
	token, _ := login["token"].(string)
	if token == "" { t.Fatalf("login returned no token: %v", login) }
	conn := wsDial(t, ts.URL, "/ws?token="+token)
	defer conn.Close()
	_ = readServerFrame(t, conn)

	readState := func() map[string]any {
		t.Helper()
		if err := conn.SetReadDeadline(time.Now().Add(6 * time.Second)); err != nil { t.Fatal(err) }
		defer conn.SetReadDeadline(time.Time{})
		var frame map[string]any
		if err := json.Unmarshal(readServerFrame(t, conn), &frame); err != nil { t.Fatalf("invalid state frame: %v", err) }
		return frame
	}
	waitResult := func(reqid string) map[string]any {
		t.Helper()
		deadline := time.Now().Add(6 * time.Second)
		for time.Now().Before(deadline) {
			frame := readState()
			me, _ := frame["me"].(map[string]any)
			result, _ := me["result"].(map[string]any)
			if result != nil && result["reqid"] == reqid { return result }
		}
		t.Fatalf("no me.result for reqid %s", reqid)
		return nil
	}
	first := readState()
	me := first["me"].(map[string]any)
	idNum, err := strconv.ParseUint(me["id"].(string), 10, 64)
	if err != nil { t.Fatalf("invalid player id: %v", err) }
	playerID := core.ID(idNum)
	tx, ty := sim.StarterLayout().POI("market")

	s.mu.Lock()
	player := s.world.Get(playerID)
	if player == nil { s.mu.Unlock(); t.Fatal("reserved player body missing") }
	pos := sim.EnsureScenePos(s.world, player)
	pos.X, pos.Y = tx, ty
	core.Purse(player).Coins = 100
	settlementID := core.Place(player).Settlement
	settlement := core.Settle(s.world.Get(settlementID))
	if settlement.Store == nil { settlement.Store = map[string]float64{} }
	settlement.Store[econ.GoodWood] = 10
	woodPrice := econ.Price(s.world.Get(settlementID), econ.GoodWood)
	if woodPrice <= 0 { s.mu.Unlock(); t.Fatalf("invalid wood market price: %.2f", woodPrice) }
	s.mu.Unlock()

	// Take buys a carried item into me.inv.items; repeating the same reqid is free.
	writeClientFrame(t, conn, "{\"op\":\"take\",\"item\":\"torch\",\"reqid\":\"take-torch-1\"}")
	if result := waitResult("take-torch-1"); result["ok"] != true { t.Fatalf("torch purchase rejected: %v", result) }
	s.mu.Lock()
	coinsAfterTake := core.Purse(s.world.Get(playerID)).Coins
	torchCount := sim.Inv(s.world.Get(playerID)).Items["torch"]
	s.mu.Unlock()
	if coinsAfterTake != 98 || torchCount != 1 { t.Fatalf("take should cost 2 and add one torch: coins=%.2f torch=%d", coinsAfterTake, torchCount) }
	writeClientFrame(t, conn, "{\"op\":\"take\",\"item\":\"torch\",\"reqid\":\"take-torch-1\"}")
	if result := waitResult("take-torch-1"); result["ok"] != true { t.Fatalf("idempotent take retry rejected: %v", result) }
	s.mu.Lock()
	coinsAfterRetry := core.Purse(s.world.Get(playerID)).Coins
	torchAfterRetry := sim.Inv(s.world.Get(playerID)).Items["torch"]
	s.mu.Unlock()
	if coinsAfterRetry != coinsAfterTake || torchAfterRetry != torchCount { t.Fatal("same take reqid charged or granted twice") }

	// A product sourced from the woods cannot be bought at the market.
	writeClientFrame(t, conn, "{\"op\":\"take\",\"item\":\"axe\",\"reqid\":\"take-axe-wrong-place\"}")
	if result := waitResult("take-axe-wrong-place"); result["ok"] != false { t.Fatalf("axe purchase at market should reject: %v", result) }
	s.mu.Lock()
	coinsAfterWrongPlace := core.Purse(s.world.Get(playerID)).Coins
	axeCount := sim.Inv(s.world.Get(playerID)).Items["axe"]
	s.mu.Unlock()
	if coinsAfterWrongPlace != coinsAfterTake || axeCount != 0 { t.Fatal("rejected take changed money or inventory") }

	// Equip consumes no stock: it selects the already purchased item in the hand slot.
	writeClientFrame(t, conn, "{\"op\":\"equip\",\"item\":\"torch\",\"reqid\":\"equip-torch-1\"}")
	if result := waitResult("equip-torch-1"); result["ok"] != true { t.Fatalf("equip rejected: %v", result) }
	s.mu.Lock()
	hand := sim.Inv(s.world.Get(playerID)).Hand
	s.mu.Unlock()
	if hand != "torch" { t.Fatalf("equip should set hand=torch, got %q", hand) }

	// Trade buys bulk goods into me.stash, not into the equipment inventory.
	writeClientFrame(t, conn, "{\"op\":\"trade\",\"good\":\""+econ.GoodWood+"\",\"side\":\"buy\",\"qty\":2,\"reqid\":\"trade-wood-buy-1\"}")
	if result := waitResult("trade-wood-buy-1"); result["ok"] != true { t.Fatalf("market buy rejected: %v", result) }
	s.mu.Lock()
	coinsAfterBuy := core.Purse(s.world.Get(playerID)).Coins
	woodInStash := econ.Stash(s.world.Get(playerID)).Goods[econ.GoodWood]
	woodInMarket := core.Settle(s.world.Get(settlementID)).Store[econ.GoodWood]
	s.mu.Unlock()
	if math.Abs(coinsAfterBuy-(coinsAfterTake-2*woodPrice)) > 1e-9 || woodInStash != 2 || woodInMarket != 8 {
		t.Fatalf("market buy mismatch: coins=%.2f stash=%.1f market=%.1f price=%.2f", coinsAfterBuy, woodInStash, woodInMarket, woodPrice)
	}
	writeClientFrame(t, conn, "{\"op\":\"trade\",\"good\":\""+econ.GoodWood+"\",\"side\":\"buy\",\"qty\":2,\"reqid\":\"trade-wood-buy-1\"}")
	if result := waitResult("trade-wood-buy-1"); result["ok"] != true { t.Fatalf("idempotent market retry rejected: %v", result) }
	s.mu.Lock()
	coinsAfterBuyRetry := core.Purse(s.world.Get(playerID)).Coins
	woodAfterBuyRetry := econ.Stash(s.world.Get(playerID)).Goods[econ.GoodWood]
	marketAfterBuyRetry := core.Settle(s.world.Get(settlementID)).Store[econ.GoodWood]
	s.mu.Unlock()
	if coinsAfterBuyRetry != coinsAfterBuy || woodAfterBuyRetry != woodInStash || marketAfterBuyRetry != woodInMarket {
		t.Fatal("same trade reqid repeated the purchase")
	}

	// Selling returns bulk goods to market stock and pays the player once (10% merchant fee).
	writeClientFrame(t, conn, "{\"op\":\"trade\",\"good\":\""+econ.GoodWood+"\",\"side\":\"sell\",\"qty\":1,\"reqid\":\"trade-wood-sell-1\"}")
	if result := waitResult("trade-wood-sell-1"); result["ok"] != true { t.Fatalf("market sell rejected: %v", result) }
	s.mu.Lock()
	coinsAfterSell := core.Purse(s.world.Get(playerID)).Coins
	woodAfterSell := econ.Stash(s.world.Get(playerID)).Goods[econ.GoodWood]
	marketAfterSell := core.Settle(s.world.Get(settlementID)).Store[econ.GoodWood]
	s.mu.Unlock()
	if math.Abs(coinsAfterSell-(coinsAfterBuy+woodPrice*0.9)) > 1e-9 || woodAfterSell != 1 || marketAfterSell != 9 {
		t.Fatalf("market sell mismatch: coins=%.2f stash=%.1f market=%.1f", coinsAfterSell, woodAfterSell, marketAfterSell)
	}
}

// TestControllerLeaseReturnsSameSoulToAI verifies disconnect releases only the
// controller lease; the entity and its persistent identity/state survive.
func TestControllerLeaseReturnsSameSoulToAI(t *testing.T) {
	s := NewStarter("soul-handoff", "", []byte("secret"), "")
	if _, err := s.acc.Register("soul-owner", "secret1"); err != nil {
		t.Fatalf("register: %v", err)
	}
	token, _, err := s.acc.Login("soul-owner", "secret1")
	if err != nil { t.Fatalf("login: %v", err) }
	uid, err := s.acc.Validate(token)
	if err != nil { t.Fatalf("validate: %v", err) }

	firstID, firstSID, status := s.reserveBody(uid)
	if status != "ok" || firstID == 0 || firstSID == 0 {
		t.Fatalf("first reserve = (%d,%d,%q), want ok", firstID, firstSID, status)
	}
	e := s.world.Get(firstID)
	if e == nil || core.Ident(e) == nil || core.Mind(e) == nil || core.Vitals(e) == nil {
		t.Fatal("reserved soul is missing identity/mind/vitals")
	}
	ident := *core.Ident(e)
	vitals := *core.Vitals(e)
	mind := core.Mind(e)
	mind.Goal, mind.LastAct, mind.GoalUntil = "work", "куёт", 12345

	s.release(firstID, firstSID)
	if s.ctl[firstID] != 0 { t.Fatal("disconnect must release controller lease") }
	if s.world.Get(firstID) != e { t.Fatal("disconnect must not destroy or replace the soul") }
	if mind.Goal != "work" || mind.LastAct != "куёт" || mind.GoalUntil != 0 {
		t.Fatalf("disconnect should preserve thought and allow AI reconsideration: %+v", mind)
	}
	secondID, secondSID, status := s.reserveBody(uid)
	if status != "ok" || secondID != firstID || secondSID == firstSID {
		t.Fatalf("reconnect = (%d,%d,%q), want same body %d and new lease", secondID, secondSID, status, firstID)
	}
	if got := core.Ident(s.world.Get(secondID)); got == nil || *got != ident {
		t.Fatalf("identity changed: got %+v want %+v", got, ident)
	}
	if got := core.Vitals(s.world.Get(secondID)); got == nil || *got != vitals {
		t.Fatalf("vitals changed: got %+v want %+v", got, vitals)
	}
	s.release(secondID, secondSID)
}
