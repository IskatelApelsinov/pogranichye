// Пакет wsrv — крошечный WebSocket-сервер на стандартной библиотеке (RFC 6455), без внешних
// зависимостей (их тут и не скачать, да и проект держим dependency-light). Хватает того, что
// нужно живому миру: рукопожатие, текстовые кадры (JSON намерений и состояния), ping/pong,
// закрытие. Фрагментацию и бинарные кадры не поддерживаем — браузер шлёт мелкий JSON одним кадром.
package wsrv

import (
	"bufio"
	"crypto/sha1"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"io"
	"net/http"
	"strings"
	"sync"
)

const magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" // из спецификации WebSocket

// Conn — поднятое соединение поверх перехваченного TCP.
type Conn struct {
	rw *bufio.ReadWriter
	c  io.Closer
	writeMu sync.Mutex // ping/pong и основной цикл могут отправлять одновременно
}

// Upgrade выполняет рукопожатие и возвращает соединение. После успеха обычный http-ответ
// по этому запросу уже не писать — мы забрали сокет.
func Upgrade(w http.ResponseWriter, r *http.Request) (*Conn, error) {
	if !strings.EqualFold(r.Header.Get("Upgrade"), "websocket") {
		return nil, errors.New("не websocket-запрос")
	}
	key := r.Header.Get("Sec-WebSocket-Key")
	if key == "" {
		return nil, errors.New("нет Sec-WebSocket-Key")
	}
	hj, ok := w.(http.Hijacker)
	if !ok {
		return nil, errors.New("сервер не умеет hijack")
	}
	conn, buf, err := hj.Hijack()
	if err != nil {
		return nil, err
	}
	sum := sha1.Sum([]byte(key + magic))
	accept := base64.StdEncoding.EncodeToString(sum[:])
	resp := "HTTP/1.1 101 Switching Protocols\r\n" +
		"Upgrade: websocket\r\n" +
		"Connection: Upgrade\r\n" +
		"Sec-WebSocket-Accept: " + accept + "\r\n\r\n"
	if _, err := buf.WriteString(resp); err != nil {
		conn.Close()
		return nil, err
	}
	if err := buf.Flush(); err != nil {
		conn.Close()
		return nil, err
	}
	return &Conn{rw: buf, c: conn}, nil
}

// ReadText читает один текстовый кадр (строку). Ping отражает pong'ом сам и читает дальше;
// на close возвращает io.EOF.
func (c *Conn) ReadText() (string, error) {
	for {
		op, payload, err := c.readFrame()
		if err != nil {
			return "", err
		}
		switch op {
		case 0x1: // текст
			return string(payload), nil
		case 0x8: // close
			return "", io.EOF
		case 0x9: // ping → pong
			_ = c.writeFrame(0xA, payload)
		case 0xA: // pong — игнорируем
		default: // прочие (бинарные/продолжение) — пропускаем
		}
	}
}

// WriteText шлёт текстовый кадр.
func (c *Conn) WriteText(s string) error { return c.writeFrame(0x1, []byte(s)) }

// Close вежливо закрывает соединение.
func (c *Conn) Close() error {
	_ = c.writeFrame(0x8, nil)
	return c.c.Close()
}

func (c *Conn) readFrame() (opcode byte, payload []byte, err error) {
	h := make([]byte, 2)
	if _, err = io.ReadFull(c.rw, h); err != nil {
		return
	}
	opcode = h[0] & 0x0f
	masked := h[1]&0x80 != 0
	n := int64(h[1] & 0x7f)
	switch n {
	case 126:
		ext := make([]byte, 2)
		if _, err = io.ReadFull(c.rw, ext); err != nil {
			return
		}
		n = int64(binary.BigEndian.Uint16(ext))
	case 127:
		ext := make([]byte, 8)
		if _, err = io.ReadFull(c.rw, ext); err != nil {
			return
		}
		n = int64(binary.BigEndian.Uint64(ext))
	}
	if n < 0 || n > 1<<20 { // защита: кадр до 1 МБ
		return 0, nil, errors.New("слишком большой кадр")
	}
	var mask []byte
	if masked {
		mask = make([]byte, 4)
		if _, err = io.ReadFull(c.rw, mask); err != nil {
			return
		}
	}
	payload = make([]byte, n)
	if _, err = io.ReadFull(c.rw, payload); err != nil {
		return
	}
	if masked {
		for i := range payload {
			payload[i] ^= mask[i%4]
		}
	}
	return opcode, payload, nil
}

func (c *Conn) writeFrame(opcode byte, payload []byte) error {
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	var head []byte
	b0 := byte(0x80) | opcode // FIN=1
	n := len(payload)
	switch {
	case n < 126:
		head = []byte{b0, byte(n)}
	case n < 1<<16:
		head = []byte{b0, 126, byte(n >> 8), byte(n)}
	default:
		head = make([]byte, 10)
		head[0] = b0
		head[1] = 127
		binary.BigEndian.PutUint64(head[2:], uint64(n))
	}
	if _, err := c.rw.Write(head); err != nil {
		return err
	}
	if _, err := c.rw.Write(payload); err != nil {
		return err
	}
	return c.rw.Flush()
}
