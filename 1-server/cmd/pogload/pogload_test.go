package main

import (
	"context"
	"encoding/json"
	"math"
	"net/http/httptest"
	"testing"
	"time"

	"pogranichye/server"
)

// --- модульные тесты парсера/генератора (без сети) ---

func TestPercentilesOf(t *testing.T) {
	if p := percentilesOf(nil); p.Count != 0 || p.P50 != 0 {
		t.Fatalf("пустой набор должен давать нули: %+v", p)
	}
	v := []float64{1, 2, 3, 4, 5, 6, 7, 8, 9, 10}
	p := percentilesOf(v)
	if p.Count != 10 {
		t.Fatalf("count=%d", p.Count)
	}
	if p.Max != 10 {
		t.Fatalf("max=%v", p.Max)
	}
	if p.P50 < 5 || p.P50 > 7 {
		t.Fatalf("p50 вне ожидаемого: %v", p.P50)
	}
	if p.P99 < p.P95 || p.P95 < p.P50 {
		t.Fatalf("перцентили не монотонны: %+v", p)
	}
}

func TestMatchResult(t *testing.T) {
	frame := `{"v":1,"alive":true,"me":{"id":"42","result":{"reqid":"r-abc","ok":true,"reason":""}}}`
	found, ok := matchResult([]byte(frame), "r-abc")
	if !found || !ok {
		t.Fatalf("ожидали found&ok, получили %v %v", found, ok)
	}
	if f, _ := matchResult([]byte(frame), "r-other"); f {
		t.Fatalf("не должно совпасть с чужим reqid")
	}
	if f, _ := matchResult([]byte(`{bad json`), "r-abc"); f {
		t.Fatalf("битый JSON не должен совпадать")
	}
}

func TestIsLocalAndJSONLP(t *testing.T) {
	for _, a := range []string{"http://localhost:8090", "http://127.0.0.1:1", "http://[::1]:2"} {
		if !isLocal(a) {
			t.Fatalf("%s должен быть local", a)
		}
	}
	if isLocal("http://example.com") {
		t.Fatal("удалённый адрес не local")
	}
	var m map[string]string
	if json.Unmarshal([]byte(jsonLP("kate", "secret")), &m) != nil || m["login"] != "kate" || m["pass"] != "secret" {
		t.Fatalf("jsonLP некорректен: %q", jsonLP("kate", "secret"))
	}
}

func TestUnitVec(t *testing.T) {
	x, y := unitVec(0)
	if math.Abs(x-1) > 1e-9 || math.Abs(y) > 1e-9 {
		t.Fatalf("0° должно быть (1,0), получили (%v,%v)", x, y)
	}
	if dx, dy := unitVec(123); math.Abs(dx*dx+dy*dy-1) > 1e-9 {
		t.Fatalf("вектор не единичный: |%v,%v|", dx, dy)
	}
}

// --- интеграционный «живой» дым: настоящие WS-клиенты против httptest-сервера ---

// TestLoadSmoke поднимает реальный сервер (несколько сёл, чтобы хватило тел),
// прогоняет маленькую нагрузку через runStep и проверяет жёсткие
// функциональные критерии: все клиенты получили валидный первый кадр, нет
// дублированного управления телом, после закрытия сессии освобождаются.
func TestLoadSmoke(t *testing.T) {
	if testing.Short() {
		t.Skip("сетевой дым пропущен в -short")
	}
	s := server.New("pogload-smoke", 8, "", []byte("secret"), "")
	ts := httptest.NewServer(s.Handler())
	defer ts.Close()

	const players = 4
	cfg := config{
		addr:      ts.URL,
		n:         players,
		scenario:  "distributed",
		duration:  1500 * time.Millisecond,
		connTO:    8 * time.Second,
		actEvery:  300 * time.Millisecond,
		httpEvery: 500 * time.Millisecond,
		churnPct:  0,
		fastHz:    4,
		runID:     "test-" + randHex(2),
	}
	m := newMetrics()
	sr := runStep(context.Background(), cfg, players, m)

	if sr.ValidFirstFrame < players {
		t.Fatalf("валидный первый кадр получили %d/%d (busy=%d none=%d)",
			sr.ValidFirstFrame, players, sr.BodyBusy, sr.BodyNone)
	}
	if dup := sr.Counters["body.duplicate"]; dup > 0 {
		t.Fatalf("обнаружено дублированное управление телом: %d", dup)
	}
	if sr.Snapshots == 0 || sr.SnapshotBytes == 0 {
		t.Fatalf("не получено ни одного кадра: snapshots=%d bytes=%d", sr.Snapshots, sr.SnapshotBytes)
	}
	if sr.AckLatMs.Count == 0 {
		t.Fatalf("ни одного ACK на op:goal — проверь контракт me.result")
	}
	if sr.Counters["release.ok"] == 0 {
		t.Fatalf("после закрытия тело не освободилось (release.ok=0, no_body=%d)", sr.Counters["release.no_body"])
	}
	// HTTP-опрос во время нагрузки должен был отработать без фатальных ошибок.
	if _, ok := sr.HTTPLatency["stats"]; !ok {
		t.Fatalf("нет замеров латентности /api/stats — фоновый HTTP-опрос не отработал")
	}
	t.Logf("smoke ok: first=%d/%d snaps=%d bytes=%d ack.n=%d aoi_max=%d release.ok=%d",
		sr.ValidFirstFrame, players, sr.Snapshots, sr.SnapshotBytes,
		sr.AckLatMs.Count, sr.AOIMax, sr.Counters["release.ok"])
}
