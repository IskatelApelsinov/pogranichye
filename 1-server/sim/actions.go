package sim

import (
	"math"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Прямые действия игрока с миром (интерактивность v1.3): рубка леса, рыбалка, магический разряд.
// Все — серверно-авторитетные, с реальными условиями (место, силы, мана) и реальным исходом
// (товар в закрома, убыль зверя). Повтор того же reqid идемпотентен.

const (
	chopEnergy     = 8.0 // сил на одну рубку
	fishEnergy     = 7.0 // сил на один заброс
	chopYield      = 1.0 // брёвен за рубку
	fishYield      = 1.0 // рыбин за удачный лов
	brushEnergy    = 5.0 // сил на один сбор хвороста — легче рубки
	brushYield     = 1.0 // хвороста за сбор — честно меньше ценности бревна (другой товар, не замена)
	brushSkillGain = 0.3 // навык дровосека растёт медленнее, чем от настоящей рубки (0.6)
	actReach       = 6.0 // м — насколько близко к POI/зоне надо стоять для действия

	sparkCost    = 25.0 // маны на разряд
	sparkManaMax = 100.0
	sparkRegen   = 6.0 // маны в секунду восстановления (реальное время)
	sparkWard    = 1.5 // на сколько разряд отпугивает/прореживает опасного зверя

	// comms 0302/0303/0304: рыба — настоящий конечный запас места (econ.ResourceNodeC{Node:
	// NodeFishery}), не бесконечный ресурс по одному факту типа воды. У реки запас заводится
	// честно (река почти всегда рыбная); у пруда — не всегда: честная монета по зерну места
	// решает, есть ли в НЕМ рыба вообще (вода не доказывает рыбу, см. 0304). Колодец — никогда.
	// Числа — начальная прикидка баланса, не финал: пруд скромнее реки по запасу и приросту.
	riverFishCap   = 60.0
	riverFishRegen = 3.0
	pondFishCap    = 24.0
	pondFishRegen  = 1.0
	pondFishChance = 0.6 // доля прудов, где рыба вообще есть
)

func init() {
	core.Register("player", func() core.Component { return &PlayerC{} })
}

const dedupCap = 64 // сколько недавних reqid помним на игрока (ограниченная история — без роста без предела)

// PlayerC — интерактивное состояние игрока: мана врождённого «искрения» (демо-оружие) и ОГРАНИЧЕННАЯ
// история обработанных reqid (кольцо) для идемпотентности при ретрае. reqid записывается ТОЛЬКО при
// успехе действия, поэтому повтор после неудачи (отошёл/нет сил) по тому же id корректно сработает.
type PlayerC struct {
	Mana     float64  `json:"mana"`
	Done     []string `json:"done"`                // недавние успешно обработанные reqid (кольцо ≤ dedupCap)
	CardGame string   `json:"card_game,omitempty"` // последняя выбранная карточная игра для общего me.card_game
	// коррелируемый результат последнего действия (#40 ack): клиент сверяет по reqid
	RReqid    string `json:"-"`
	ROk       bool   `json:"-"`
	RReason   string `json:"-"`
	DrankBeer bool
	exitN     int // счётчик выходов на тракт ногами (для уникальных reqid) `json:"-"` // пил ли пиво (для вводной задачи #80)
}

// SetResult записывает результат последнего действия игрока (для ack в кадре me.result).
func SetResult(w *core.World, e *core.Entity, reqid string, ok bool, reason string) {
	if reqid == "" {
		return
	}
	pc := EnsurePlayer(w, e)
	pc.RReqid, pc.ROk, pc.RReason = reqid, ok, reason
}

func (c *PlayerC) Kind() string { return "player" }

func Player(e *core.Entity) *PlayerC { c, _ := e.Get("player").(*PlayerC); return c }

// EnsurePlayer вешает интерактивное состояние игрока (полная мана), если его ещё нет.
func EnsurePlayer(w *core.World, e *core.Entity) *PlayerC {
	if p := Player(e); p != nil {
		return p
	}
	p := &PlayerC{Mana: sparkManaMax}
	w.Attach(e, p)
	return p
}

// alreadyDone — обрабатывался ли уже этот reqid (идемпотентный повтор успешного действия).
func (p *PlayerC) alreadyDone(reqid string) bool {
	for _, r := range p.Done {
		if r == reqid {
			return true
		}
	}
	return false
}

// markDone записывает успешно обработанный reqid в кольцо (вытесняя старые сверх лимита).
func (p *PlayerC) markDone(reqid string) {
	p.Done = append(p.Done, reqid)
	if len(p.Done) > dedupCap {
		p.Done = p.Done[len(p.Done)-dedupCap:]
	}
}

// RegenMana восстанавливает ману игроков в РЕАЛЬНОМ времени (вызывается из tick-цикла с dt секунд).
func RegenMana(w *core.World, dt float64) {
	if w == nil || dt <= 0 {
		return
	}
	w.Each("player", func(e *core.Entity) {
		p := Player(e)
		if p.Mana < sparkManaMax {
			p.Mana = math.Min(sparkManaMax, p.Mana+sparkRegen*dt)
		}
	})
}

// trainSkill растит умение игрока от дела (прокачка с замедлением — см. SkillsC.Train).
func trainSkill(w *core.World, e *core.Entity, name string, amt float64) {
	sk := core.Skills(e)
	if sk == nil {
		sk = &core.SkillsC{Skills: map[string]float64{}}
		w.Attach(e, sk)
	}
	sk.Train(name, amt)
	trainStatsFor(w, e, name, amt) // дело растит и характеристики
}

func addStash(w *core.World, e *core.Entity, good string, n float64) {
	s := econ.Stash(e)
	if s == nil {
		s = &econ.StashC{Goods: map[string]float64{}}
		w.Attach(e, s)
	}
	if s.Goods == nil {
		s.Goods = map[string]float64{}
	}
	s.Goods[good] += n
}

// ChopWood — срубить дерево у леса: нужны силы и близость к опушке; даёт бревно в закрома.
func ChopWood(w *core.World, e *core.Entity, reqid string) (bool, string) {
	if why := siteLacks(w, e, "woods"); why != "" {
		return false, why
	}
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, "" // идемпотентный повтор успешного действия
	}
	p := ScenePos(e)
	if p == nil {
		return false, "нет тела в сцене"
	}
	l := layoutFor(w, e)
	wx, wy := l.POI("woods")
	if !l.InWoods(p.X, p.Y) && math.Hypot(p.X-wx, p.Y-wy) > actReach {
		return false, "надо подойти к лесу"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < chopEnergy {
		return false, "нет сил рубить"
	}
	v.Energy -= chopEnergy
	addStash(w, e, econ.GoodWood, chopYield)
	trainSkill(w, e, "woodcutter", 0.6) // прокачка дровосека
	pc.markDone(reqid)                  // запись только при успехе
	return true, ""
}

// GatherBrushwood — собрать хворост в рощице (copse, comms 0215 v1.23): для сёл БЕЗ настоящего
// строевого леса (feature "woods" не стоит, generateLayout кладёт только decorative copse —
// l.Zones никогда не содержит одновременно forest и copse, см. layout_gen.go). В отличие от
// ChopWood НЕ смотрит на siteLacks("woods") — само место здесь решает, не флаг поселения — поэтому
// честно недоступно, если игрок физически не в зоне copse (InWoods включает copse, но тут нужно
// именно её, не полный лес — там своя полноценная рубка). Даёт отдельный товар GoodBrushwood,
// меньше и медленнее навыка, чем настоящее бревно — не выдаём хворост за брёвна.
func GatherBrushwood(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil {
		return false, "нет тела в сцене"
	}
	l := layoutFor(w, e)
	inCopse := false
	for _, z := range l.Zones {
		if z.Kind == "copse" && z.contains(p.X, p.Y) {
			inCopse = true
			break
		}
	}
	if !inCopse {
		return false, "здесь нет рощицы для хвороста"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < brushEnergy {
		return false, "нет сил собирать"
	}
	v.Energy -= brushEnergy
	addStash(w, e, econ.GoodBrushwood, brushYield)
	trainSkill(w, e, "woodcutter", brushSkillGain)
	pc.markDone(reqid)
	return true, ""
}

// waterKind — честный тип воды места для лова: "river" (включая стартовое село, у которого
// HasRiver выставлен напрямую, без Land.Water) | "pond" | "" (нет воды для лова — в т.ч. колодец).
func waterKind(l *Layout) string {
	if l.HasRiver || l.Land.Water == "river" {
		return "river"
	}
	if l.Land.Water == "pond" {
		return "pond"
	}
	return ""
}

// fisheryNode — существующий серверный запас рыбы места (econ.ResourceNodeC{Node: NodeFishery})
// по конкретному поселению, если он уже заведён (фоновой экономикой или прежним ловом).
func fisheryNode(w *core.World, seID core.ID) *econ.ResourceNodeC {
	var n *econ.ResourceNodeC
	w.Each("resnode", func(ne *core.Entity) {
		if n != nil {
			return
		}
		if rn := econ.Node(ne); rn != nil && rn.Node == econ.NodeFishery && rn.Settlement == seID {
			n = rn
		}
	})
	return n
}

// fisheryExpectationFor — что ЧЕСТНО положено месту с раскладкой l по его воде, БЕЗ побочных
// эффектов (для place.site: превью доступности нельзя заводить сущности на каждый кадр каждого
// зрителя) и без обращения к кешу LayoutOf (чистая функция — проще и безопаснее тестировать).
// У реки рыба почти всегда есть; у пруда — не всегда: сам факт land.water=="pond" не доказывает
// рыбу (решение Kate, comms 0304) — честная монета по зерну места решает раз и навсегда
// (детерминированно, тот же бросок при реальном заведении запаса в ensureFisheryFor).
func fisheryExpectationFor(w *core.World, se *core.Entity, l *Layout) (kind string, hasFish bool) {
	if se == nil || l == nil {
		return "", false
	}
	kind = waterKind(l)
	switch kind {
	case "river":
		return kind, true
	case "pond":
		s := core.Settle(se)
		g := dice.NewStream(dice.Seed64(w.Seed, "pond-fish", s.Name, sid(se.ID)))
		return kind, g.Chance(pondFishChance)
	default:
		return "", false
	}
}

// fisheryExpectation — fisheryExpectationFor по текущей (кешированной) раскладке поселения.
func fisheryExpectation(w *core.World, se *core.Entity) (string, bool) {
	if se == nil {
		return "", false
	}
	return fisheryExpectationFor(w, se, LayoutOf(w, se.ID))
}

// ensureFisheryFor — реальный серверный запас рыбы места: если уже существует (фоновая экономика
// её, возможно, уже тронула), берём его; иначе заводим один раз по той же честной монете, что и
// fisheryExpectationFor (один источник правды, не параллельный счётчик). У пруда без рыбы (монета
// не выпала) и у колодца — nil: лов там недоступен, и повторный вызов это же нескончаемо
// подтвердит (одна и та же монета на тех же входных данных).
func ensureFisheryFor(w *core.World, se *core.Entity, l *Layout) *econ.ResourceNodeC {
	if se == nil {
		return nil
	}
	if n := fisheryNode(w, se.ID); n != nil {
		return n
	}
	kind, has := fisheryExpectationFor(w, se, l)
	if !has {
		return nil
	}
	stockCap, regen := riverFishCap, riverFishRegen
	if kind == "pond" {
		stockCap, regen = pondFishCap, pondFishRegen
	}
	return econ.Node(w.Spawn(&econ.ResourceNodeC{Node: econ.NodeFishery, Good: econ.GoodFish,
		Settlement: se.ID, Stock: stockCap, Cap: stockCap, Regen: regen}))
}

// FishAvailable — честное превью для кадра (place.site.can_fish): видна ли СЕЙЧАС рыба в месте
// игрока. Не заводит сущностей (см. fisheryExpectation) — только читает, если запас уже есть.
func FishAvailable(w *core.World, se *core.Entity) bool {
	if se == nil {
		return false
	}
	if n := fisheryNode(w, se.ID); n != nil {
		return n.Stock >= fishYield
	}
	_, has := fisheryExpectation(w, se)
	return has
}

// FishCatch — закинуть снасть у реки или пруда: нужны силы, берег и реальная рыба в запасе места
// (comms 0302/0304) — не любая вода одинаково рыбная, и лов не бесконечен. Отказ из-за отсутствия
// рыбы не тратит силы и не выдаёт рыбу (ни при пустом запасе, ни при его отсутствии — игроку это
// не различимо намеренно, чтобы не выдавать скрытые детали мира).
func FishCatch(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return fishCatchAt(w, e, layoutFor(w, e), settlementEnt(w, e), reqid)
}

// fishCatchAt — тело FishCatch с явной раскладкой/поселением (тестируемо без кеша LayoutOf: тесты
// на пруд/колодец подают синтетическую Layout, минуя процедурную генерацию).
func fishCatchAt(w *core.World, e *core.Entity, l *Layout, se *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil {
		return false, "нет тела в сцене"
	}
	if waterKind(l) == "" {
		return false, "здесь нет ни реки, ни пруда — рыбу ловят у воды"
	}
	rx, ry := l.POI("river")
	if !l.NearWater(p.X, p.Y) && math.Hypot(p.X-rx, p.Y-ry) > actReach {
		return false, "надо встать у воды"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < fishEnergy {
		return false, "нет сил на лов"
	}
	fn := ensureFisheryFor(w, se, l)
	if fn == nil || fn.Stock < fishYield {
		return false, "рыба здесь сейчас не водится"
	}
	v.Energy -= fishEnergy
	fn.Stock -= fishYield
	addStash(w, e, econ.GoodFish, fishYield)
	trainSkill(w, e, "fisher", 0.6) // прокачка рыбака
	pc.markDone(reqid)
	return true, ""
}

// CastSpark — магический разряд (демо-оружие): тратит ману; если рядом в угодьях есть опасный зверь —
// отпугивает/прореживает его. Требует быть в поселении на открытом месте (не в стене) — проверка зоны.
func CastSpark(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil || layoutFor(w, e).Blocked(p.X, p.Y) {
		return false, "негде творить разряд"
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return false, "ты не в угодьях поселения"
	}
	if pc.Mana < sparkCost {
		return false, "не хватает маны"
	}
	pc.Mana -= sparkCost
	trainSkill(w, e, "mage", 0.3) // прокачка дара
	pc.markDone(reqid)
	// бьёт по опасному зверю в угодьях поселения (если есть)
	if se := w.Get(pl.Settlement); se != nil {
		if f := battle.Fauna(se); f != nil {
			for _, sp := range []string{"wolf", "bear", "boar"} {
				if f.Pop[sp] >= 1 {
					f.Pop[sp] = math.Max(0, f.Pop[sp]-sparkWard)
					break
				}
			}
		}
	}
	return true, ""
}
