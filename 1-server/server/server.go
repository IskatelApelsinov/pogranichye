// Пакет server — сетевой слой мира: HTTP для регистрации/входа (через службу accounts) и
// WebSocket, по которому игрок управляет ПЕРСОНАЖЕМ живого мира. Мир крутится сам в отдельной
// горутине; подключился игрок — «вселяется» в тело (controller), отключился — телом снова правит
// AI (простая лестница присутствия). Протокол engine-agnostic (JSON): так же подключится и Unity.
package server

import (
	"encoding/json"
	"net/http"
	"strconv"
	"sync"
	"time"

	"pogranichye/accounts"
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
	"pogranichye/sim"
)

// Server держит мир под замком и крутит его во времени.
type Server struct {
	mu       sync.Mutex
	world    *core.World
	runner   *core.Runner
	acc      *accounts.Store
	webDir   string
	tickEach time.Duration

	ctl     map[core.ID]uint64 // персонаж → id сессии, что им правит (0/нет — свободен; AI не трогает цель)
	nextSID uint64             // счётчик сессий: у каждого ws-подключения свой id владения телом
}

// New создаёт сервер: генерирует мир и службу аккаунтов.
func New(seed string, villages int, accPath string, secret []byte, webDir string) *Server {
	w, hub := sim.Generate(seed, villages)
	r := core.NewRunnerWith(hub, core.Clock{}, sim.Default()...)
	return &Server{
		world: w, runner: r,
		acc:      accounts.NewStore(accPath, secret),
		webDir:   webDir,
		tickEach: 400 * time.Millisecond, // один час мира за 0.4с → сутки ~10с (видно жизнь)
		ctl:      map[core.ID]uint64{},
	}
}

// Handler собирает маршруты (отдельно от Run — удобно для тестов).
func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/register", s.handleRegister)
	mux.HandleFunc("/api/login", s.handleLogin)
	mux.HandleFunc("/ws", s.handleWS)
	mux.HandleFunc("/", s.handleIndex)
	return mux
}

// Run запускает тик-петлю и HTTP-сервер (блокирующе).
func (s *Server) Run(addr string) error {
	go s.tickLoop()
	srv := &http.Server{Addr: addr, Handler: s.Handler()}
	return srv.ListenAndServe()
}

// Step прокручивает мир на один час (для тестов/инструментов — вне обычной тик-петли).
func (s *Server) Step() {
	s.mu.Lock()
	s.runner.Step(s.world)
	s.mu.Unlock()
}

func (s *Server) tickLoop() {
	t := time.NewTicker(s.tickEach)
	defer t.Stop()
	for range t.C {
		s.mu.Lock()
		s.runner.Step(s.world)
		s.mu.Unlock()
	}
}

// --- HTTP: аккаунты ---

func (s *Server) handleRegister(w http.ResponseWriter, r *http.Request) {
	var in struct{ Login, Pass string }
	if json.NewDecoder(r.Body).Decode(&in) != nil {
		writeJSON(w, map[string]any{"ok": false, "msg": "плохой запрос"})
		return
	}
	if _, err := s.acc.Register(in.Login, in.Pass); err != nil {
		writeJSON(w, map[string]any{"ok": false, "msg": err.Error()})
		return
	}
	writeJSON(w, map[string]any{"ok": true})
}

func (s *Server) handleLogin(w http.ResponseWriter, r *http.Request) {
	var in struct{ Login, Pass string }
	if json.NewDecoder(r.Body).Decode(&in) != nil {
		writeJSON(w, map[string]any{"ok": false, "msg": "плохой запрос"})
		return
	}
	tok, _, err := s.acc.Login(in.Login, in.Pass)
	if err != nil {
		writeJSON(w, map[string]any{"ok": false, "msg": err.Error()})
		return
	}
	writeJSON(w, map[string]any{"ok": true, "token": tok})
}

// --- выбор/привязка персонажа ---

// reserveBody АТОМАРНО (под одним замком) находит игроку тело и СРАЗУ резервирует его за новой
// сессией — чтобы второй WS с тем же токеном не завладел тем же телом. Возвращает тело, id сессии и
// статус: "ok" — зарезервировано, "busy" — тело уже под управлением (конфликт), "none" — свободного
// тела в мире нет. Привязанное персонаж-тело предпочитается; иначе берём свободного взрослого НПС.
func (s *Server) reserveBody(uid uint64) (core.ID, uint64, string) {
	s.mu.Lock()
	defer s.mu.Unlock()

	var cid core.ID
	if bound := s.acc.CharOf(uid); bound != 0 {
		if e := s.world.Get(core.ID(bound)); e != nil && core.Ident(e) != nil {
			cid = core.ID(bound)
		}
	}
	if cid == 0 { // привязанного нет/умер — ищем свободного взрослого, не занятого другой сессией
		s.world.Each("ident", func(e *core.Entity) {
			if cid != 0 || s.ctl[e.ID] != 0 {
				return
			}
			if v := core.Vitals(e); v != nil && v.Age >= 16 {
				cid = e.ID
			}
		})
		if cid == 0 {
			return 0, 0, "none"
		}
		_ = s.acc.Bind(uid, uint64(cid))
	}
	if s.ctl[cid] != 0 {
		return cid, 0, "busy" // это тело уже кто-то ведёт — второй сессии не даём
	}
	s.nextSID++
	sid := s.nextSID
	s.ctl[cid] = sid // зарезервировали ДО upgrade
	return cid, sid, "ok"
}

