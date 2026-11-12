// Пакет server — сетевой слой мира: HTTP для регистрации/входа (через службу accounts) и
// WebSocket, по которому игрок управляет ПЕРСОНАЖЕМ живого мира. Мир крутится сам в отдельной
// горутине; подключился игрок — «вселяется» в тело (controller), отключился — телом снова правит
// AI (простая лестница присутствия). Протокол engine-agnostic (JSON): так же подключится и Unity.
package server

import (
	"bytes"
	cryptorand "crypto/rand"
	"encoding/json"
	"log"
	"math/big"
	"mime"
	"net/http"
	"os"
	"path/filepath"
	"pogranichye/dice"
	"pogranichye/memory"
	"strconv"
	"strings"
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
	chatPeers map[core.ID]*chatPeer // активные получатели событий чата; очереди ограничены
	chatSeq uint64
	nextSID uint64             // счётчик сессий: у каждого ws-подключения свой id владения телом
	// Подменяемый источник перемешивания нужен для детерминированных серверных тестов.
	ochkoShuffle func([]sim.OchkoCard) error
	durakShuffle func([]sim.DurakCard) error

	// диагностика нагрузки (/api/stats): температура областей и длительность последнего часа мира
	hot, warm, cold int
	lastStepMs      float64

	stop     chan struct{} // закрывается в Close: останавливает тик-петлю
	stopOnce sync.Once
}

// quit — канал остановки тик-петли (создаётся при первом обращении).
func (s *Server) quit() chan struct{} {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.stop == nil {
		s.stop = make(chan struct{})
	}
	return s.stop
}

// Close останавливает тик-петлю мира (тикеры освобождаются, горутина завершается). Повторный вызов
// безопасен. HTTP-сервер и соединения закрывает владелец (http.Server / httptest).
func (s *Server) Close() {
	ch := s.quit()
	s.stopOnce.Do(func() { close(ch) })
}

// fastEach — шаг РЕАЛЬНОГО времени (бой, ходьба НПС, дорога, мана): чаще игрового часа, чтобы бой от
// первого лица был отзывчивым. Игровой час (нужды/экономика) идёт своим темпом tickEach.
const fastEach = 100 * time.Millisecond

// New создаёт сервер: генерирует мир и службу аккаунтов.
// SetTick задаёт темп мира (сколько реального времени на один игровой час). Для демо-профиля можно
// замедлить, чтобы время суток читалось; ≤0 — оставить как есть. Движение НПС/мана считаются от этого
// же dt, так что ходьба остаётся в реальном времени.
func (s *Server) SetTick(d time.Duration) {
	if d > 0 {
		s.tickEach = d
	}
}

func New(seed string, villages int, accPath string, secret []byte, webDir string) *Server {
	w, hub := sim.Generate(seed, villages)
	r := core.NewRunnerWith(hub, core.Clock{}, sim.Default()...)
	return &Server{
		world: w, runner: r,
		acc:      accounts.NewStore(accPath, secret),
		webDir:   webDir,
		tickEach: time.Minute, // игровой час за минуту: сутки за 24 реальных минуты
		ctl:      map[core.ID]uint64{},
		chatPeers: map[core.ID]*chatPeer{},
		ochkoShuffle: shuffleOchkoDeck,
		durakShuffle: shuffleDurakDeck,
	}
}

// NewStarter — сервер на ОДНОЙ стартовой деревне (для тонкого клиента/тестов): предсказуемый мир.
func NewStarter(seed, accPath string, secret []byte, webDir string) *Server {
	w, hub := sim.GenerateStarter(seed)
	r := core.NewRunnerWith(hub, core.Clock{}, sim.Default()...)
	return &Server{
		world: w, runner: r,
		acc:      accounts.NewStore(accPath, secret),
		webDir:   webDir,
		tickEach: time.Minute,
		ctl:      map[core.ID]uint64{},
		chatPeers: map[core.ID]*chatPeer{},
		ochkoShuffle: shuffleOchkoDeck,
		durakShuffle: shuffleDurakDeck,
	}
}

// SetFrontier задаёт настройки бесконечного мира: потолок числа поселений (0 — мир не растёт) и
// сколько деревень в каждом новом краю. Внешний мир растёт туда, куда приходят игроки.
func (s *Server) SetFrontier(max, villages int) {
	s.mu.Lock()
	cfg := sim.DefaultFrontier()
	cfg.Max, cfg.Enabled = max, max > 0
	if villages > 0 {
		cfg.Villages = villages
	}
	sim.ConfigureFrontier(s.world, cfg)
	s.mu.Unlock()
}

// --- сохранение мира на диск (v1.12) ---

// SaveTo пишет полный снимок мира (все сущности, часы, кости) в файл атомарно: сначала во временный
// файл, затем переименование — оборванная запись не портит прошлое сохранение.
func (s *Server) SaveTo(path string) error {
	s.mu.Lock()
	blob, err := memory.Save(s.world, s.runner.Clock, s.runner.Hub)
	s.mu.Unlock()
	if err != nil {
		return err
	}
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, blob, 0o600); err != nil {
		return err
	}
	return os.Rename(tmp, path)
}

