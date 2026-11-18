package main

// Ядро синтетического нагрузочного стенда «Приграничье» (comms 0284).
// Только стандартная библиотека: собственный минимальный WebSocket-клиент
// (рукопожатие + маскированные клиентские кадры + чтение серверных кадров),
// HTTP-опрос публичных /api/* и потокобезопасный сбор метрик.
//
// Важно: это ТОЛЬКО тестовый инструмент. Он не меняет контракт v1 и не вводит
// новых intents — шлёт лишь уже поддерживаемые op:"goal"/op:"move" и читает
// реальные серверные кадры. Пароли и токены в лог не попадают.

import (
	"bufio"
	"context"
	"crypto/rand"
	"crypto/sha1"
	"encoding/base64"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"sort"
	"strings"
	"sync"
	"time"
)

// --- минимальный ws-клиент (stdlib) ---

type wsConn struct {
	raw net.Conn
	br  *bufio.Reader
}

// wsDial выполняет HTTP Upgrade-рукопожатие и возвращает открытое соединение.
// path — уже со схемой query (например "/ws?token=...&fast=4").
func wsDial(httpBase, path string, timeout time.Duration) (*wsConn, error) {
	host := httpBase
	host = strings.TrimPrefix(host, "http://")
	host = strings.TrimPrefix(host, "https://")
	host = strings.TrimSuffix(host, "/")
	d := net.Dialer{Timeout: timeout}
	c, err := d.Dial("tcp", host)
	if err != nil {
		return nil, err
	}
	keyRaw := make([]byte, 16)
	if _, err := rand.Read(keyRaw); err != nil {
		c.Close()
		return nil, err
	}
	key := base64.StdEncoding.EncodeToString(keyRaw)
	req := "GET " + path + " HTTP/1.1\r\nHost: " + host + "\r\n" +
		"Upgrade: websocket\r\nConnection: Upgrade\r\n" +
		"Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n"
	c.SetDeadline(time.Now().Add(timeout))
	if _, err := c.Write([]byte(req)); err != nil {
		c.Close()
		return nil, err
	}
	br := bufio.NewReader(c)
	status, err := br.ReadString('\n')
	if err != nil {
		c.Close()
		return nil, err
	}
	if !strings.Contains(status, "101") {
		c.Close()
		return nil, fmt.Errorf("ожидали 101, получили %q", strings.TrimSpace(status))
	}
	// Сверяем Sec-WebSocket-Accept, чтобы считать handshake настоящим, а не любым 101.
	var accept string
	for {
		line, err := br.ReadString('\n')
		if err != nil {
			c.Close()
			return nil, err
		}
		if line == "\r\n" || line == "\n" {
			break
		}
		if h, v, ok := strings.Cut(line, ":"); ok && strings.EqualFold(strings.TrimSpace(h), "Sec-WebSocket-Accept") {
			accept = strings.TrimSpace(v)
		}
	}
	want := wsAccept(key)
	if accept != "" && accept != want {
		c.Close()
		return nil, fmt.Errorf("неверный Sec-WebSocket-Accept")
	}
	c.SetDeadline(time.Time{})
	return &wsConn{raw: c, br: br}, nil
}