// release снимает резервацию тела, но ТОЛЬКО если её держит именно эта сессия (sid) — чтобы
// запоздалое закрытие старой сессии не освободило чужое новое управление. Отпустил — цель снова за AI.
func (s *Server) release(cid core.ID, sid uint64) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.ctl[cid] != sid {
		return // телом уже владеет другая (новая) сессия — не трогаем
	}
	delete(s.ctl, cid)
	if e := s.world.Get(cid); e != nil {
		if m := core.Mind(e); m != nil {
			m.GoalUntil = 0 // отпустил тело → AI снова решает (лестница присутствия)
		}
	}
}

// --- WebSocket: управление телом ---

func (s *Server) handleWS(w http.ResponseWriter, r *http.Request) {
	uid, err := s.acc.Validate(r.URL.Query().Get("token"))
	if err != nil {
		http.Error(w, "нужен токен", http.StatusUnauthorized)
		return
	}
	cid, sid, status := s.reserveBody(uid)
	switch status {
	case "none":
		http.Error(w, "нет свободного тела в мире", http.StatusServiceUnavailable)
		return
	case "busy":
		http.Error(w, "это тело уже под управлением другой сессии", http.StatusConflict)
		return
	}
	// дать телу место в сцене (ходьба v1.1) — игрок появляется в центре деревенской площадки
	s.mu.Lock()
	if e := s.world.Get(cid); e != nil {
		sim.EnsureScenePos(s.world, e)
	}
	s.mu.Unlock()
	conn, err := wsUpgrade(w, r)
	if err != nil {
		s.release(cid, sid) // upgrade не удался — снимаем свою резервацию
		return
	}
	defer conn.Close()
	defer s.release(cid, sid) // отпускаем только СВОЮ резервацию

	// единственный ридер намерений; на ошибке/закрытии закрывает done
	done := make(chan struct{})
	go func() {
		defer close(done)
		for {
			msg, err := conn.ReadText()
			if err != nil {
				return
			}
			s.applyIntent(cid, msg)
		}
	}()

	// поток состояния: картина вокруг персонажа ~раз в секунду
	tick := time.NewTicker(1 * time.Second)
	defer tick.Stop()
	_ = conn.WriteText(s.stateJSON(cid)) // первый кадр сразу
	for {
		select {
		case <-done:
			return
		case <-tick.C:
			if err := conn.WriteText(s.stateJSON(cid)); err != nil {
				return
			}
		}
	}
}

// applyIntent применяет намерение игрока. Пока одно: выбрать цель персонажу (её держим,
// пока игрок в теле; AI не перебивает). Боевые/социальные намерения добавим переносом.
func (s *Server) applyIntent(cid core.ID, msg string) {
	var in struct {
		Op   string `json:"op"`
		Goal string `json:"goal"`
		// движение (v1.1): направление (единичный вектор задаёт клиент), бег, взгляд (град), шаг dt, seq
		MX   float64 `json:"mx"`
		MY   float64 `json:"my"`
		Run  bool    `json:"run"`
		Face float64 `json:"face"`
		Dt   float64 `json:"dt"`
		Seq  int64   `json:"seq"`
	}
	if json.Unmarshal([]byte(msg), &in) != nil {
		return
	}
	switch in.Op {
	case "goal":
		if in.Goal == "" {
			return
		}
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			if m := core.Mind(e); m != nil {
				m.Goal = in.Goal
				m.GoalUntil = s.runner.Clock.TotalHours + 100000
			}
		}
		s.mu.Unlock()
	case "move":
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			p := sim.EnsureScenePos(s.world, e)
			sim.ApplyMove(p, in.MX, in.MY, in.Run, in.Face, in.Dt, in.Seq)
		}
		s.mu.Unlock()
	}
}