// LoadFrom поднимает мир из файла сохранения, заменяя сгенерированный. Возвращает false, если файла
// нет (первый запуск). Тела, которыми управляли игроки на момент снимка, освобождаются сами: сессий
// после перезапуска нет.
func (s *Server) LoadFrom(path string) (bool, error) {
	blob, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	w, clock, hub, err := memory.Load(blob)
	if err != nil {
		return false, err
	}
	s.mu.Lock()
	s.world = w
	s.runner = core.NewRunnerWith(hub, clock, sim.Default()...)
	s.ctl = map[core.ID]uint64{}
	s.mu.Unlock()
	return true, nil
}

// Autosave сохраняет мир каждые every до остановки сервера (Close) и ещё раз при остановке.
func (s *Server) Autosave(path string, every time.Duration) {
	t := time.NewTicker(every)
	defer t.Stop()
	stop := s.quit()
	for {
		select {
		case <-stop:
			if err := s.SaveTo(path); err != nil {
				log.Printf("сохранение при остановке: %v", err)
			}
			return
		case <-t.C:
			if err := s.SaveTo(path); err != nil {
				log.Printf("автосохранение: %v", err)
			}
		}
	}
}

// Handler собирает маршруты (отдельно от Run — удобно для тестов).
func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/register", s.handleRegister)
	mux.HandleFunc("/api/login", s.handleLogin)
	mux.HandleFunc("/ws", s.handleWS)
	mux.HandleFunc("/api/layout", s.handleLayout)
	mux.HandleFunc("/api/stats", s.handleStats)
	mux.HandleFunc("/api/planet", func(w http.ResponseWriter, r *http.Request) { // служебная сводка планеты
		s.mu.Lock()
		out := sim.PlanetView(s.world)
		s.mu.Unlock()
		writeJSON(w, out)
	})
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
	f := time.NewTicker(fastEach)
	defer t.Stop()
	defer f.Stop()
	stop := s.quit()
	for {
		select {
		case <-stop:
			return
		case <-t.C:
			s.mu.Lock()
			t0 := time.Now()
			s.runner.Step(s.world) // игровой час: нужды/цели/экономика
			s.lastStepMs = float64(time.Since(t0).Microseconds()) / 1000
			s.mu.Unlock()
		case <-f.C:
			s.mu.Lock()
			s.fastStep(fastEach.Seconds())
			s.mu.Unlock()
		}
	}
}

// fastStep — один шаг реального времени (под замком): температура областей, ходьба НПС горячих
// поселений, враги и бой, путники, мана. Вынесен отдельно — его же зовут тесты.
func (s *Server) fastStep(dt float64) {
	ctl := func(id core.ID) bool { return s.ctl[id] != 0 }
	s.hot, s.warm, s.cold = sim.UpdateHeat(s.world, ctl)
	// тела под управлением игроков пропускаем (их ведёт сам игрок)
	sim.StepNPCMovement(s.world, dt, ctl)
	sim.StepCombat(s.world, dt, ctl, s.runner.Hub.Stream("combat"))
	sim.StepExits(s.world, ctl, func(id core.ID) *dice.Stream { return s.runner.Hub.Stream("player", eid(id)) })
	sim.StepJourneys(s.world, dt)
	sim.RegenMana(s.world, dt) // мана игроков восстанавливается в реальном времени
	sim.SoberUp(s.world, dt)
	sim.UpdateBurden(s.world, dt)
}

// handleStats — диагностика нагрузки: сколько сущностей, как распределена «температура» областей.
func (s *Server) handleStats(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	foes := 0
	s.world.Each("foe", func(*core.Entity) { foes++ })
	out := map[string]any{"entities": s.world.Count(), "players": len(s.ctl), "foes": foes,
		"hot": s.hot, "warm": s.warm, "cold": s.cold, "hour_ms": s.lastStepMs,
		"day": s.runner.Clock.Day(), "hour": s.runner.Clock.Hour(),
		"frontier": sim.FrontierStats(s.world)}
	s.mu.Unlock()
	writeJSON(w, out)
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
		sim.ClearCoDrink(s.world, e) // снять приглашение выпить с ушедшего и его партнёра
		sim.EndTalk(e)               // закрыть разговор
		sim.PartyLeave(s.world, e)   // ушёл из мира — вышел из отряда
		sim.SetGuard(s.world, e, false)
	}
}

// --- WebSocket: управление телом ---

