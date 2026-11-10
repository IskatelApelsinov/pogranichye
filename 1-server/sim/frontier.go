package sim

import (
	"math"

	"pogranichye/core"
	"pogranichye/dice"
)

// Бесконечный мир (v1.11). Внешний мир не кончается на том, что сгенерировано при старте: когда игрок
// ВПЕРВЫЕ приходит в край внешнего мира, за этим краем «открывается» следующий — новые деревни (и
// иногда город) дальше от центра. Так карта растёт туда, куда идут игроки, а не заранее и не вся
// сразу. Новые поселения рождаются холодными (heat.go), поэтому рост мира почти ничего не стоит,
// пока туда не дошли. Рост ограничен настройкой Max — предохранитель для памяти сервера.

func init() {
	core.Register("frontier", func() core.Component { return &FrontierC{} })
	core.Register("frontiercfg", func() core.Component { return &FrontierCfgC{} })
}

// FrontierC — метка края: открыт ли уже следующий край за ним и на каком он кольце от стартовых земель.
type FrontierC struct {
	Opened bool `json:"opened"`
	Ring   int  `json:"ring"`
}

func (c *FrontierC) Kind() string { return "frontier" }

// FrontierCfgC — настройки бесконечного мира (один на мир).
type FrontierCfgC struct {
	Enabled   bool `json:"enabled"`
	Max       int  `json:"max"`        // потолок числа поселений в мире (0 — без роста)
	Villages  int  `json:"villages"`   // деревень в каждом новом краю
	CityEvery int  `json:"city_every"` // город в каждом N-м новом краю (0 — без городов)
	Folk      int  `json:"folk"`       // жителей в новой деревне (город — втрое)
	Made      int  `json:"made"`       // сколько краёв уже открыто (счётчик)
}

func (c *FrontierCfgC) Kind() string { return "frontiercfg" }

// DefaultFrontier — настройки по умолчанию: мир растёт до 300 поселений, по 4 деревни на край.
func DefaultFrontier() FrontierCfgC {
	return FrontierCfgC{Enabled: true, Max: 300, Villages: 4, CityEvery: 3, Folk: 7}
}

// ConfigureFrontier задаёт настройки бесконечного мира (флаги сервера). Повторный вызов обновляет их.
func ConfigureFrontier(w *core.World, cfg FrontierCfgC) *FrontierCfgC {
	if cfg.Villages < 1 {
		cfg.Villages = 1
	}
	if cfg.Villages > 12 {
		cfg.Villages = 12
	}
	if cfg.Folk < 3 {
		cfg.Folk = 3
	}
	if cfg.Folk > 30 {
		cfg.Folk = 30
	}
	var cur *FrontierCfgC
	w.Each("frontiercfg", func(e *core.Entity) { cur, _ = e.Get("frontiercfg").(*FrontierCfgC) })
	if cur == nil {
		cur = &FrontierCfgC{}
		w.Spawn(cur)
	}
	made := cur.Made
	*cur = cfg
	cur.Made = made
	return cur
}

// peekFrontierCfg — настройки без побочных эффектов (nil, если ещё не заданы). Для чтения/диагностики.
func peekFrontierCfg(w *core.World) *FrontierCfgC {
	var cur *FrontierCfgC
	w.Each("frontiercfg", func(e *core.Entity) { cur, _ = e.Get("frontiercfg").(*FrontierCfgC) })
	return cur
}

func frontierCfg(w *core.World) *FrontierCfgC {
	cur := peekFrontierCfg(w)
	if cur == nil {
		c := DefaultFrontier()
		cur = ConfigureFrontier(w, c)
	}
	return cur
}

// frontierBatch — сколько поселений добавит СЛЕДУЮЩЕЕ открытие края: деревни и, на каждом
// CityEvery-м открытии, ещё город. Считается ДО изменения мира, чтобы потолок Max был честным.
func frontierBatch(cfg *FrontierCfgC) (n int, city bool) {
	city = cfg.CityEvery > 0 && (cfg.Made+1)%cfg.CityEvery == 0
	n = cfg.Villages
	if city {
		n++
	}
	return n, city
}

var frontierAdj = []string{"Дальний", "Тихий", "Чёрный", "Ветреный", "Старый", "Сизый", "Высокий", "Солёный", "Медовый", "Глухой", "Белый", "Рыжий"}
var frontierNoun = []string{"Удел", "Дол", "Бор", "Берег", "Кряж", "Плёс", "Яр", "Край", "Луг", "Перевал"}
var frontierBiome = []string{"plain", "forest", "hills", "coast", "steppe"}

