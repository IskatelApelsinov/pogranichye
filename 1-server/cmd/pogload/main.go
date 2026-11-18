// Command pogload — синтетический нагрузочный стенд сервера «Приграничье».
//
// Инструмент подключает N настоящих WebSocket-игроков к УЖЕ ЗАПУЩЕННОМУ
// серверу: каждый бот проходит /api/register → /api/login → /ws?token=...,
// читает реальные кадры v1, шлёт поддерживаемые intents (op:"goal"/op:"move")
// и, где предусмотрено, проверяет ACK (me.result по reqid). Параллельно
// опрашиваются публичные /api/world, /api/terrain, /api/layout, /api/stats.
// Итог — машинный JSON-отчёт + короткая читаемая сводка.
//
// Это ТОЛЬКО тестовый инструмент: контракт v1 не меняется, новых intents нет,
// production-аккаунты/сохранения не используются. Пароли и токены в лог не
// пишутся. Префикс логинов уникален на прогон (runID), чтобы повторные запуски
// не сталкивались.
//
// Примеры:
//
//	# поднять изолированный сервер (отдельный save, быстрый тик):
//	go run ./cmd/pogserver -addr :8090 -villages 20 -save pogload.sav -tick 400
//	# и в другом терминале — 100 игроков, распределённо, 60с:
//	go run ./cmd/pogload -addr http://localhost:8090 -n 100 -scenario distributed -duration 60s
//
//	# ступенчатая разведка предела:
//	go run ./cmd/pogload -addr http://localhost:8090 -ramp 1,10,25,50,100 -duration 30s
//
// Жёсткие функциональные критерии (см. comms 0284): все запрошенные WS-клиенты
// подключились и получили валидный первый snapshot; нет дублированного
// управления телом; после закрытия сессии освобождены; HTTP API продолжает
// отвечать. Пороги производительности здесь НЕ задаются — сначала baseline.
package main

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"math"
	"math/big"
	"os"
	"os/signal"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// commitSHA и buildInfo подставляются через -ldflags при сборке в CI; иначе "".
var commitSHA = ""

type config struct {
	addr        string
	n           int
	ramp        []int
	scenario    string
	duration    time.Duration
	connTO      time.Duration
	actEvery    time.Duration
	httpEvery   time.Duration
	churnPct    int
	fastHz      int
	reportPath  string
	runID       string
	allowRemote bool
}