func (s *Server) handleWS(w http.ResponseWriter, r *http.Request) {
	uid, err := s.acc.Validate(r.URL.Query().Get("token"))
	if err != nil { http.Error(w, "нужен токен", http.StatusUnauthorized); return }
	cid, sid, status := s.reserveBody(uid)
	switch status {
	case "none": http.Error(w, "нет свободного тела в мире", http.StatusServiceUnavailable); return
	case "busy": http.Error(w, "это тело уже под управлением другой сессии", http.StatusConflict); return
	}
	s.mu.Lock()
	if e := s.world.Get(cid); e != nil {
		sim.EnsureScenePos(s.world, e); sim.EnsurePlayer(s.world, e)
		sim.RollGift(s.world, e, s.runner.Hub.Stream("gift", eid(e.ID)))
		if pl := core.Place(e); pl != nil { sim.LearnVisited(s.world, e, pl.Settlement) }
	}
	s.mu.Unlock()
	conn, err := wsUpgrade(w, r)
	if err != nil { s.release(cid, sid); return }
	defer conn.Close()
	defer s.release(cid, sid)
	defer s.unregisterChat(cid)
	chatEvents := s.registerChat(cid)
	done := make(chan struct{})
	go func() {
		defer close(done)
		for { msg, err := conn.ReadText(); if err != nil { return }; s.applyIntent(cid, msg) }
	}()
	tick := time.NewTicker(time.Second)
	defer tick.Stop()
	var fastC <-chan time.Time
	if hz, _ := strconv.Atoi(r.URL.Query().Get("fast")); hz >= 2 {
		if hz > 15 { hz = 15 }
		ft := time.NewTicker(time.Second / time.Duration(hz)); defer ft.Stop(); fastC = ft.C
	}
	_ = conn.WriteText(s.stateJSON(cid))
	for {
		select {
		case <-done: return
		case event := <-chatEvents:
			if err := conn.WriteText(event); err != nil { return }
		case <-tick.C:
			if err := conn.WriteText(s.stateJSON(cid)); err != nil { return }
		case <-fastC:
			if err := conn.WriteText(s.fastJSON(cid)); err != nil { return }
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
		// действия v1.2: покупка/питьё; reqid для идемпотентности (повтор не списывает дважды)
		Item  string `json:"item"`
		ReqID string `json:"reqid"`
		// действия v1.3: рубка/рыбалка/магия — op:"act" с action
		Action string `json:"action"`
		Game   string `json:"game"`
		Index  int    `json:"index"`
		// социальное v1.4: разговор/знакомство/совместное питьё
		Target string `json:"target"` // id собеседника/партнёра (uint64 строкой)
		Line   string `json:"line"`   // выбранная реплика
		// навигация менеджмент-клиента v1.6: дискретный переход в локацию
		Location string `json:"location"`
		// расширение v1.7: инвентарь/рынок/путешествия
		Good   string   `json:"good"`
		Side   string   `json:"side"`
		Qty    int      `json:"qty"`    // ядро v1.10: плетения, бой, хитрости, просьбы, ремесло, дорога, отряд
		Nodes  []string `json:"nodes"`  // узор плетения
		Aim    string   `json:"aim"`    // часть тела: head|torso|arm|leg
		Style  string   `json:"style"`  // стиль удара
		Acc    float64  `json:"acc"`    // точность 0..1 (прицельный клиент)
		Power  float64  `json:"power"`  // натяжение/замах 0..1
		Aimed  bool     `json:"aimed"`  // клиент целился сам (первое лицо)
		On     bool     `json:"on"`     // блок вкл/выкл
		Stance string   `json:"stance"` // стойка
		Trick  string   `json:"trick"`  // хитрость
		Choice string   `json:"choice"` // решение на встрече в пути
		Quest  string   `json:"quest"`  // id просьбы
		ChatChannel string `json:"channel"`
		ChatText string `json:"text"`
		VoiceAction string `json:"action"`
		VoiceSDP string `json:"sdp"`
		VoiceCandidate string `json:"candidate"`
		VoiceSDPMid string `json:"sdp_mid"`
		VoiceSDPMLine int `json:"sdp_mline_index"`

	}
	if json.Unmarshal([]byte(msg), &in) != nil {
		return
	}
	switch in.Op {
	case "chat":
		s.sendChat(cid, in.ChatChannel, in.ChatText, in.Target)
	case "voice":
		s.signalVoice(cid, in.VoiceAction, in.Target, in.VoiceSDP, in.VoiceCandidate, in.VoiceSDPMid, in.VoiceSDPMLine)
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
			if !sim.OnRoad(e) {
				p := sim.EnsureScenePos(s.world, e)
				sim.ApplyMove(p, in.MX, in.MY, in.Run, in.Face, in.Dt, in.Seq)
			}
		}
		s.mu.Unlock()
	case "buy":
		if in.Item == "beer" {
			s.mu.Lock()
			if e := s.world.Get(cid); e != nil {
				rok, rwhy := sim.BuyBeer(s.world, e, in.ReqID) // атомарно/идемпотентно; исход в кадре (me.mug)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			}
			s.mu.Unlock()
		}
	case "consume":
		if in.Item == "beer" {
			s.mu.Lock()
			if e := s.world.Get(cid); e != nil {
				rok, rwhy := sim.DrinkBeer(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			}
			s.mu.Unlock()
		}
	case "act": // рубка/рыбалка/магия (v1.3); исход виден в кадре (закрома, мана, убыль зверя)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			switch in.Action {
			case "chop":
				rok, rwhy := sim.ChopWood(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "fish":
				rok, rwhy := sim.FishCatch(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "cast":
				rok, rwhy := sim.CastSpark(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "build":
				rok, rwhy := sim.Build(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "hunt":
				rok, rwhy := sim.Hunt(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "fight":
				rok, rwhy := sim.Fight(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "meditate":
				rok, rwhy := sim.Meditate(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "mine":
				rok, rwhy := sim.Mine(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "gather":
				rok, rwhy := sim.Gather(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "farmwork":
				rok, rwhy := sim.Farmwork(s.world, e, s.runner.Clock.Season(), in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "forge":
				rok, rwhy := sim.Forge(s.world, e, in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			case "apiary":
				rok, rwhy := sim.Apiary(s.world, e, s.runner.Clock.Season(), in.ReqID)
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			}
		}
		s.mu.Unlock()
	case "card_game": // серверные карточные игры против корчмаря
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			var ok bool
			var why string
			game := in.Game
			if game == "" { game = "ochko" } // совместимость с первым контрактом #340
			switch game {
			case "ochko":
				switch in.Action {
				case "start":
					shuffle := s.ochkoShuffle
					if shuffle == nil { shuffle = shuffleOchkoDeck }
					ok, why = sim.StartOchko(s.world, e, in.ReqID, shuffle)
				case "hit":
					ok, why = sim.HitOchko(s.world, e, in.ReqID)
				case "stand":
					ok, why = sim.StandOchko(s.world, e, in.ReqID)
				default:
					why = "неизвестный ход в очко"
				}
			case "durak":
				if in.Action == "start" {
					shuffle := s.durakShuffle
					if shuffle == nil { shuffle = shuffleDurakDeck }
					ok, why = sim.StartDurak(s.world, e, in.ReqID, shuffle)
				} else {
					ok, why = sim.ActDurak(s.world, e, in.Action, in.Index, in.ReqID)
				}
			default:
				why = "неизвестная карточная игра"
			}
			sim.SetResult(s.world, e, in.ReqID, ok, why)
		}
		s.mu.Unlock()
	case "take": // подобрать предмет (v1.7)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			rok, rwhy := sim.Take(s.world, e, in.Item, in.ReqID)
			sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
		}
		s.mu.Unlock()
	case "equip":
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			ok, why := sim.Equip(s.world, e, in.Item, in.ReqID)
			sim.SetResult(s.world, e, in.ReqID, ok, why)
		}
		s.mu.Unlock()
	case "wear":
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			ok, why := sim.Wear(s.world, e, in.Item, in.ReqID)
			sim.SetResult(s.world, e, in.ReqID, ok, why)
		}
		s.mu.Unlock()
	case "unwear": // снять надетое (v1.17)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			ok, why := sim.Unwear(s.world, e, in.Item, in.ReqID)
			sim.SetResult(s.world, e, in.ReqID, ok, why)
		}
		s.mu.Unlock()
	case "trade": // рынок: купить/продать (v1.7)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			rok, rwhy := sim.Trade(s.world, e, in.Good, in.Side, in.Qty, in.ReqID)
			sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
		}
		s.mu.Unlock()
	case "travel": // перейти в другое поселение (v1.7)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			sim.Travel(s.world, e, parseID(in.Target))
		}
		s.mu.Unlock()
	case "goto": // дискретная навигация менеджмент-клиента: встать в локацию (v1.6)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			if in.Target != "" { // подойти к зверю (тонкий клиент): qty — дистанция в метрах
				rok, rwhy := sim.Approach(s.world, e, parseID(in.Target), float64(in.Qty))
				sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
			} else if !sim.OnRoad(e) {
				sim.GoTo(s.world, e, in.Location)
			}
		}
		s.mu.Unlock()
	case "talk": // заговорить с жителем рядом (v1.4)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			// talk — идемпотентная установка состояния (без кулдауна и без платы): повтор просто
			// перенацеливает разговор. Если клиент прислал reqid, исход коррелируется в me.result (#40).
			rok, rwhy := sim.StartTalk(s.world, e, parseID(in.Target))
			sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
		}
		s.mu.Unlock()
	case "say": // выбрать реплику в разговоре (знакомство/угощение)
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			rok, rwhy := sim.Say(s.world, e, in.Line, in.ReqID)
			sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
		}
		s.mu.Unlock()
	case "endtalk":
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			sim.EndTalk(e)
		}
		s.mu.Unlock()
	case "invite": // позвать другого ИГРОКА выпить вместе
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			rok, rwhy := sim.InviteCoDrink(s.world, e, parseID(in.Target), func(id core.ID) bool { return s.ctl[id] != 0 })
			sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
		}
		s.mu.Unlock()
	case "accept": // принять приглашение выпить
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			rok, rwhy := sim.AcceptCoDrink(s.world, e)
			sim.SetResult(s.world, e, in.ReqID, rok, rwhy)
		}
		s.mu.Unlock()
	case "declinedrink": // отклонить/отменить приглашение
		s.mu.Lock()
		if e := s.world.Get(cid); e != nil {
			c := sim.CoDrink(e)
			ok := c != nil && c.State != ""
			sim.ClearCoDrink(s.world, e)
			reason := ""
			if !ok {
				reason = "нет активного приглашения"
			}
			sim.SetResult(s.world, e, in.ReqID, ok, reason)
		}
		s.mu.Unlock()
	default:
		s.applyCore(cid, in.Op, coreIntent{Nodes: in.Nodes, Target: in.Target, ReqID: in.ReqID, Aim: in.Aim,
			Style: in.Style, Acc: in.Acc, Power: in.Power, Aimed: in.Aimed, On: in.On, Stance: in.Stance,
			Trick: in.Trick, Choice: in.Choice, Quest: in.Quest, Action: in.Action, Item: in.Item,
			Good: in.Good, Qty: in.Qty})
	}
}