func wsAccept(key string) string {
	h := sha1.New()
	io.WriteString(h, key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")
	return base64.StdEncoding.EncodeToString(h.Sum(nil))
}

// readText читает один текстовый серверный кадр (сервер шлёт немаскированные
// кадры). Пинги/понги/закрытия обрабатываются прозрачно. Возвращает payload.
func (w *wsConn) readText(deadline time.Time) ([]byte, error) {
	for {
		w.raw.SetReadDeadline(deadline)
		var h [2]byte
		if _, err := io.ReadFull(w.br, h[:]); err != nil {
			return nil, err
		}
		opcode := h[0] & 0x0f
		masked := h[1]&0x80 != 0
		n := int(h[1] & 0x7f)
		switch n {
		case 126:
			var ext [2]byte
			if _, err := io.ReadFull(w.br, ext[:]); err != nil {
				return nil, err
			}
			n = int(binary.BigEndian.Uint16(ext[:]))
		case 127:
			var ext [8]byte
			if _, err := io.ReadFull(w.br, ext[:]); err != nil {
				return nil, err
			}
			n = int(binary.BigEndian.Uint64(ext[:]))
		}
		var mask [4]byte
		if masked {
			if _, err := io.ReadFull(w.br, mask[:]); err != nil {
				return nil, err
			}
		}
		payload := make([]byte, n)
		if _, err := io.ReadFull(w.br, payload); err != nil {
			return nil, err
		}
		if masked {
			for i := range payload {
				payload[i] ^= mask[i%4]
			}
		}
		switch opcode {
		case 0x1: // текст
			return payload, nil
		case 0x8: // close
			return nil, io.EOF
		case 0x9: // ping → pong
			_ = w.writeFrame(0xA, payload)
		default:
			// continuation/binary/pong — игнорируем, ждём текст
		}
	}
}

func (w *wsConn) writeFrame(opcode byte, payload []byte) error {
	// Клиент ОБЯЗАН маскировать кадры (RFC 6455).
	var maskKey [4]byte
	if _, err := rand.Read(maskKey[:]); err != nil {
		return err
	}
	var hdr []byte
	n := len(payload)
	switch {
	case n < 126:
		hdr = []byte{0x80 | opcode, 0x80 | byte(n)}
	case n < 1<<16:
		hdr = []byte{0x80 | opcode, 0x80 | 126, byte(n >> 8), byte(n)}
	default:
		hdr = []byte{0x80 | opcode, 0x80 | 127, 0, 0, 0, 0, byte(n >> 24), byte(n >> 16), byte(n >> 8), byte(n)}
	}
	buf := make([]byte, 0, len(hdr)+4+n)
	buf = append(buf, hdr...)
	buf = append(buf, maskKey[:]...)
	for i := 0; i < n; i++ {
		buf = append(buf, payload[i]^maskKey[i%4])
	}
	w.raw.SetWriteDeadline(time.Now().Add(5 * time.Second))
	_, err := w.raw.Write(buf)
	return err
}

func (w *wsConn) writeText(s string) error { return w.writeFrame(0x1, []byte(s)) }

func (w *wsConn) close() error {
	_ = w.writeFrame(0x8, nil)
	return w.raw.Close()
}

// --- метрики ---

type sampleSet struct {
	mu sync.Mutex
	ms []float64 // миллисекунды
}

func (s *sampleSet) add(d time.Duration) {
	s.addRaw(float64(d.Microseconds()) / 1000.0)
}

// addRaw добавляет готовое число (например размер кадра в байтах).
func (s *sampleSet) addRaw(v float64) {
	s.mu.Lock()
	s.ms = append(s.ms, v)
	s.mu.Unlock()
}

// pct возвращает p50/p95/p99 (мс) и count. Безопасен при пустом наборе.
func (s *sampleSet) pct() percentiles {
	s.mu.Lock()
	cp := append([]float64(nil), s.ms...)
	s.mu.Unlock()
	return percentilesOf(cp)
}

type percentiles struct {
	Count int     `json:"count"`
	P50   float64 `json:"p50_ms"`
	P95   float64 `json:"p95_ms"`
	P99   float64 `json:"p99_ms"`
	Max   float64 `json:"max_ms"`
}

func percentilesOf(v []float64) percentiles {
	p := percentiles{Count: len(v)}
	if len(v) == 0 {
		return p
	}
	sort.Float64s(v)
	q := func(f float64) float64 {
		if len(v) == 1 {
			return v[0]
		}
		idx := int(f * float64(len(v)-1))
		if idx < 0 {
			idx = 0
		}
		if idx >= len(v) {
			idx = len(v) - 1
		}
		return v[idx]
	}
	p.P50, p.P95, p.P99, p.Max = q(0.50), q(0.95), q(0.99), v[len(v)-1]
	return p
}

type counter struct {
	mu sync.Mutex
	m  map[string]int64
}

func newCounter() *counter      { return &counter{m: map[string]int64{}} }
func (c *counter) inc(k string) { c.add(k, 1) }
func (c *counter) add(k string, n int64) {
	c.mu.Lock()
	c.m[k] += n
	c.mu.Unlock()
}
func (c *counter) snapshot() map[string]int64 {
	c.mu.Lock()
	defer c.mu.Unlock()
	out := make(map[string]int64, len(c.m))
	for k, v := range c.m {
		out[k] = v
	}
	return out
}

// metrics — единый потокобезопасный сборщик на весь прогон.
type metrics struct {
	registerLat sampleSet
	loginLat    sampleSet
	wsConnLat   sampleSet
	firstFrame  sampleSet
	ackLat      sampleSet
	reconnect   sampleSet

	httpLat map[string]*sampleSet // по endpoint
	httpMu  sync.Mutex

	frameSize sampleSet

	c *counter

	bytesMu    sync.Mutex
	snapBytes  int64
	snapFrames int64

	aoiMu  sync.Mutex
	aoiMax int
	aoiSum int64
	aoiN   int64
}

func newMetrics() *metrics {
	return &metrics{httpLat: map[string]*sampleSet{}, c: newCounter()}
}

func (m *metrics) http(endpoint string, d time.Duration) {
	m.httpMu.Lock()
	s := m.httpLat[endpoint]
	if s == nil {
		s = &sampleSet{}
		m.httpLat[endpoint] = s
	}
	m.httpMu.Unlock()
	s.add(d)
}

func (m *metrics) frame(payload []byte) {
	m.frameSize.addRaw(float64(len(payload))) // размер кадра в байтах
	m.bytesMu.Lock()
	m.snapBytes += int64(len(payload))
	m.snapFrames++
	m.bytesMu.Unlock()
}

func (m *metrics) aoi(n int) {
	m.aoiMu.Lock()
	if n > m.aoiMax {
		m.aoiMax = n
	}
	m.aoiSum += int64(n)
	m.aoiN++
	m.aoiMu.Unlock()
}

// --- HTTP: регистрация/вход/опрос ---

type httpClient struct {
	base string
	cl   *http.Client
	m    *metrics
}

func newHTTPClient(base string, m *metrics) *httpClient {
	return &httpClient{base: base, m: m, cl: &http.Client{Timeout: 15 * time.Second}}
}

func (h *httpClient) postJSON(ctx context.Context, path, body string) (map[string]any, time.Duration, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, h.base+path, strings.NewReader(body))
	if err != nil {
		return nil, 0, err
	}
	req.Header.Set("Content-Type", "application/json")
	t0 := time.Now()
	resp, err := h.cl.Do(req)
	d := time.Since(t0)
	if err != nil {
		return nil, d, err
	}
	defer resp.Body.Close()
	var out map[string]any
	_ = json.NewDecoder(resp.Body).Decode(&out)
	return out, d, nil
}

