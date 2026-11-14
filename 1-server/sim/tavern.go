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

// atTavern — в корчме ли тело вообще (общий, нестрогий признак: у внешнего порога ИЛИ уже внутри
// здания), для разговоров, карточных игр (durak.go/ochko.go) и общих социальных действий
// (social.go) — им всем достаточно "в корчме", без точного стояния у стойки. Не путать с
// atTavernCounter ниже, который специально строже (только у самой стойки) — для заказа пива.
func atTavern(w *core.World, e *core.Entity) bool {
	p := ScenePos(e)
	if p == nil {
		return false
	}
	l := layoutFor(w, e)
	tx, ty := l.POI("tavern")
	if math.Hypot(p.X-tx, p.Y-ty) <= tavernReach {
		return true
	}
	return l.insideTavernBuilding(p.X, p.Y) // внутри здания (напр., уже дошёл до стойки) — тоже "в корчме"
}

// atTavernCounter — стоит ли тело именно у СТОЙКИ корчмы своего поселения, а не просто где-то в
// здании/у двери (comms 0223): раньше заказ пива проверяли так же нестрого, как atTavern выше (по
// старому внешнему порогу POI "tavern" — он же дверь), что не различало "дошёл до двери" от "стоит
// у стойки внутри". Теперь — до геометрического центра здания "tavern" именно той раскладки, где
// физически стоит тело (TavernCounter, sim/layout.go), с учётом актуального радиуса. Используется
// ТОЛЬКО для покупки пива — разговоры/карты/угощение остаются на общем, нестрогом atTavern.
func atTavernCounter(w *core.World, e *core.Entity) bool {
	p := ScenePos(e)
	if p == nil {
		return false
	}
	cx, cy, radius, ok := layoutFor(w, e).TavernCounter()
	if !ok {
		return false
	}
	return math.Hypot(p.X-cx, p.Y-cy) <= radius
}

// bartenderServing — обслуживает ли сейчас корчмарь ЭТОГО поселения стойку (comms 0223): им должен
// быть именно привязанный к селу корчмарь (Settle.Keeper, "хозяин корчмы" — то же лицо, что и глава
// села в этой игре), он должен быть жив и присутствовать в мире, и его текущее подтверждённое
// занятие (MindC.ActCode, выставляется раз в час в sim/systems.go из sim/activity.go) — "keep_tavern"
// ("за стойкой корчмы"). Переиспользуем существующие role/act_code/identity — НЕ создаём отдельного
// временного NPC специально под продажу пива.
func bartenderServing(w *core.World, se *core.Entity) bool {
	s := core.Settle(se)
	if s == nil || s.Keeper == 0 {
		return false
	}
	k := w.Get(s.Keeper)
	if k == nil {
		return false // корчмаря сейчас нет в мире (умер/не загружен) — некому продать
	}
	id := core.Ident(k)
	if id == nil || id.Role != "tavernkeeper" {
		return false
	}
	m := core.Mind(k)
	return m != nil && m.ActCode == "keep_tavern"
}

// TavernCounterView — аддитивное поле контракта place.tavern_counter (comms 0223): где стоять, чтобы
// заказать пиво, и прислуживает ли сейчас корчмарь. Координаты — локальные (та же система, что
// place.anchor + me.pos), сервер считает их сам из геометрии; клиент НЕ присылает и не выбирает их
// сам — покупка (BuyBeer) всё равно проверяется заново на сервере независимо от этого поля. nil —
// в этом поселении нет корчмы с геометрией (не ожидается при st.Tavern==true, но на всякий случай).
func TavernCounterView(w *core.World, settlement core.ID, se *core.Entity) map[string]any {
	l := LayoutOf(w, settlement)
	cx, cy, radius, ok := l.TavernCounter()
	if !ok {
		return nil
	}
	return map[string]any{
		"x": math.Round(cx*1000) / 1000, "y": math.Round(cy*1000) / 1000, "radius": radius,
		"bartender_present": bartenderServing(w, se),
	}
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
	if !atTavernCounter(w, e) {
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
	if !bartenderServing(w, se) {
		return false, "корчмаря сейчас нет за стойкой"
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
	addDrunk(w, e, 22) // хмель — настоящее серверное состояние
	v.Hunger = math.Max(0, v.Hunger-beerHunger)
	pc.DrankBeer = true
	return true, ""
}