// coreIntent — поля намерений ядра v1.10 (плетения/бой/просьбы/ремесло/дорога/отряд).
type coreIntent struct {
	Nodes                                                   []string
	Target, ReqID, Aim, Style, Stance, Trick, Choice, Quest string
	Action, Item, Good                                      string
	Qty                                                     int
	Acc, Power                                              float64
	Aimed, On                                               bool
}

// applyCore — намерения ядра v1.10. Всё серверно-авторитетно; исход — в me.result и ленте me.feed.
func (s *Server) applyCore(cid core.ID, op string, in coreIntent) {
	s.mu.Lock()
	defer s.mu.Unlock()
	e := s.world.Get(cid)
	if e == nil {
		return
	}
	w := s.world
	rng := s.runner.Hub.Stream("player", eid(cid))
	ctl := func(id core.ID) bool { return s.ctl[id] != 0 }
	day := s.runner.Clock.Day()
	ok, why, has := false, "", true
	switch op {
	case "weave": // сплести узор из узлов (магия плетениями)
		ok, why = sim.WeaveCast(w, e, in.Nodes, parseID(in.Target), in.ReqID, rng)
	case "attack": // удар/выстрел: ввод (цель, часть тела, точность) → серверное разрешение
		ok, why = sim.Strike(w, e, sim.StrikeInput{Target: parseID(in.Target), Aim: in.Aim, Style: in.Style,
			Acc: in.Acc, Power: in.Power, Aimed: in.Aimed, ReqID: in.ReqID}, rng)
	case "guard":
		sim.SetGuard(w, e, in.On)
		has = false
	case "dodge":
		ok, why = sim.DodgeRoll(w, e)
	case "stance":
		ok, why = sim.SetStance(w, e, in.Stance)
	case "trick":
		ok, why = sim.Trick(w, e, in.Trick, in.ReqID)
	case "quest":
		switch in.Action {
		case "accept":
			ok, why = sim.QuestAccept(w, e, in.Quest, day)
		case "turnin":
			ok, why = sim.QuestTurnIn(w, e, in.Quest)
		case "drop":
			ok, why = sim.QuestDrop(w, e, in.Quest)
		}
	case "horse":
		ok, why = sim.Horse(w, e, in.Action, in.Good, in.Qty, in.ReqID)
	case "craft":
		ok, why = sim.Craft(w, e, in.Item, in.ReqID)
	case "use":
		ok, why = sim.Use(w, e, in.Item, in.ReqID)
	case "journey":
		if in.Action == "choose" {
			ok, why = sim.JourneyChoose(w, e, in.Choice, rng)
		} else {
			ok, why = sim.JourneyStart(w, e, parseID(in.Target), in.ReqID, rng)
		}
	case "party":
		switch in.Action {
		case "invite":
			ok, why = sim.PartyInvite(w, e, parseID(in.Target), ctl)
		case "accept":
			ok, why = sim.PartyAccept(w, e)
		case "leave":
			sim.PartyLeave(w, e)
			ok = true
		}
	default:
		has = false
	}
	if has {
		sim.SetResult(w, e, in.ReqID, ok, why)
	}
}