// stateJSON собирает картину вокруг персонажа для клиента.
func (s *Server) stateJSON(cid core.ID) string {
	s.mu.Lock()
	defer s.mu.Unlock()
	e := s.world.Get(cid)
	if e == nil {
		return `{"v":1,"alive":false,"msg":"твоё тело покинуло мир"}`
	}
	id := core.Ident(e)
	v := core.Vitals(e)
	m := core.Mind(e)
	home := core.Home(e)
	pl := core.Place(e)
	out := map[string]any{
		"v":      1, // версия протокола (см. 3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md)
		"alive":  true,
		"day":    s.runner.Clock.Day(),
		"year":   s.runner.Clock.Year(),
		"season": s.runner.Clock.Season().String(),
		"me": map[string]any{
			"id":   eid(e.ID), // uint64 строкой — чтобы JS не терял точность; ключ Entity View
			"name": id.Name, "surname": id.Surname, "role": id.Role, "sex": id.Sex,
			"age": round1(v.Age), "health": round0(v.Health), "hunger": round0(v.Hunger),
			"energy": round0(v.Energy), "morale": round0(v.Morale),
			"goal": m.Goal, "last": m.LastAct, "home": home.Status,
			"coins": meCoins(e), "debt": meDebt(e),
			"wounds": meWounds(e), "bleeding": meBleeding(e),
			"stash": meStash(e),
			"pos":   mePos(e), // ходьба v1.1: каноническая позиция тела в сцене (метры) + взгляд/запал/seq
		},
	}
	if pl != nil && pl.Settlement != 0 {
		if se := s.world.Get(pl.Settlement); se != nil {
			st := core.Settle(se)
			neighbors := []map[string]any{}
			n := 0
			s.world.Each("place", func(o *core.Entity) {
				if n >= 8 || o.ID == e.ID {
					return
				}
				if p := core.Place(o); p != nil && p.Settlement == pl.Settlement {
					if oi := core.Ident(o); oi != nil {
						nx, ny, nf := sim.NeighborPos(o.ID) // устойчивое положение соседа (сервер пока не симулирует каждого)
						neighbors = append(neighbors, map[string]any{
							"id": eid(o.ID), "name": oi.Name, "role": oi.Role, "act": core.Mind(o).LastAct,
							"pos": map[string]any{"x": round1(nx), "y": round1(ny), "facing": round0(nf)}})
						n++
					}
				}
			})
			out["place"] = map[string]any{
				"name": st.Name, "tier": st.Tier,
				"food":        round0(econ.FoodOnHand(se)),
				"bread":       round0(st.Store[econ.GoodBread]),
				"bread_price": round1(econ.Price(se, econ.GoodBread)),
				"neighbors":   neighbors,
			}
		}
	}
	b, _ := json.Marshal(out)
	return string(b)
}

func (s *Server) handleIndex(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path != "/" {
		http.NotFound(w, r)
		return
	}
	http.ServeContent(w, r, "index.html", time.Now(), openIndex(s.webDir))
}

func writeJSON(w http.ResponseWriter, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	_ = json.NewEncoder(w).Encode(v)
}

// eid — идентификатор сущности для протокола: uint64 СТРОКОЙ (в JS число теряет точность за 2^53).
func eid(id core.ID) string { return strconv.FormatUint(uint64(id), 10) }

func round0(f float64) float64 { return float64(int(f + 0.5)) }
func round1(f float64) float64 { return float64(int(f*10+0.5)) / 10 }

func meCoins(e *core.Entity) float64 {
	if p := core.Purse(e); p != nil {
		return round1(p.Coins)
	}
	return 0
}
func meDebt(e *core.Entity) float64 {
	p := core.Purse(e)
	if p == nil {
		return 0
	}
	d := 0.0
	for _, v := range p.Debts {
		d += v
	}
	return round1(d)
}

// meWounds — раны игрока по частям тела (часть → тяжесть), пусто если цел.
func meWounds(e *core.Entity) map[string]float64 {
	b := battle.Body(e)
	if b == nil || len(b.Wounds) == 0 {
		return map[string]float64{}
	}
	out := map[string]float64{}
	for p, w := range b.Wounds {
		out[p] = round1(w)
	}
	return out
}

// meBleeding — кровит ли (0 — нет).
func meBleeding(e *core.Entity) float64 {
	if b := battle.Body(e); b != nil {
		return round1(b.Bleeding)
	}
	return 0
}

// mePos — каноническая позиция тела игрока в сцене (ходьба v1.1): координаты (м), взгляд (град),
// запал бега (0..1) и номер последнего обработанного ввода движения (для сверки на клиенте).
func mePos(e *core.Entity) map[string]any {
	p := sim.ScenePos(e)
	if p == nil {
		return map[string]any{"x": 0.0, "y": 0.0, "facing": 90.0, "stam": 1.0, "seq": int64(0)}
	}
	return map[string]any{
		"x": round1(p.X), "y": round1(p.Y), "facing": round0(p.Facing),
		"stam": round1(p.Stam), "seq": p.Seq,
	}
}

// meStash — личные закрома игрока (товар → сколько), пусто если ничего нет дома.
func meStash(e *core.Entity) map[string]float64 {
	s := econ.Stash(e)
	if s == nil || len(s.Goods) == 0 {
		return map[string]float64{}
	}
	out := map[string]float64{}
	for g, v := range s.Goods {
		if v >= 0.1 {
			out[g] = round1(v)
		}
	}
	return out
}