// OpenFrontier — игрок пришёл в поселение: если это внешний мир и за этим краем ещё ничего нет,
// открыть следующий край. Возвращает имя нового края ("" — ничего не открылось).
func OpenFrontier(w *core.World, e *core.Entity, settlement core.ID) string {
	st := core.Settle(w.Get(settlement))
	if st == nil {
		return ""
	}
	re := w.Get(st.Region)
	r := core.Region(re)
	if r == nil || !r.Outer {
		return "" // стартовые земли не растут: за ними горы
	}
	cfg := frontierCfg(w)
	fr, _ := re.Get("frontier").(*FrontierC)
	if fr == nil {
		fr = &FrontierC{}
		w.Attach(re, fr)
	}
	if !cfg.Enabled || fr.Opened {
		return ""
	}
	total := 0
	w.Each("settlement", func(*core.Entity) { total++ })
	batch, withCity := frontierBatch(cfg)
	if cfg.Max <= 0 || total+batch > cfg.Max {
		return "" // потолок мира: край целиком (деревни + город) не помещается — не открываем ничего
	}
	fr.Opened = true
	cfg.Made++
	// детерминированно от края: одно и то же семя мира открывает одни и те же земли
	g := dice.NewStream(dice.Seed64(w.Seed, "frontier", r.Name, sid(re.ID)))
	// новый край — дальше от центра стартовых земель, в ту же сторону с разбросом
	dx, dy := r.X-500, r.Y-500
	d := math.Hypot(dx, dy)
	if d < 1 {
		dx, dy, d = 1, 0, 1
	}
	ang := math.Atan2(dy, dx) + g.Uniform(-0.6, 0.6)
	nx, ny := r.X+math.Cos(ang)*420, r.Y+math.Sin(ang)*420
	name := frontierAdj[g.Intn(len(frontierAdj))] + " " + frontierNoun[g.Intn(len(frontierNoun))]
	nre := w.Spawn(&core.RegionC{Name: name, Biome: frontierBiome[g.Intn(len(frontierBiome))], Outer: true,
		X: nx, Y: ny, Mana: clampf(r.Mana*g.Uniform(0.7, 1.4), 0.3, 2.2)})
	w.Attach(nre, &FrontierC{Ring: fr.Ring + 1})
	roles := []string{"farmer", "farmer", "fisher", "smith", "hunter", "woodcutter", "miner", "weaver", "cook", "herder", "herbalist", "tavernkeeper"}
	settle := func(nm string, folk int, city bool) {
		se := NewSettlement(w, g, nm, nre)
		s := core.Settle(se)
		s.X, s.Y = nx+g.Uniform(-110, 110), ny+g.Uniform(-110, 110) // без зажима в стартовый квадрат карты
		if city {
			s.Tier = "city"
		}
		var first *core.Entity
		for j := 0; j < folk; j++ {
			sex := "м"
			if g.Chance(0.5) {
				sex = "ж"
			}
			p := starterPerson(w, g, se.ID, roles[(j+g.Intn(3))%len(roles)], sex, 18+float64(g.Intn(45)), j%3 == 0)
			if first == nil {
				first = p
			}
		}
		if first != nil {
			s.Keeper = first.ID
		}
		if h := heatOf(w.Get(settlement)); h != nil { // мир с температурой: новые земли рождаются холодными
			w.Attach(se, &HeatC{Level: 0})
		}
	}
	for i := 0; i < cfg.Villages; i++ {
		settle(procToponym(g), cfg.Folk, false)
	}
	if withCity {
		settle(procToponym(g), cfg.Folk*3, true)
	}
	if e != nil {
		say(w, e, "road", "Местные говорят: за дальним перевалом лежат ещё земли — "+name+".")
	}
	return name
}

// FrontierStats — сколько краёв открыто и каков потолок (для /api/stats).
func FrontierStats(w *core.World) map[string]any {
	cfg := peekFrontierCfg(w) // только чтение: диагностика не создаёт настройки в мире
	if cfg == nil {
		d := DefaultFrontier()
		cfg = &d
	}
	n := 0
	w.Each("settlement", func(*core.Entity) { n++ })
	return map[string]any{"enabled": cfg.Enabled, "settlements": n, "max": cfg.Max, "opened": cfg.Made, "villages": cfg.Villages}
}