// shuffleOchkoDeck использует crypto/rand: исход карточной партии не выводится из seed мира
// и не может быть предсказан клиентом. При ошибке StartOchko не списывает ставку.
func shuffleOchkoDeck(cards []sim.OchkoCard) error {
	for i := len(cards) - 1; i > 0; i-- {
		n, err := cryptorand.Int(cryptorand.Reader, big.NewInt(int64(i+1)))
		if err != nil {
			return err
		}
		j := int(n.Int64())
		cards[i], cards[j] = cards[j], cards[i]
	}
	return nil
}

// shuffleDurakDeck — криптографическое перемешивание 36-карточной колоды.
func shuffleDurakDeck(cards []sim.DurakCard) error {
	for i := len(cards) - 1; i > 0; i-- {
		n, err := cryptorand.Int(cryptorand.Reader, big.NewInt(int64(i+1)))
		if err != nil { return err }
		j := int(n.Int64())
		cards[i], cards[j] = cards[j], cards[i]
	}
	return nil
}

// parseID — id собеседника из строки кадра (uint64) в core.ID; 0 при ошибке.
func parseID(sid string) core.ID {
	n, err := strconv.ParseUint(sid, 10, 64)
	if err != nil {
		return 0
	}
	return core.ID(n)
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
			"name": id.Name, "surname": id.Surname, "nick": id.Nick, "role": id.Role, "sex": id.Sex,
			"trait": id.Trait, "origin": id.Origin, // характеристики личности (v1.6)
			"age": round1(v.Age), "health": round0(v.Health), "hunger": round0(v.Hunger),
			"energy": round0(v.Energy), "morale": round0(v.Morale),
			"skills": meSkills(e), // умения и их уровни — прокачка от труда (v1.6)
			"goal":   m.Goal, "last": m.LastAct, "home": home.Status,
			"act_code": m.ActCode, // стабильный код занятия для анимаций клиента (#23)
			"mug":      meMug(e),  // держит ли кружку (интерактивность v1.2)
			"mana":     meMana(e), // мана для магического разряда (v1.3)
			"coins":    meCoins(e), "debt": meDebt(e),
			"wounds": meWounds(e), "bleeding": meBleeding(e),
			"stash":        meStash(e),
			"pos":          mePos(e),                                      // ходьба v1.1: каноническая позиция тела в сцене (метры) + взгляд/запал/seq
			"talk":         meTalk(s.world, e),                            // текущий разговор: собеседник, реплика, варианты ответа (v1.4)
			"invite_from":  meInvite(s.world, e),                          // кто зовёт выпить вместе (co-drink, v1.4)
			"social_event": meSocial(e),                                   // подтверждённый тост/угощение (монотонный seq) — v1.5
			"known_places": s.meKnownPlaces(e),                            // что игрок знает о поселениях: visited/known (#26, v1.5)
			"desire":       sim.Desire(e),                                 // текущее главное желание (v1.7)
			"inv":          meInv(e),                                      // инвентарь: предметы, рука, одежда (v1.7)
			"world":        sim.Settlements(s.world),                      // карта поселений для путешествий (v1.7)
			"result":       meResult(e),                                   // ack последнего действия (reqid/ok/reason) — #40
			"task":         sim.OpeningTask(s.world, e),                   // вводная задача + выданное поручение (#80)
			"journal":      sim.Journal(s.world, e, s.runner.Clock.Day()), // личная летопись вех (#54)
			// ядро v1.10
			"weave":   sim.WeaveView(s.world, e),                                                            // плетения: узлы, ёмкость ума, знакомые схемы
			"combat":  sim.CombatView(s.world, e),                                                           // стойка/блок/щит/стрелы/убитые
			"feed":    sim.Feed(e),                                                                          // лента подтверждённых событий (монотонный seq)
			"quests":  sim.QuestsView(s.world, e, s.runner.Clock.Day()),                                     // просьбы из нужд мира
			"recipes": sim.RecipesView(e),                                                                   // ремесло: рецепты и хватает ли сырья
			"journey": sim.JourneyView(s.world, e, s.runner.Clock.Hour(), s.runner.Clock.Season().String()), // путь (null — не в дороге)
			"horse":   sim.HorseView(s.world, e),                                                            // лошадь и седельная сумка (v1.13; null — нет)
			"bag":     sim.BagView(e),                                                                       // сума: загрузка/вместимость от силы (v1.13)
			"stats":   sim.StatsView(e),                                                                     // характеристики (v1.12)
			"drunk":   round0(sim.Drunk(e)),                                                                 // хмель 0..100 (v1.12)
			"region":  sim.RegionView(s.world, e),                                                           // карта края: известное и тракты (v1.12)
			"party":   sim.PartyView(s.world, e),                                                            // отряд друзей
			"card_game": sim.CardGameView(e), // личная партия; скрытая рука корчмаря не попадает в кадр
		},
	}
	out["hour"] = s.runner.Clock.Hour()                    // час суток 0..23 — для света/ночи на клиенте
	if pl != nil && pl.Settlement != 0 && !sim.OnRoad(e) { // в пути сцены поселения нет — см. me.journey
		if se := s.world.Get(pl.Settlement); se != nil {
			st := core.Settle(se)
			neighbors := []map[string]any{}
			for _, o := range s.nearest(e, pl.Settlement, neighborCap) {
				{
					if oi := core.Ident(o); oi != nil {
						nx, ny, nf := neighborPos(o) // РЕАЛЬНАЯ позиция НПС в сцене (ходит сервером)
						om := core.Mind(o)
						// знакомство (v1.4): имя видно только знакомым; иначе аноним + обезличенное desc
						known := sim.Knows(e, o.ID)
						shownName := ""
						if known {
							shownName = oi.Name
						}
						neighbors = append(neighbors, map[string]any{
							"id": eid(o.ID), "name": shownName, "known": known,
							"desc": sim.DescFor(oi.Role), "role": oi.Role, "trait": oi.Trait, "act": om.LastAct, "want": sim.Desire(o),
							"gear":     sim.PublicGear(o),     // видимая экипировка: рука + надетое (#48), без личной сумы
							"act_code": om.ActCode,            // стабильный код занятия соседа (для анимаций)
							"player":   s.ctl[o.ID] != 0,      // presence: живой игрок, а не НПС (v1.5) — клиент рисует иначе и зовёт выпить только его
							"casting":  sim.CastingSeen(e, o), // плетёт ли (опытный маг читает имя узора) — v1.10
							"pos":      map[string]any{"x": round1(nx), "y": round1(ny), "facing": round0(nf)}})
					}
				}
			}
			out["place"] = map[string]any{
				"id":   eid(se.ID), // id поселения (сверка с me.world/known_places) — v1.10
				"name": st.Name, "tier": st.Tier,
				"food":        round0(econ.FoodOnHand(se)),
				"bread":       round0(st.Store[econ.GoodBread]),
				"bread_price": round1(econ.Price(se, econ.GoodBread)),
				"tavern":      st.Tavern,                      // есть ли корчма (где можно выпить)
				"beer":        round0(st.Store[sim.BeerGood]), // запас пива в корчме (v1.2)
				"beer_price":  sim.BeerPrice,                  // цена кружки
				"neighbors":   neighbors,
				"foes":        sim.FoesView(s.world, pl.Settlement),     // звери/лихие люди сцены (v1.10)
				"exits":       sim.ExitsView(s.world, e, pl.Settlement), // выезды: тракты к соседям и перевал (v1.15)
				"site":        sim.SiteView(s.world, se),                // место на планете и характер села (v1.14)
				"stable":      sim.StableView(se),                       // конюшня: лошади на продажу и цена (v1.13)
				"market":      sim.MarketView(se, e),                    // прилавок: запас и цены (v1.10)
				"life":        s.placeLife(se),                           // агрегаты жизни общины (v1.18)
			}
		}
	}
	b, _ := json.Marshal(out)
	return string(b)
}

