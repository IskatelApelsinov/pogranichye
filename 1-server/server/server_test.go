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
	af, bf := read(a), read(b)
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
