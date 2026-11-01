// Пакет server — сетевой слой мира: HTTP для регистрации/входа (через службу accounts) и
// WebSocket, по которому игрок управляет ПЕРСОНАЖЕМ живого мира. Мир крутится сам в отдельной
// горутине; подключился игрок — «вселяется» в тело (controller), отключился — телом снова правит
// AI (простая лестница присутствия). Протокол engine-agnostic (JSON): так же подключится и Unity.
package server

import (
	"encoding/json"
	"net/http"
	"sync"
	"time"

	"pogranichye/accounts"
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

	ctl map[core.ID]bool // какие персонажи сейчас под игроком (их цель не трогает AI)
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
		ctl:      map[core.ID]bool{},
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

// embody находит игроку тело: уже привязанное (если живо) или свободного взрослого NPC.
func (s *Server) embody(uid uint64) core.ID {
	s.mu.Lock()
	defer s.mu.Unlock()
	if cid := s.acc.CharOf(uid); cid != 0 {
		if e := s.world.Get(core.ID(cid)); e != nil && core.Ident(e) != nil {
			return core.ID(cid)
		}
	}
	// выбрать свободного взрослого (не под игроком)
	var pick core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if pick != 0 || s.ctl[e.ID] {
			return
		}
		if v := core.Vitals(e); v != nil && v.Age >= 16 {
			pick = e.ID
		}
	})
	if pick != 0 {
		_ = s.acc.Bind(uid, uint64(pick))
	}
	return pick
}

// --- WebSocket: управление телом ---

func (s *Server) handleWS(w http.ResponseWriter, r *http.Request) {
	uid, err := s.acc.Validate(r.URL.Query().Get("token"))
	if err != nil {
		http.Error(w, "нужен токен", http.StatusUnauthorized)
		return
	}
	cid := s.embody(uid)
	if cid == 0 {
		http.Error(w, "нет свободного тела в мире", http.StatusServiceUnavailable)
		return
	}
	conn, err := wsUpgrade(w, r)
	if err != nil {
		return
	}
	defer conn.Close()

	s.mu.Lock()
	s.ctl[cid] = true
	s.mu.Unlock()
	defer func() {
		s.mu.Lock()
		delete(s.ctl, cid)
		if e := s.world.Get(cid); e != nil {
			if m := core.Mind(e); m != nil {
				m.GoalUntil = 0 // отпустил тело → AI снова решает (лестница присутствия)
			}
		}
		s.mu.Unlock()
	}()

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
	}
	if json.Unmarshal([]byte(msg), &in) != nil {
		return
	}
	if in.Op == "goal" && in.Goal != "" {
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			if m := core.Mind(e); m != nil {
				m.Goal = in.Goal
				m.GoalUntil = s.runner.Clock.TotalHours + 100000
			}
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
		return `{"alive":false,"msg":"твоё тело покинуло мир"}`
	}
	id := core.Ident(e)
	v := core.Vitals(e)
	m := core.Mind(e)
	home := core.Home(e)
	pl := core.Place(e)
	out := map[string]any{
		"alive":  true,
		"day":    s.runner.Clock.Day(),
		"year":   s.runner.Clock.Year(),
		"season": s.runner.Clock.Season().String(),
		"me": map[string]any{
			"name": id.Name, "surname": id.Surname, "role": id.Role, "sex": id.Sex,
			"age": round1(v.Age), "health": round0(v.Health), "hunger": round0(v.Hunger),
			"energy": round0(v.Energy), "morale": round0(v.Morale),
			"goal": m.Goal, "last": m.LastAct, "home": home.Status,
			"coins": meCoins(e), "debt": meDebt(e),
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
						neighbors = append(neighbors, map[string]any{
							"name": oi.Name, "role": oi.Role, "act": core.Mind(o).LastAct})
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
