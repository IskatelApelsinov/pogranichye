package server

import (
	"bytes"
	"io"
	"net/http"
	"os"
	"path/filepath"

	"pogranichye/wsrv"
)

// тонкие обёртки над wsrv, чтобы server не зависел от деталей.
type wsConn = wsrv.Conn

func wsUpgrade(w http.ResponseWriter, r *http.Request) (*wsrv.Conn, error) { return wsrv.Upgrade(w, r) }

// openIndex отдаёт index.html тонкого клиента из webDir; если файла нет — встроенную
// минимальную страницу-заглушку, чтобы сервер всегда поднимался «из коробки».
func openIndex(webDir string) io.ReadSeeker {
	if webDir != "" {
		if data, err := os.ReadFile(filepath.Join(webDir, "index.html")); err == nil {
			return bytes.NewReader(data)
		}
	}
	return bytes.NewReader([]byte(fallbackIndex))
}

const fallbackIndex = `<!doctype html><meta charset="utf-8"><title>Приграничье</title>
<body style="font:16px sans-serif;max-width:640px;margin:40px auto;padding:0 16px">
<h1>Приграничье — сервер жив</h1>
<p>Тонкий клиент не найден в webDir. Запусти сервер с флагом <code>-web ../thin-client/web</code>
или открой тонкий клиент из каталога <code>thin-client/</code>.</p></body>`
