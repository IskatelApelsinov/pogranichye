package sim

import (
	"math"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Большое расширение мира (v1.7): инвентарь и предметы (топор/удочка/факел/фонарь/меч/лук/одежда),
// настоящий рынок (купля-продажа), путешествия между деревнями, занятия (стройка, торговля, охота,
// стычка), желания НПС. Всё аддитивно и серверно-авторитетно. Балансу — отдельная шлифовка; цель —
// рабочие механики вширь.

func init() {
	core.Register("inventory", func() core.Component { return &InventoryC{Items: map[string]int{}, Worn: map[string]string{}} })
}

// InventoryC — вещи в руках/суме и надетая одежда. Hand — что в руке (инструмент/оружие).
type InventoryC struct {
	Items map[string]int    `json:"items"` // предмет → сколько
	Hand  string            `json:"hand"`  // что в руке (axe|sword|bow|rod|torch|lantern|"")
	Worn  map[string]string `json:"worn"`  // слот одежды → предмет (cloak|boots|hat)
}

func (c *InventoryC) Kind() string { return "inventory" }

func Inv(e *core.Entity) *InventoryC { c, _ := e.Get("inventory").(*InventoryC); return c }

func ensureInv(w *core.World, e *core.Entity) *InventoryC {
	if i := Inv(e); i != nil {
		if i.Items == nil {
			i.Items = map[string]int{}
		}
		if i.Worn == nil {
			i.Worn = map[string]string{}
		}
		return i
	}
	i := &InventoryC{Items: map[string]int{}, Worn: map[string]string{}}
	w.Attach(e, i)
	return i
}

// itemSource — где можно взять предмет (ключ POI локации). Факел/фонарь/одежда — на рынке/в амбаре.
var itemSource = map[string]string{
	"axe": "woods", "rod": "river", "sword": "smithy", "bow": "smithy",
	"torch": "market", "lantern": "market", "cloak": "granary", "boots": "granary", "hat": "granary",
}
var wearSlot = map[string]string{"cloak": "body", "boots": "feet", "hat": "head"}
var handItem = map[string]bool{"axe": true, "sword": true, "bow": true, "rod": true, "torch": true, "lantern": true}

func atPoi(e *core.Entity, poi string) bool {
	p := ScenePos(e)
	if p == nil {
		return false
	}
	x, y := poiByKind(poi)
	return math.Hypot(p.X-x, p.Y-y) <= 10
}

// Take — подобрать предмет в подходящей локации. Идемпотентность по reqid (через PlayerC).
func Take(w *core.World, e *core.Entity, item, reqid string) (bool, string) {
	src, ok := itemSource[item]
	if !ok {
		return false, "такого не поднять"
	}
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if !atPoi(e, src) {
		return false, "этого здесь нет — ищи в нужном месте"
	}
	ensureInv(w, e).Items[item]++
	pc.markDone(reqid)
	return true, ""
}

// Equip — взять предмет в руку (должен быть в суме и быть «ручным»).
func Equip(w *core.World, e *core.Entity, item string) (bool, string) {
	inv := ensureInv(w, e)
	if item != "" && inv.Items[item] < 1 {
		return false, "такого нет в суме"
	}
	if item != "" && !handItem[item] {
		return false, "это не берут в руку"
	}
	inv.Hand = item
	return true, ""
}

// Wear — надеть одежду (слот по предмету). Лёгкий подъём настроя за обновку.
func Wear(w *core.World, e *core.Entity, item string) (bool, string) {
	slot, ok := wearSlot[item]
	if !ok {
		return false, "это не носят"
	}
	inv := ensureInv(w, e)
	if inv.Items[item] < 1 {
		return false, "такой одежды нет"
	}
	inv.Worn[slot] = item
	if v := core.Vitals(e); v != nil {
		v.Morale = math.Min(100, v.Morale+2)
	}
	return true, ""
}

// --- Рынок: настоящая купля-продажа у амбара/лавки ---

func settlementEnt(w *core.World, e *core.Entity) *core.Entity {
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return nil
	}
	return w.Get(pl.Settlement)
}