// neighborCap — сколько ближайших жителей попадает в кадр (раньше 8 первых по обходу; теперь —
// БЛИЖАЙШИЕ к игроку, чтобы вид от первого лица не терял тех, кто стоит рядом).
const neighborCap = 24

// nearest — до n ближайших к игроку жителей его поселения (не в пути), по расстоянию в сцене.
func (s *Server) nearest(e *core.Entity, settlement core.ID, n int) []*core.Entity {
	if n <= 0 || e == nil {
		return nil
	}
	px, py, _ := neighborPos(e)
	type row struct {
		e *core.Entity
		d float64
	}
	// Keep only the nearest n while scanning. The old implementation retained every
	// resident and sorted the entire settlement for every player frame (up to 10 Hz).
	// This bounded insertion list uses O(n) memory and O(population*n) comparisons,
	// with n normally 24, instead of O(population) memory + O(population log population).
	rows := make([]row, 0, n)
	s.world.Each("place", func(o *core.Entity) {
		if o.ID == e.ID || sim.OnRoad(o) {
			return
		}
		p := core.Place(o)
		if p == nil || p.Settlement != settlement || core.Ident(o) == nil {
			return
		}
		x, y, _ := neighborPos(o)
		candidate := row{o, (x-px)*(x-px) + (y-py)*(y-py)}
		at := len(rows)
		for i := range rows {
			if candidate.d < rows[i].d || (candidate.d == rows[i].d && candidate.e.ID < rows[i].e.ID) {
				at = i
				break
			}
		}
		if at >= n {
			return
		}
		if len(rows) < n {
			rows = append(rows, row{})
		}
		copy(rows[at+1:], rows[at:])
		rows[at] = candidate
	})
	out := make([]*core.Entity, len(rows))
	for i := range rows {
		out[i] = rows[i].e
	}
	return out
}