// get опрашивает публичный GET endpoint, замеряет латентность и статус.
func (h *httpClient) get(ctx context.Context, endpoint, path string) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, h.base+path, nil)
	if err != nil {
		return err
	}
	t0 := time.Now()
	resp, err := h.cl.Do(req)
	if err != nil {
		h.m.c.inc("http." + endpoint + ".err")
		return err
	}
	n, _ := io.Copy(io.Discard, resp.Body)
	resp.Body.Close()
	h.m.http(endpoint, time.Since(t0))
	h.m.c.add("http."+endpoint+".bytes", n)
	if resp.StatusCode == 200 {
		h.m.c.inc("http." + endpoint + ".ok")
	} else {
		h.m.c.inc(fmt.Sprintf("http.%s.status_%d", endpoint, resp.StatusCode))
	}
	return nil
}

// getStats читает /api/stats и возвращает сводку сервера (для отчёта).
func (h *httpClient) getStats(ctx context.Context) map[string]any {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, h.base+"/api/stats", nil)
	if err != nil {
		return nil
	}
	resp, err := h.cl.Do(req)
	if err != nil {
		return nil
	}
	defer resp.Body.Close()
	var out map[string]any
	_ = json.NewDecoder(resp.Body).Decode(&out)
	return out
}

var errNoBody = errors.New("нет свободного тела")