// Trade — купить/продать товар на рынке поселения по текущей цене. side: "buy"|"sell".
// reqid обязателен и идемпотентен: повтор того же пакета не повторяет сделку (двойное списание).
func Trade(w *core.World, e *core.Entity, good, side string, qty int, reqid string) (bool, string) {
	if qty < 1 {
		qty = 1
	}
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	se := settlementEnt(w, e)
	if se == nil {
		return false, "ты не в поселении"
	}
	s := core.Settle(se)
	pu := core.Purse(e)
	if s == nil || pu == nil {
		return false, "нет рынка"
	}
	price := econ.Price(se, good)
	if price <= 0 {
		price = 1
	}
	total := price * float64(qty)
	st := ensureStashOf(w, e)
	if s.Store == nil {
		s.Store = map[string]float64{}
	}
	switch side {
	case "buy":
		if pu.Coins < total {
			return false, "не хватает монет"
		}
		if s.Store[good] < float64(qty) {
			return false, "на рынке нет столько"
		}
		pu.Coins -= total
		s.Store[good] -= float64(qty)
		st.Goods[good] += float64(qty)
	case "sell":
		if st.Goods[good] < float64(qty) {
			return false, "у тебя нет столько"
		}
		st.Goods[good] -= float64(qty)
		s.Store[good] += float64(qty)
		pu.Coins += total * 0.9 // купец берёт малую мзду
	default:
		return false, "купить или продать?"
	}
	pc.markDone(reqid)
	return true, ""
}

func ensureStashOf(w *core.World, e *core.Entity) *econ.StashC {
	s := econ.Stash(e)
	if s == nil {
		s = &econ.StashC{Goods: map[string]float64{}}
		w.Attach(e, s)
	}
	if s.Goods == nil {
		s.Goods = map[string]float64{}
	}
	return s
}

// --- Путешествия между деревнями ---

// Travel — перейти в другое поселение (меняет PlaceC и ставит у колодца). Разрешаем к любому
// существующему поселению-соседу (демо-мир — «центр мира», отделён морем/горами от прочего).
func Travel(w *core.World, e *core.Entity, settlement core.ID) (bool, string) {
	se := w.Get(settlement)
	if se == nil || core.Settle(se) == nil {
		return false, "нет такого поселения"
	}
	pl := core.Place(e)
	if pl == nil {
		return false, "некому путешествовать"
	}
	if OnRoad(e) {
		return false, "ты уже в пути"
	}
	pl.Settlement = settlement
	LearnVisited(w, e, settlement)
	GoTo(w, e, "well")
	OpenFrontier(w, e, settlement) // врата во внешний мир: за краем, куда пришёл игрок, открывается следующий
	return true, ""
}

// Settlements — список поселений мира для карты путешествий (id, имя, тир, координаты).
func Settlements(w *core.World) []map[string]any {
	out := []map[string]any{}
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil {
			return
		}
		out = append(out, map[string]any{"id": se.ID, "name": s.Name, "tier": s.Tier,
			"x": round1x(s.X), "y": round1x(s.Y),
			"outer": isOuter(w, s)}) // внешний мир — за горами демо-зоны
	})
	return out
}

func round1x(v float64) float64 { return math.Round(v*10) / 10 }

// isOuter — относится ли поселение к внешнему миру (регион помечен Outer). Без глобального состояния.
func isOuter(w *core.World, s *core.SettlementC) bool {
	if r := core.Region(w.Get(s.Region)); r != nil {
		return r.Outer
	}
	return false
}

// --- Занятия: стройка, охота, стычка ---