// fastJSON — БЫСТРЫЙ кадр (по запросу ?fast=N): только то, что меняется в бою каждую долю секунды —
// тело игрока, боевое состояние, лента, позиции соседей и врагов. Помечен "fast":true; полные кадры
// идут как прежде раз в секунду.
func (s *Server) fastJSON(cid core.ID) string {
	s.mu.Lock()
	defer s.mu.Unlock()
	e := s.world.Get(cid)
	if e == nil {
		return `{"v":1,"fast":true,"alive":false}`
	}
	v := core.Vitals(e)
	me := map[string]any{"pos": mePos(e), "mana": meMana(e), "combat": sim.CombatView(s.world, e),
		"feed": sim.Feed(e), "result": meResult(e), "wounds": meWounds(e)}
	if v != nil {
		me["health"], me["energy"] = round0(v.Health), round0(v.Energy)
	}
	out := map[string]any{"v": 1, "fast": true, "alive": true, "me": me}
	if pl := core.Place(e); pl != nil && pl.Settlement != 0 && !sim.OnRoad(e) {
		ns := []map[string]any{}
		for _, o := range s.nearest(e, pl.Settlement, neighborCap) {
			x, y, f := neighborPos(o)
			ns = append(ns, map[string]any{"id": eid(o.ID), "pos": map[string]any{"x": round1(x), "y": round1(y), "facing": round0(f)}})
		}
		out["place"] = map[string]any{"neighbors": ns, "foes": sim.FoesView(s.world, pl.Settlement)}
	}
	b, _ := json.Marshal(out)
	return string(b)
}

// meTalk — текущий разговор игрока для кадра: с кем, что собеседник сказал, доступные реплики.
// nil, если игрок ни с кем не говорит. Имя собеседника — только если знаком, иначе обезличенное desc.
func meTalk(w *core.World, e *core.Entity) any {
	t := sim.Talk(e)
	if t == nil || t.Stage == "" {
		return nil
	}
	tgt := w.Get(t.With)
	if tgt == nil {
		return nil
	}
	known := sim.Knows(e, t.With)
	who := ""
	if oi := core.Ident(tgt); oi != nil {
		if known {
			who = oi.Name
		} else {
			who = sim.DescFor(oi.Role)
		}
	}
	opts := []map[string]any{}
	for _, o := range sim.TalkOptionsFor(e, tgt) {
		opts = append(opts, map[string]any{"id": o.ID, "text": o.Text})
	}
	return map[string]any{"with": eid(t.With), "who": who, "known": known, "line": t.Line, "options": opts}
}

// meInvite — кто зовёт игрока выпить вместе (co-drink); nil если нет приглашения. Имя/описание зовущего.
func meInvite(w *core.World, e *core.Entity) any {
	c := sim.CoDrink(e)
	if c == nil || c.State != "in" {
		return nil
	}
	a := w.Get(c.Partner)
	if a == nil {
		return nil
	}
	who := "игрок"
	if oi := core.Ident(a); oi != nil {
		if sim.Knows(e, a.ID) {
			who = oi.Name
		} else {
			who = sim.DescFor(oi.Role)
		}
	}
	return map[string]any{"from": eid(c.Partner), "who": who}
}

