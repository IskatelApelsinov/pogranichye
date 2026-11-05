package sim

import (
	"math"

	"pogranichye/core"
	"pogranichye/dice"
)

// Корчма и питьё — первое прямое действие игрока в мире (интерактивность v1.2).
//
// Правила мира, не UI:
//   - в корчме поселения варится пиво (запас в Store["beer"], подновляется корчмарём — BrewSystem);
//   - игрок у стойки может КУПИТЬ кружку за монеты (−1 из запаса, −цена из кошеля, +кружка в руку);
//   - отдельным действием ВЫПИТЬ кружку (эффект на нужды по правилам мира — не через eat/socialize);
//   - покупка/питьё АТОМАРНЫ и ИДЕМПОТЕНТНЫ по reqid (повтор запроса не списывает дважды);
//   - нехватка пива/монет/дальность от стойки — НИЧЕГО не меняется.
//
// Сервер — владелец. Клиент рисует кружку/руку только по подтверждённому состоянию (me.mug).

const (
	BeerGood    = "beer"
	beerCap     = 24.0 // сколько пива держит корчма
	beerBrewDay = 6.0  // сколько доваривается за сутки
	BeerPrice   = 0.5  // цена кружки в монетах
	tavernReach = 2.5  // м — насколько близко к стойке надо стоять, чтобы заказать
	beerMorale  = 8.0  // прибавка к настрою от кружки
	beerHunger  = 4.0  // чуть притупляет голод
)

func init() {
	core.Register("mug", func() core.Component { return &MugC{} })
}

// MugC — кружка в руке игрока. Идемпотентность покупки/питья — через ограниченную историю reqid в
// PlayerC (общий bounded dedup, см. actions.go), а не отдельные поля.
type MugC struct {
	Has bool `json:"has"`
}

func (c *MugC) Kind() string { return "mug" }

func Mug(e *core.Entity) *MugC { c, _ := e.Get("mug").(*MugC); return c }

func ensureMug(w *core.World, e *core.Entity) *MugC {
	if m := Mug(e); m != nil {
		return m
	}
	m := &MugC{}
	w.Attach(e, m)
	return m
}

// BrewSystem — корчма доваривает пиво раз в сутки (до потолка). Варит только там, где есть корчма.
type BrewSystem struct{}

func (BrewSystem) Name() string { return "brew" }

func (BrewSystem) Hour(w *core.World, clock core.Clock, _ *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil || !s.Tavern {
			return
		}
		if s.Store == nil {
			s.Store = map[string]float64{}
		}
		if s.Store[BeerGood] < beerCap {
			s.Store[BeerGood] = math.Min(beerCap, s.Store[BeerGood]+beerBrewDay)
		}
	})
}

// atTavern — стоит ли тело у стойки корчмы (по сцене). ОГРАНИЧЕНИЕ: координаты стойки берутся из
// общей раскладки одной стартовой сцены (poiByKind). Для мира с несколькими поселениями, у каждого
// со своей корчмой, нужна сцена на поселение — отдельная веха; здесь предполагается одна активная сцена.
func atTavern(e *core.Entity) bool {
	p := ScenePos(e)
	if p == nil {
		return false
	}
	tx, ty := poiByKind("tavern")
	return math.Hypot(p.X-tx, p.Y-ty) <= tavernReach
}

// BuyBeer — АТОМАРНАЯ идемпотентная покупка кружки. Вызывать под замком. Требует непустой reqid;
// повтор того же reqid — успех без повторного списания (bounded история в PlayerC).
func BuyBeer(w *core.World, e *core.Entity, reqid string) (bool, string) {
	if e == nil {
		return false, "нет тела"
	}
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	mug := ensureMug(w, e)
	if mug.Has {
		return false, "кружка уже в руке"
	}
	if !atTavern(e) {
		return false, "надо подойти к стойке корчмы"
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return false, "ты не в поселении"
	}
	se := w.Get(pl.Settlement)
	s := core.Settle(se)
	if s == nil || !s.Tavern {
		return false, "здесь нет корчмы"
	}
	if s.Store == nil || s.Store[BeerGood] < 1 {
		return false, "пиво кончилось"
	}
	pu := core.Purse(e)
	if pu == nil || pu.Coins < BeerPrice {
		return false, "не хватает монет"
	}
	// атомарно: всё проверено — меняем разом
	pu.Coins -= BeerPrice
	s.Store[BeerGood] -= 1
	mug.Has = true
	pc.markDone(reqid)
	return true, ""
}

// DrinkBeer — АТОМАРНОЕ идемпотентное питьё: эффект по правилам мира. Вызывать под замком.
func DrinkBeer(w *core.World, e *core.Entity, reqid string) (bool, string) {
	if e == nil {
		return false, "нет тела"
	}
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	mug := ensureMug(w, e)
	if !mug.Has {
		return false, "нет кружки"
	}
	v := core.Vitals(e)
	if v == nil {
		return false, "нет тела"
	}
	mug.Has = false
	pc.markDone(reqid)
	v.Morale = math.Min(100, v.Morale+beerMorale)
	v.Hunger = math.Max(0, v.Hunger-beerHunger)
	return true, ""
}