// Build — помочь на стройке: тратит дерево из закромов, растит навык строителя, крепит село (store wood).
func Build(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	st := ensureStashOf(w, e)
	if st.Goods[econ.GoodWood] < 2 {
		return false, "нужно хотя бы 2 дерева"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < 10 {
		return false, "нет сил строить"
	}
	st.Goods[econ.GoodWood] -= 2
	v.Energy -= 10
	if se := settlementEnt(w, e); se != nil {
		if s := core.Settle(se); s != nil {
			if s.Store == nil {
				s.Store = map[string]float64{}
			}
			s.Store[econ.GoodWood] += 2 // вклад в общее строительство
		}
	}
	trainSkill(w, e, "builder", 0.7)
	pc.markDone(reqid)
	return true, ""
}

// Hunt — поохотиться (лучше с луком): если в угодьях есть зверь — добыть мясо/шкуру, подрастить навык,
// с риском раны. Простая модель без внешней dice: исход из навыка, оружия и хэша reqid.
func Hunt(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return chaseBeast(w, e, reqid, false)
}

// Fight — стычка со зверем (лучше с мечом): риск выше, добычи меньше, но отгоняет опасность.
func Fight(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return chaseBeast(w, e, reqid, true)
}

func chaseBeast(w *core.World, e *core.Entity, reqid string, melee bool) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < 12 {
		return false, "нет сил"
	}
	se := settlementEnt(w, e)
	if se == nil {
		return false, "негде охотиться"
	}
	f := battle.Fauna(se)
	if f == nil {
		return false, "зверья не водится"
	}
	// выбрать цель: для охоты — дичь/заяц/кабан, для стычки — хищник
	order := []string{"hare", "boar", "deer", "fox"}
	if melee {
		order = []string{"wolf", "bear", "boar"}
	}
	target := ""
	for _, sp := range order {
		if f.Pop[sp] >= 1 {
			target = sp
			break
		}
	}
	if target == "" {
		return false, "подходящего зверя рядом нет"
	}
	inv := ensureInv(w, e)
	skill := 0.0
	if sk := core.Skills(e); sk != nil {
		skill = sk.Level("hunter")
	}
	weapon := 0.0
	if melee && inv.Hand == "sword" {
		weapon = 25
	} else if !melee && inv.Hand == "bow" {
		weapon = 25
	} else if inv.Hand == "axe" {
		weapon = 10
	}
	danger := map[string]float64{"hare": 2, "fox": 6, "deer": 8, "boar": 22, "wolf": 30, "bear": 45}[target]
	roll := float64(hashReq(reqid)%100) // 0..99
	success := roll < 45+skill*1.5+weapon // шанс растёт от навыка и оружия
	v.Energy -= 12
	trainSkill(w, e, "hunter", 0.8)
	if success {
		f.Pop[target] = math.Max(0, f.Pop[target]-1)
		st := ensureStashOf(w, e)
		st.Goods["meat"] += 1
		if !melee {
			st.Goods["hide"] += 1
		}
		fireSocial(w, e, "hunt", 0) // подтверждённый момент добычи для клиента
		return true, ""
	}
	// промах/отпор зверя — риск раны пропорционально опасности
	if roll > 80 {
		hurt := danger * 0.4
		v.Health = math.Max(1, v.Health-hurt)
	}
	return false, "зверь ушёл" + map[bool]string{true: ", и потрепал тебя", false: ""}[roll > 80]
}

func hashReq(s string) uint64 {
	var h uint64 = 1469598103934665603
	for i := 0; i < len(s); i++ {
		h ^= uint64(s[i])
		h *= 1099511628211
	}
	return h
}

// Desire — текущее главное желание персонажа (для показа «жизни» НПС и игрока). Из нужд и цели.
func Desire(e *core.Entity) string {
	v := core.Vitals(e)
	if v != nil {
		if v.Hunger > 70 { // голод РАСТЁТ к 100 (NeedsSystem) — высокий значит голоден
			return "голоден"
		}
		if v.Energy < 25 {
			return "устал"
		}
		if v.Morale < 30 {
			return "тоскует"
		}
		if v.Health < 40 {
			return "хворает"
		}
	}
	if m := core.Mind(e); m != nil {
		switch m.Goal {
		case "work":
			return "при деле"
		case "eat":
			return "хочет поесть"
		case "rest":
			return "на отдых"
		case "socialize":
			return "к людям"
		case "seek_home":
			return "домой"
		case "wander":
			return "бродит"
		}
	}
	return "в порядке"
}

// PublicGear — ПУБЛИЧНЫЙ видимый subset экипировки сущности для соседей (#48): предмет в руке и
// надетая одежда. Личную суму (items/количества) НЕ раскрываем. Пусто, если инвентаря нет.
func PublicGear(e *core.Entity) map[string]any {
	i := Inv(e)
	if i == nil {
		return map[string]any{"hand": "", "worn": map[string]string{}}
	}
	worn := map[string]string{}
	for k, v := range i.Worn {
		worn[k] = v
	}
	return map[string]any{"hand": i.Hand, "worn": worn}
}

// OpeningTask — минимальная вводная задача (#80) из РЕАЛЬНОГО состояния (не по клику/goal): познакомься
// с жителем → поработай (сруби/налови/построй/добудь) → загляни в корчму и выпей. Сервер-истина.
func OpeningTask(e *core.Entity) map[string]any {
	met := false
	if a := Acquaint(e); a != nil && len(a.Known) > 0 {
		met = true
	}
	worked := false
	if sk := core.Skills(e); sk != nil {
		for _, s := range []string{"woodcutter", "fisher", "builder", "hunter"} {
			if sk.Level(s) > 0 {
				worked = true
			}
		}
	}
	drank := false
	if p := Player(e); p != nil && p.DrankBeer {
		drank = true
	}
	stage, text := 1, "Познакомься с кем-нибудь из жителей (💬)."
	if met {
		stage, text = 2, "Поработай: сруби дрова, налови рыбы или помоги на стройке."
	}
	if met && worked {
		stage, text = 3, "Загляни в корчму и выпей кружку пива."
	}
	done := met && worked && drank
	if done {
		text = "Освоился! Деревня твоя — живи и промышляй."
	}
	return map[string]any{"stage": stage, "met": met, "worked": worked, "drank": drank, "done": done, "text": text}
}