func main() {
	var (
		addr        = flag.String("addr", "http://localhost:8090", "базовый URL сервера")
		n           = flag.Int("n", 100, "число одновременных WS-игроков (игнорируется, если задан -ramp)")
		rampStr     = flag.String("ramp", "", "ступени нагрузки через запятую, напр. 1,10,25,50,100 (вместо -n)")
		scenario    = flag.String("scenario", "distributed", "co-located|distributed|churn|mixed")
		duration    = flag.Duration("duration", 30*time.Second, "длительность установившейся фазы на каждой ступени")
		connTO      = flag.Duration("connect-timeout", 10*time.Second, "таймаут connect/handshake/first-frame")
		actEvery    = flag.Duration("act-every", 2*time.Second, "период отправки действий игроком")
		httpEvery   = flag.Duration("http-every", 3*time.Second, "период фоновых GET /api/* (0 — выключить)")
		churnPct    = flag.Int("churn", 8, "доля игроков, уходящих и переподключающихся в churn/mixed, %")
		fastHz      = flag.Int("fast", 4, "частота быстрых кадров WS (?fast=), 0 — только раз в секунду")
		reportPath  = flag.String("report", "pogload-report.json", "куда сохранить JSON-отчёт (\"\" — не сохранять)")
		runID       = flag.String("run-id", "", "идентификатор прогона (по умолчанию случайный) — префикс логинов")
		allowRemote = flag.Bool("i-understand-remote", false, "разрешить нагружать не-localhost target")
	)
	flag.Parse()

	cfg := config{
		addr: strings.TrimRight(*addr, "/"), n: *n, scenario: *scenario,
		duration: *duration, connTO: *connTO, actEvery: *actEvery,
		httpEvery: *httpEvery, churnPct: *churnPct, fastHz: *fastHz,
		reportPath: *reportPath, runID: *runID, allowRemote: *allowRemote,
	}
	if *rampStr != "" {
		for _, p := range strings.Split(*rampStr, ",") {
			if v, err := strconv.Atoi(strings.TrimSpace(p)); err == nil && v > 0 {
				cfg.ramp = append(cfg.ramp, v)
			}
		}
	}
	if len(cfg.ramp) == 0 {
		cfg.ramp = []int{cfg.n}
	}
	if cfg.runID == "" {
		cfg.runID = randHex(4)
	}
	if !isLocal(cfg.addr) && !cfg.allowRemote {
		fmt.Fprintf(os.Stderr, "ОТКАЗ: target %s не localhost. Для удалённого запуска добавь -i-understand-remote (не использовать на production!).\n", cfg.addr)
		os.Exit(2)
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()

	rep := runAll(ctx, cfg, newMetrics)
	printSummary(rep)
	if cfg.reportPath != "" {
		if b, err := json.MarshalIndent(rep, "", "  "); err == nil {
			if err := os.WriteFile(cfg.reportPath, b, 0o644); err == nil {
				fmt.Printf("\nJSON-отчёт: %s\n", cfg.reportPath)
			}
		}
	}
	if !rep.Passed {
		os.Exit(1)
	}
}

// Report — машинный отчёт прогона (всех ступеней).
type Report struct {
	RunID       string       `json:"run_id"`
	Scenario    string       `json:"scenario"`
	Target      string       `json:"target"`
	CommitSHA   string       `json:"commit_sha"`
	GoVersion   string       `json:"go_version"`
	OSArch      string       `json:"os_arch"`
	StartedAt   string       `json:"started_at"`
	WallClock   string       `json:"wall_clock"`
	Passed      bool         `json:"passed"`
	FailReasons []string     `json:"fail_reasons,omitempty"`
	Steps       []StepReport `json:"steps"`
}

// StepReport — итог одной ступени нагрузки (N игроков).
type StepReport struct {
	Players          int                    `json:"players"`
	Connected        int                    `json:"connected"`
	ValidFirstFrame  int                    `json:"valid_first_frame"`
	BodyBusy         int64                  `json:"body_busy"`
	BodyNone         int64                  `json:"body_none"`
	RegisterLatMs    percentiles            `json:"register_latency"`
	LoginLatMs       percentiles            `json:"login_latency"`
	WSConnectLatMs   percentiles            `json:"ws_connect_latency"`
	FirstFrameLatMs  percentiles            `json:"first_frame_latency"`
	AckLatMs         percentiles            `json:"ack_latency"`
	ReconnectLatMs   percentiles            `json:"reconnect_latency"`
	FrameSizeBytes   percentiles            `json:"frame_size_bytes"`
	Snapshots        int64                  `json:"snapshots"`
	SnapshotBytes    int64                  `json:"snapshot_bytes"`
	SnapshotBytesSec float64                `json:"snapshot_bytes_per_sec"`
	AOIMax           int                    `json:"aoi_max"`
	AOIAvg           float64                `json:"aoi_avg"`
	HTTPLatency      map[string]percentiles `json:"http_latency"`
	Counters         map[string]int64       `json:"counters"`
	ServerStats      map[string]any         `json:"server_stats,omitempty"`
	LoadgenRuntime   runtimeStats           `json:"loadgen_runtime"`
	DurationSec      float64                `json:"duration_sec"`
}

type runtimeStats struct {
	Goroutines int    `json:"goroutines"`
	HeapAllocB uint64 `json:"heap_alloc_bytes"`
	SysB       uint64 `json:"sys_bytes"`
	NumGC      uint32 `json:"num_gc"`
}

func runAll(ctx context.Context, cfg config, newM func() *metrics) *Report {
	rep := &Report{
		RunID: cfg.runID, Scenario: cfg.scenario, Target: cfg.addr,
		CommitSHA: commitSHA, GoVersion: runtime.Version(),
		OSArch:    runtime.GOOS + "/" + runtime.GOARCH,
		StartedAt: time.Now().UTC().Format(time.RFC3339),
	}
	t0 := time.Now()
	for _, step := range cfg.ramp {
		if ctx.Err() != nil {
			break
		}
		sr := runStep(ctx, cfg, step, newM())
		rep.Steps = append(rep.Steps, sr)
		// Функциональные критерии (без порогов производительности):
		if sr.ValidFirstFrame < step {
			rep.FailReasons = append(rep.FailReasons,
				fmt.Sprintf("ступень %d: валидный первый кадр получили только %d/%d", step, sr.ValidFirstFrame, step))
		}
		if dup := sr.Counters["body.duplicate"]; dup > 0 {
			rep.FailReasons = append(rep.FailReasons,
				fmt.Sprintf("ступень %d: обнаружено дублированное управление телом (%d)", step, dup))
		}
	}
	rep.WallClock = time.Since(t0).Round(time.Millisecond).String()
	rep.Passed = len(rep.FailReasons) == 0 && len(rep.Steps) > 0
	return rep
}

// runStep выполняет одну ступень: рампованный запуск player ботов, установившаяся
// фаза длиной cfg.duration, затем аккуратное закрытие и проверка освобождения.
func runStep(ctx context.Context, cfg config, players int, m *metrics) StepReport {
	fmt.Printf("== ступень: %d игроков (сценарий %s) ==\n", players, cfg.scenario)
	stepCtx, cancel := context.WithCancel(ctx)
	defer cancel()

	h := newHTTPClient(cfg.addr, m)
	var connected, validFirst int64

	// Фоновый опрос публичных /api/* во время нагрузки.
	var pollWG sync.WaitGroup
	if cfg.httpEvery > 0 {
		pollWG.Add(1)
		go func() {
			defer pollWG.Done()
			httpPoller(stepCtx, h, cfg.httpEvery)
		}()
	}

	var wg sync.WaitGroup
	// Ступенчатый ramp: равномерно распределяем старты ботов по ~1с.
	rampWindow := time.Second
	gap := time.Duration(0)
	if players > 1 {
		gap = rampWindow / time.Duration(players)
	}
	for i := 0; i < players; i++ {
		select {
		case <-stepCtx.Done():
		default:
		}
		wg.Add(1)
		go func(idx int) {
			defer wg.Done()
			b := &bot{cfg: cfg, m: m, h: h, idx: idx,
				login: fmt.Sprintf("pl-%s-%d-%d", cfg.runID, players, idx)}
			if b.connect(stepCtx) {
				atomic.AddInt64(&connected, 1)
				if b.gotFirst {
					atomic.AddInt64(&validFirst, 1)
				}
				b.run(stepCtx)
			}
		}(i)
		if gap > 0 {
			t := time.NewTimer(gap)
			select {
			case <-t.C:
			case <-stepCtx.Done():
				t.Stop()
			}
		}
	}

	// Установившаяся фаза.
	steadyStart := time.Now()
	select {
	case <-time.After(cfg.duration):
	case <-stepCtx.Done():
	}
	steadyDur := time.Since(steadyStart)

	// Снимем серверную сводку до закрытия, пока игроки ещё в мире.
	serverStats := h.getStats(context.Background())

	cancel()  // велим ботам закрыться
	wg.Wait() // дождёмся завершения
	pollWG.Wait()

	// Проверка освобождения сессий: после ухода всех players игроков сервер
	// должен снова отдавать тела. Пробуем переселиться одним контрольным ботом.
	verifyRelease(cfg, m, h)

	m.bytesMu.Lock()
	snaps, bytesTotal := m.snapFrames, m.snapBytes
	m.bytesMu.Unlock()
	m.aoiMu.Lock()
	aoiMax, aoiSum, aoiN := m.aoiMax, m.aoiSum, m.aoiN
	m.aoiMu.Unlock()
	var aoiAvg float64
	if aoiN > 0 {
		aoiAvg = float64(aoiSum) / float64(aoiN)
	}
	var bps float64
	if steadyDur > 0 {
		bps = float64(bytesTotal) / steadyDur.Seconds()
	}

	httpP := map[string]percentiles{}
	m.httpMu.Lock()
	for k, s := range m.httpLat {
		httpP[k] = s.pct()
	}
	m.httpMu.Unlock()

	var rs runtime.MemStats
	runtime.ReadMemStats(&rs)
	cnt := m.c.snapshot()

	return StepReport{
		Players: players, Connected: int(connected), ValidFirstFrame: int(validFirst),
		BodyBusy: cnt["body.busy"], BodyNone: cnt["body.none"],
		RegisterLatMs: m.registerLat.pct(), LoginLatMs: m.loginLat.pct(),
		WSConnectLatMs: m.wsConnLat.pct(), FirstFrameLatMs: m.firstFrame.pct(),
		AckLatMs: m.ackLat.pct(), ReconnectLatMs: m.reconnect.pct(),
		FrameSizeBytes: m.frameSize.pct(),
		Snapshots:      snaps, SnapshotBytes: bytesTotal, SnapshotBytesSec: round2(bps),
		AOIMax: aoiMax, AOIAvg: round2(aoiAvg),
		HTTPLatency: httpP, Counters: cnt, ServerStats: serverStats,
		LoadgenRuntime: runtimeStats{Goroutines: runtime.NumGoroutine(),
			HeapAllocB: rs.HeapAlloc, SysB: rs.Sys, NumGC: rs.NumGC},
		DurationSec: round2(steadyDur.Seconds()),
	}
}

// bot — один синтетический игрок.
type bot struct {
	cfg      config
	m        *metrics
	h        *httpClient
	idx      int
	login    string
	pass     string
	token    string
	conn     *wsConn
	meID     string
	gotFirst bool
}

// connect: register → login → ws → первый кадр. Возвращает true, если WS открыт.
func (b *bot) connect(ctx context.Context) bool {
	b.pass = "p" + randHex(8) // пароль не логируем
	// register
	_, d, err := b.h.postJSON(ctx, "/api/register", jsonLP(b.login, b.pass))
	b.m.registerLat.add(d)
	if err != nil {
		b.m.c.inc("register.err")
		return false
	}
	b.m.c.inc("register.ok")
	// login
	lr, d, err := b.h.postJSON(ctx, "/api/login", jsonLP(b.login, b.pass))
	b.m.loginLat.add(d)
	if err != nil {
		b.m.c.inc("login.err")
		return false
	}
	tok, _ := lr["token"].(string)
	if tok == "" {
		b.m.c.inc("login.no_token")
		return false
	}
	b.token = tok // токен в лог НЕ пишем
	b.m.c.inc("login.ok")
	return b.openWS(ctx)
}

func (b *bot) openWS(ctx context.Context) bool {
	path := "/ws?token=" + b.token
	if b.cfg.fastHz >= 2 {
		path += "&fast=" + strconv.Itoa(b.cfg.fastHz)
	}
	t0 := time.Now()
	conn, err := wsDial(b.cfg.addr, path, b.cfg.connTO)
	if err != nil {
		// Разбор отказа резервирования тела по коду статуса.
		switch {
		case strings.Contains(err.Error(), "409"):
			b.m.c.inc("body.busy")
		case strings.Contains(err.Error(), "503"):
			b.m.c.inc("body.none")
		default:
			b.m.c.inc("ws.handshake_err")
		}
		return false
	}
	b.m.wsConnLat.add(time.Since(t0))
	b.m.c.inc("ws.connected")
	b.conn = conn
	// первый кадр (snapshot)
	ff0 := time.Now()
	payload, err := conn.readText(time.Now().Add(b.cfg.connTO))
	if err != nil {
		b.m.c.inc("first_frame.err")
		conn.close()
		b.conn = nil
		return false
	}
	b.m.firstFrame.add(time.Since(ff0))
	if b.ingest(payload, true) {
		b.gotFirst = true
		b.m.c.inc("first_frame.ok")
	} else {
		b.m.c.inc("first_frame.invalid")
	}
	return true
}

// ingest разбирает кадр: считает байты/AOI, проверяет alive и запоминает me.id.
// first=true — обязателен валидный alive+me.id (жёсткий критерий первого кадра).
func (b *bot) ingest(payload []byte, first bool) bool {
	b.m.frame(payload)
	var f map[string]any
	if json.Unmarshal(payload, &f) != nil {
		b.m.c.inc("frame.malformed")
		return false
	}
	if v, _ := f["v"].(float64); v != 1 {
		b.m.c.inc("frame.bad_version")
		return false
	}
	alive, _ := f["alive"].(bool)
	if !alive {
		if first {
			return false
		}
		b.m.c.inc("frame.not_alive")
		return true
	}
	if me, ok := f["me"].(map[string]any); ok {
		if id, _ := me["id"].(string); id != "" {
			if first {
				b.meID = id
			} else if b.meID != "" && id != b.meID {
				// тело сменилось под той же сессией — это и есть дублированное управление
				b.m.c.inc("body.duplicate")
			}
		} else if first {
			return false
		}
	} else if first {
		return false
	}
	if place, ok := f["place"].(map[string]any); ok {
		if ns, ok := place["neighbors"].([]any); ok {
			b.m.aoi(len(ns))
		}
	}
	return true
}

// run: установившаяся фаза одного бота — читаем кадры, периодически шлём
// действия и проверяем ACK; churn-боты уходят и переподключаются.
func (b *bot) run(ctx context.Context) {
	if b.conn == nil {
		return
	}
	defer func() {
		if b.conn != nil {
			b.conn.close()
			b.conn = nil
		}
	}()

	// Решаем, churn ли этот бот.
	churn := (b.cfg.scenario == "churn" || b.cfg.scenario == "mixed") &&
		b.cfg.churnPct > 0 && randInt(100) < b.cfg.churnPct

	actT := time.NewTicker(b.cfg.actEvery)
	defer actT.Stop()
	var churnAt <-chan time.Time
	if churn {
		// Уход где-то в первой трети фазы.
		churnAt = time.After(time.Duration(randInt(int(b.cfg.duration/3/time.Millisecond)+1)) * time.Millisecond)
	}

	// Ожидаемый reqid для измерения ACK-латентности.
	var pendingReqid string
	var pendingSince time.Time

	// Читаем кадры в фоне, чтобы не блокировать действия.
	frames := make(chan []byte, 64)
	readDone := make(chan struct{})
	go func() {
		defer close(readDone)
		for {
			payload, err := b.conn.readText(time.Now().Add(5 * time.Second))
			if err != nil {
				return
			}
			select {
			case frames <- payload:
			case <-ctx.Done():
				return
			}
		}
	}()

	for {
		select {
		case <-ctx.Done():
			return
		case <-readDone:
			return
		case payload := <-frames:
			b.ingest(payload, false)
			if pendingReqid != "" {
				if ok, _ := matchResult(payload, pendingReqid); ok {
					b.m.ackLat.add(time.Since(pendingSince))
					b.m.c.inc("ack.ok")
					pendingReqid = ""
				}
			}
		case <-actT.C:
			// Если прошлый ACK ещё не пришёл — считаем как потерянный/медленный.
			if pendingReqid != "" {
				b.m.c.inc("ack.timeout")
				pendingReqid = ""
			}
			reqid := b.sendAction()
			if reqid != "" {
				pendingReqid = reqid
				pendingSince = time.Now()
			}
		case <-churnAt:
			churnAt = nil
			b.doChurn(ctx)
			if b.conn == nil {
				return
			}
			// перезапустить читатель после reconnect
			go func() {
				for {
					payload, err := b.conn.readText(time.Now().Add(5 * time.Second))
					if err != nil {
						return
					}
					select {
					case frames <- payload:
					case <-ctx.Done():
						return
					}
				}
			}()
		}
	}
}

// sendAction шлёт поддерживаемый intent. op:"goal" возвращает ACK по reqid
// (измеряем латентность); op:"move" — поток шага без ACK.
func (b *bot) sendAction() string {
	// В co-located все стоят на месте (worst-case AOI) — только goal.
	if b.cfg.scenario != "co-located" && randInt(2) == 0 {
		// лёгкий шаг: единичный вектор, маленький dt
		dx, dy := unitVec(randInt(360))
		_ = b.conn.writeText(fmt.Sprintf(`{"v":1,"op":"move","mx":%.3f,"my":%.3f,"dt":0.1}`, dx, dy))
		b.m.c.inc("act.move")
		return ""
	}
	goals := []string{"auto", "work", "rest", "socialize", "wander"}
	g := goals[randInt(len(goals))]
	reqid := "r-" + randHex(6)
	if b.conn.writeText(fmt.Sprintf(`{"v":1,"op":"goal","goal":%q,"reqid":%q}`, g, reqid)) != nil {
		b.m.c.inc("act.goal_err")
		return ""
	}
	b.m.c.inc("act.goal")
	return reqid
}

// doChurn закрывает соединение и переподключается тем же аккаунтом, измеряя
// время reconnect и подтверждая, что тело снова выдано.
func (b *bot) doChurn(ctx context.Context) {
	if b.conn != nil {
		b.conn.close()
		b.conn = nil
	}
	// Небольшая пауза, чтобы сервер успел освободить сессию.
	select {
	case <-time.After(200 * time.Millisecond):
	case <-ctx.Done():
		return
	}
	t0 := time.Now()
	if b.openWS(ctx) {
		b.m.reconnect.add(time.Since(t0))
		b.m.c.inc("reconnect.ok")
	} else {
		b.m.c.inc("reconnect.fail")
	}
}

// verifyRelease: после закрытия всех ботов сервер должен снова выдать тело.
func verifyRelease(cfg config, m *metrics, h *httpClient) {
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	login := fmt.Sprintf("rel-%s-%d", cfg.runID, time.Now().UnixNano()%100000)
	pass := "p" + randHex(8)
	if _, _, err := h.postJSON(ctx, "/api/register", jsonLP(login, pass)); err != nil {
		return
	}
	lr, _, err := h.postJSON(ctx, "/api/login", jsonLP(login, pass))
	if err != nil {
		return
	}
	tok, _ := lr["token"].(string)
	if tok == "" {
		return
	}
	conn, err := wsDial(cfg.addr, "/ws?token="+tok, cfg.connTO)
	if err != nil {
		m.c.inc("release.no_body") // тела не освободились
		return
	}
	defer conn.close()
	if _, err := conn.readText(time.Now().Add(cfg.connTO)); err == nil {
		m.c.inc("release.ok")
	}
}

// httpPoller гоняет публичные GET endpoints во время нагрузки.
func httpPoller(ctx context.Context, h *httpClient, every time.Duration) {
	t := time.NewTicker(every)
	defer t.Stop()
	chunks := [][2]int{{0, 0}, {1, 0}, {0, 1}, {5, 5}, {-1, -1}, {-3, 2}}
	ci := 0
	for {
		select {
		case <-ctx.Done():
			return
		case <-t.C:
			_ = h.get(ctx, "world", "/api/world")
			_ = h.get(ctx, "stats", "/api/stats")
			_ = h.get(ctx, "layout", "/api/layout?site=0")
			cx, cz := chunks[ci%len(chunks)][0], chunks[ci%len(chunks)][1]
			ci++
			_ = h.get(ctx, "terrain", fmt.Sprintf("/api/terrain?cx=%d&cz=%d&step=8", cx, cz))
			_ = h.get(ctx, "spaces", fmt.Sprintf("/api/spaces?cx=%d&cz=%d", cx, cz))
		}
	}
}

// matchResult: кадр содержит me.result.reqid==reqid? → (нашли, ok).
func matchResult(payload []byte, reqid string) (bool, bool) {
	var f struct {
		Me struct {
			Result struct {
				Reqid string `json:"reqid"`
				Ok    bool   `json:"ok"`
			} `json:"result"`
		} `json:"me"`
	}
	if json.Unmarshal(payload, &f) != nil {
		return false, false
	}
	if f.Me.Result.Reqid == reqid {
		return true, f.Me.Result.Ok
	}
	return false, false
}

// --- мелкие помощники ---

func jsonLP(login, pass string) string {
	b, _ := json.Marshal(map[string]string{"login": login, "pass": pass})
	return string(b)
}

func randHex(nBytes int) string {
	b := make([]byte, nBytes)
	rand.Read(b)
	return hex.EncodeToString(b)
}

func randInt(n int) int {
	if n <= 0 {
		return 0
	}
	v, err := rand.Int(rand.Reader, big.NewInt(int64(n)))
	if err != nil {
		return 0
	}
	return int(v.Int64())
}

func unitVec(deg int) (float64, float64) {
	r := float64(deg) * math.Pi / 180
	return math.Cos(r), math.Sin(r)
}

func round2(f float64) float64 {
	return float64(int64(f*100+0.5)) / 100
}

func isLocal(addr string) bool {
	a := strings.ToLower(addr)
	return strings.Contains(a, "localhost") || strings.Contains(a, "127.0.0.1") || strings.Contains(a, "[::1]")
}

func printSummary(r *Report) {
	fmt.Printf("\n=== pogload: прогон %s (сценарий %s, target %s) ===\n", r.RunID, r.Scenario, r.Target)
	fmt.Printf("Go %s · %s · commit %s · wall %s\n", r.GoVersion, r.OSArch, orDash(r.CommitSHA), r.WallClock)
	for _, s := range r.Steps {
		fmt.Printf("\n-- %d игроков --\n", s.Players)
		fmt.Printf("  подключились: %d · валидный первый кадр: %d/%d · busy=%d none=%d\n",
			s.Connected, s.ValidFirstFrame, s.Players, s.BodyBusy, s.BodyNone)
		fmt.Printf("  connect p50/p95/p99 мс: %.1f/%.1f/%.1f · first-frame: %.1f/%.1f/%.1f\n",
			s.WSConnectLatMs.P50, s.WSConnectLatMs.P95, s.WSConnectLatMs.P99,
			s.FirstFrameLatMs.P50, s.FirstFrameLatMs.P95, s.FirstFrameLatMs.P99)
		fmt.Printf("  ACK p50/p95/p99 мс: %.1f/%.1f/%.1f (n=%d) · reconnect ok=%d\n",
			s.AckLatMs.P50, s.AckLatMs.P95, s.AckLatMs.P99, s.AckLatMs.Count, s.Counters["reconnect.ok"])
		fmt.Printf("  кадры: %d · %d байт · %.0f байт/с · размер p50/p95/p99: %.0f/%.0f/%.0f\n",
			s.Snapshots, s.SnapshotBytes, s.SnapshotBytesSec,
			s.FrameSizeBytes.P50, s.FrameSizeBytes.P95, s.FrameSizeBytes.P99)
		fmt.Printf("  AOI max=%d avg=%.1f · release: ok=%d no_body=%d · body.duplicate=%d\n",
			s.AOIMax, s.AOIAvg, s.Counters["release.ok"], s.Counters["release.no_body"], s.Counters["body.duplicate"])
	}
	fmt.Printf("\nИТОГ: %s\n", passWord(r.Passed))
	for _, fr := range r.FailReasons {
		fmt.Printf("  ✗ %s\n", fr)
	}
}

func orDash(s string) string {
	if s == "" {
		return "-"
	}
	return s
}
func passWord(ok bool) string {
	if ok {
		return "PASS (функциональные критерии выполнены)"
	}
	return "FAIL"
}
