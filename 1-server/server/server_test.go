package server

import (
	"bufio"
	"crypto/rand"
	"encoding/base64"
	"encoding/binary"
	"encoding/json"
	"io"
	"net"
	"net/http"
	"net/http/httptest"

	"pogranichye/core"
	"strings"
	"testing"
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


// TestControllerLeaseReturnsSameSoulToAIMakesDisconnectAControlChange verifies that
// disconnect releases only the controller lease: the body and its persistent
// identity/state remain in the world and can be reclaimed by the same account.
func TestControllerLeaseReturnsSameSoulToAI(t *testing.T) {
	s := NewStarter("soul-handoff", "", []byte("secret"), "")
	if _, err := s.acc.Register("soul-owner", "secret1"); err != nil {
		t.Fatalf("register: %v", err)
	}
	token, _, err := s.acc.Login("soul-owner", "secret1")
	if err != nil {
		t.Fatalf("login: %v", err)
	}
	uid, err := s.acc.Validate(token)
	if err != nil {
		t.Fatalf("validate: %v", err)
	}

	firstID, firstSID, status := s.reserveBody(uid)
	if status != "ok" || firstID == 0 || firstSID == 0 {
		t.Fatalf("first reserve = (%d,%d,%q), want ok", firstID, firstSID, status)
	}
	e := s.world.Get(firstID)
	if e == nil || core.Ident(e) == nil || core.Mind(e) == nil || core.Vitals(e) == nil {
		t.Fatal("reserved soul is missing its ordinary identity/mind/body components")
	}
	ident := *core.Ident(e)
	vitals := *core.Vitals(e)
	mind := core.Mind(e)
	mind.Goal = "work"
	mind.LastAct = "куёт"
	mind.GoalUntil = 12345

	s.release(firstID, firstSID)
	if s.ctl[firstID] != 0 {
		t.Fatal("disconnect must release controller lease")
	}
	if s.world.Get(firstID) != e {
		t.Fatal("disconnect must not replace or destroy the soul entity")
	}
	if mind.Goal != "work" || mind.LastAct != "куёт" || mind.GoalUntil != 0 {
		t.Fatalf("disconnect must preserve identity of thought but let AI reconsider: %+v", mind)
	}

	secondID, secondSID, status := s.reserveBody(uid)
	if status != "ok" || secondID != firstID || secondSID == firstSID {
		t.Fatalf("reconnect reserve = (%d,%d,%q), want same body %d and new lease", secondID, secondSID, status, firstID)
	}
	if got := core.Ident(s.world.Get(secondID)); got == nil || *got != ident {
		t.Fatalf("identity changed across controller handoff: got %+v want %+v", got, ident)
	}
	if got := core.Vitals(s.world.Get(secondID)); got == nil || *got != vitals {
		t.Fatalf("vitals changed across controller handoff: got %+v want %+v", got, vitals)
	}
	s.release(secondID, secondSID)
}