// meKnownPlaces — что игрок знает о поселениях (#26): список visited/known с именами. Пусто, если ничего
// не знает. Неизвестные поселения в кадр НЕ попадают (не раскрываем неоткрытый мир).
func (s *Server) meKnownPlaces(e *core.Entity) []map[string]any {
	k := sim.Knowledge(e)
	out := []map[string]any{}
	if k == nil {
		return out
	}
	for sid, f := range k.Places {
		name := ""
		if st := core.Settle(s.world.Get(sid)); st != nil {
			name = st.Name
		}
		out = append(out, map[string]any{"id": eid(sid), "name": name, "visited": f.Visited, "known": f.Known})
	}
	return out
}

// meSocial — последнее подтверждённое соц-событие (тост co-drink / угощение) с монотонным seq.
// nil, если событий не было. Клиент проигрывает жест ОДИН раз на новый seq, не по клику/morale.
func meSocial(e *core.Entity) any {
	ev := sim.SocialEvent(e)
	if ev == nil || ev.Seq == 0 {
		return nil
	}
	return map[string]any{"seq": ev.Seq, "type": ev.Type, "with": eid(ev.With)}
}

// meMug — держит ли игрок кружку (для показа кружки/руки на клиенте только по подтверждению).
func meMug(e *core.Entity) bool {
	if m := sim.Mug(e); m != nil {
		return m.Has
	}
	return false
}

// meResult — ack последнего действия игрока (reqid/ok/reason) для клиента; nil, если действий не было.
func meResult(e *core.Entity) any {
	p := sim.Player(e)
	if p == nil || p.RReqid == "" {
		return nil
	}
	return map[string]any{"reqid": p.RReqid, "ok": p.ROk, "reason": p.RReason}
}

// meInv — инвентарь игрока: предметы, что в руке, надетая одежда (v1.7).
func meInv(e *core.Entity) map[string]any {
	i := sim.Inv(e)
	if i == nil {
		return map[string]any{"items": map[string]int{}, "hand": "", "worn": map[string]string{}}
	}
	return map[string]any{"items": i.Items, "hand": i.Hand, "worn": i.Worn}
}

// meSkills — умения игрока и их уровни (прокачка от труда). Пусто, если навыков нет.
func meSkills(e *core.Entity) map[string]any {
	out := map[string]any{}
	if sk := core.Skills(e); sk != nil {
		for name, lvl := range sk.Skills {
			out[name] = round1(lvl)
		}
	}
	return out
}

// meMana — мана игрока для магического разряда (0..100).
func meMana(e *core.Entity) float64 {
	if p := sim.Player(e); p != nil {
		return round0(p.Mana)
	}
	return 0
}

// handleLayout отдаёт ВЕРСИОНИРОВАННУЮ геометрию мира (границы, деревня, здания, река, POI) — её и
// сервер, и клиент берут за единую истину (сервер — для коллизий, Unity/web — для отрисовки).
func (s *Server) handleLayout(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, sim.LayoutJSON())
}

// neighborPos — позиция соседа для кадра: реальная ScenePos (её ведёт StepNPCMovement). Если житель
// ещё не получил сцену (не попал под шаг движения), отдаём его детерминированное стартовое место —
// то же, куда его поставит система ходьбы, чтобы не было «прыжка из центра».
func neighborPos(e *core.Entity) (x, y, facing float64) {
	if p := sim.ScenePos(e); p != nil {
		return p.X, p.Y, p.Facing
	}
	sx, sy := sim.SpawnSpot(uint64(e.ID))
	return sx, sy, 90
}

func (s *Server) handleIndex(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path == "/" {
		http.ServeContent(w, r, "index.html", time.Now(), openIndex(s.webDir))
		return
	}
	// статика тонкого клиента (карта village-scene.svg, атлас и пр.) из webDir.
	// без webDir отдавать нечего — только корневая страница-заглушка.
	if s.webDir == "" {
		http.NotFound(w, r)
		return
	}
	// защита от выхода за каталог: чистим путь и держим его внутри webDir.
	clean := filepath.Clean("/" + strings.TrimPrefix(r.URL.Path, "/"))
	full := filepath.Join(s.webDir, clean)
	root, _ := filepath.Abs(s.webDir)
	abs, _ := filepath.Abs(full)
	if root == "" || !strings.HasPrefix(abs, root+string(os.PathSeparator)) {
		http.NotFound(w, r)
		return
	}
	data, err := os.ReadFile(abs)
	if err != nil {
		http.NotFound(w, r)
		return
	}
	// тип по расширению; Go не всегда знает .svg — проставим явно.
	if ct := mime.TypeByExtension(filepath.Ext(abs)); ct != "" {
		w.Header().Set("Content-Type", ct)
	} else if strings.EqualFold(filepath.Ext(abs), ".svg") {
		w.Header().Set("Content-Type", "image/svg+xml")
	}
	http.ServeContent(w, r, filepath.Base(abs), time.Now(), bytes.NewReader(data))
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
