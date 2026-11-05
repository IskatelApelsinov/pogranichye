package sim

import (
	"math"

	"pogranichye/core"
)

// Социальный слой (v1.4): знакомство (имя открывается после знакомства), разговор с репликами,
// угощение НПС пивом. Сервер — владелец: имя в кадре соседа есть только у знакомых игроку жителей.
//
// Знакомство диегетично (#25): пока не поговорил — житель аноним («рыбак», «кузнец»); поговорил и
// представился — узнал имя. В корчме знакомиться проще (люди расположены к беседе) — см. talkReach.

const (
	talkReach      = 5.0  // м — на каком расстоянии можно заговорить
	talkReachBar   = 8.0  // м — у корчмы круг общения шире (знакомятся в баре)
	treatMorale    = 10.0 // +настрой НПС за угощение
	treatSelfMoral = 6.0  // +настрой игроку за совместную кружку
)

func init() {
	core.Register("acquaint", func() core.Component { return &AcquaintC{Known: map[core.ID]bool{}} })
	core.Register("talk", func() core.Component { return &TalkC{} })
	core.Register("codrink", func() core.Component { return &CoDrinkC{} })
}

// AcquaintC — кого игрок знает лично (тогда видно имя). На теле игрока.
type AcquaintC struct {
	Known map[core.ID]bool `json:"known"`
}

func (c *AcquaintC) Kind() string { return "acquaint" }

func Acquaint(e *core.Entity) *AcquaintC { c, _ := e.Get("acquaint").(*AcquaintC); return c }

func ensureAcquaint(w *core.World, e *core.Entity) *AcquaintC {
	if a := Acquaint(e); a != nil {
		if a.Known == nil {
			a.Known = map[core.ID]bool{}
		}
		return a
	}
	a := &AcquaintC{Known: map[core.ID]bool{}}
	w.Attach(e, a)
	return a
}

// Knows — знает ли игрок e этого жителя по имени.
func Knows(e *core.Entity, who core.ID) bool {
	a := Acquaint(e)
	return a != nil && a.Known[who]
}

// TalkC — текущий разговор игрока: с кем, последняя реплика собеседника, стадия.
type TalkC struct {
	With  core.ID `json:"with"`
	Line  string  `json:"line"`  // что сказал собеседник
	Stage string  `json:"stage"` // "" нет разговора | "start" | "known"
}

func (c *TalkC) Kind() string { return "talk" }

func Talk(e *core.Entity) *TalkC { c, _ := e.Get("talk").(*TalkC); return c }

func ensureTalk(w *core.World, e *core.Entity) *TalkC {
	if t := Talk(e); t != nil {
		return t
	}
	t := &TalkC{}
	w.Attach(e, t)
	return t
}

// descFor — обезличенное описание жителя по ремеслу (пока не знаком).
func DescFor(role string) string {
	switch role {
	case "farmer":
		return "пахарь"
	case "fisher":
		return "рыбак"
	case "smith":
		return "кузнец"
	case "hunter":
		return "охотник"
	case "cook":
		return "стряпуха"
	case "tavernkeeper":
		return "корчмарь"
	case "herbalist":
		return "травник"
	case "weaver":
		return "ткач"
	case "miner":
		return "рудокоп"
	case "herder":
		return "пастух"
	case "woodcutter":
		return "дровосек"
	default:
		return "местный"
	}
}

// reachTo — круг общения: у корчмы шире.
func reachFor(p *ScenePosC) float64 {
	tx, ty := poiByKind("tavern")
	if p != nil && math.Hypot(p.X-tx, p.Y-ty) <= tavernReach+2 {
		return talkReachBar
	}
	return talkReach
}

// DialogOption — доступная реплика игрока.
type DialogOption struct {
	ID   string `json:"id"`
	Text string `json:"text"`
}

// talkOptions — какие реплики доступны игроку сейчас (зависит от знакомства и места).
func talkOptions(known, atTavern bool) []DialogOption {
	if !known {
		return []DialogOption{
			{"greet", "Будем знакомы — назовись."},
			{"news", "Как твои дела?"},
			{"bye", "Бывай."},
		}
	}
	opts := []DialogOption{{"news", "Что нового?"}}
	if atTavern {
		opts = append(opts, DialogOption{"drink", "Выпьем по кружке? (угощаю)"})
	}
	return append(opts, DialogOption{"bye", "Бывай."})
}

// TalkOptionsFor — доступные реплики игрока к этому собеседнику сейчас (по знакомству и месту).
func TalkOptionsFor(e, tgt *core.Entity) []DialogOption {
	return talkOptions(Knows(e, tgt.ID), atTavern(e))
}

// StartTalk — заговорить с жителем рядом. Возвращает (ок, причина).
func StartTalk(w *core.World, e *core.Entity, target core.ID) (bool, string) {
	p := ScenePos(e)
	tgt := w.Get(target)
	if p == nil || tgt == nil || core.Mind(tgt) == nil {
		return false, "не с кем говорить"
	}
	tp := ScenePos(tgt)
	if tp == nil || math.Hypot(p.X-tp.X, p.Y-tp.Y) > reachFor(p) {
		return false, "слишком далеко — подойди"
	}
	t := ensureTalk(w, e)
	t.With = target
	if Knows(e, target) {
		t.Stage = "known"
		t.Line = "И тебе поздорову."
	} else {
		t.Stage = "start"
		t.Line = "Чего тебе, добрый человек?"
	}
	return true, ""
}

// Say — игрок выбрал реплику. Двигает разговор; "greet" знакомит (открывает имя), "drink" угощает.
func Say(w *core.World, e *core.Entity, line string) (bool, string) {
	t := Talk(e)
	if t == nil || t.Stage == "" {
		return false, "ты ни с кем не говоришь"
	}
	tgt := w.Get(t.With)
	if tgt == nil {
		t.Stage = ""
		return false, "собеседник ушёл"
	}
	id := core.Ident(tgt)
	switch line {
	case "greet":
		ensureAcquaint(w, e).Known[t.With] = true // познакомились — имя открыто
		t.Stage = "known"
		if id != nil {
			t.Line = "Звать меня " + id.Name + ". А тебя?"
		}
	case "news":
		t.Line = newsLine(tgt)
	case "drink":
		return treatNPC(w, e, tgt)
	case "bye":
		t.Stage = ""
		t.Line = ""
	default:
		return false, "нет такой реплики"
	}
	return true, ""
}

// EndTalk — завершить разговор.
func EndTalk(e *core.Entity) {
	if t := Talk(e); t != nil {
		t.Stage = ""
		t.Line = ""
		t.With = 0
	}
}

// newsLine — что житель отвечает «как дела» — по его занятию/настрою (живая, не скриптовая реплика).
func newsLine(tgt *core.Entity) string {
	m := core.Mind(tgt)
	v := core.Vitals(tgt)
	if m != nil && m.LastAct != "" {
		line := "Да вот, " + m.LastAct + "."
		if v != nil && v.Morale < 35 {
			line += " Тяжко нынче."
		} else if v != nil && v.Morale > 70 {
			line += " Грех жаловаться!"
		}
		return line
	}
	return "Живём помаленьку."
}

// treatNPC — угостить знакомого жителя кружкой в корчме: игрок платит, оба веселеют. Требует знакомства,
// корчмы рядом, пива в запасе и монет. Атомарно; отказ не меняет состояние.
func treatNPC(w *core.World, e, tgt *core.Entity) (bool, string) {
	if !Knows(e, tgt.ID) {
		return false, "сперва познакомься"
	}
	if !atTavern(e) {
		return false, "угощать можно у стойки корчмы"
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return false, "ты не в поселении"
	}
	s := core.Settle(w.Get(pl.Settlement))
	if s == nil || s.Store == nil || s.Store[BeerGood] < 2 {
		return false, "мало пива в корчме"
	}
	pu := core.Purse(e)
	if pu == nil || pu.Coins < BeerPrice*2 {
		return false, "не хватает монет на двоих"
	}
	pu.Coins -= BeerPrice * 2
	s.Store[BeerGood] -= 2
	if tv := core.Vitals(tgt); tv != nil {
		tv.Morale = math.Min(100, tv.Morale+treatMorale)
	}
	if ev := core.Vitals(e); ev != nil {
		ev.Morale = math.Min(100, ev.Morale+treatSelfMoral)
	}
	name := "сосед"
	if id := core.Ident(tgt); id != nil {
		name = id.Name
	}
	if t := Talk(e); t != nil {
		t.Line = "За твоё здоровье! — " + name + " поднимает кружку."
	}
	return true, ""
}

// --- Совместное питьё двух ИГРОКОВ (co-drink): приглашение/согласие у стойки корчмы ---

// CoDrinkC — состояние совместного питья на теле игрока: партнёр и стадия.
type CoDrinkC struct {
	Partner core.ID `json:"partner"`
	State   string  `json:"state"` // "" нет | "out" я позвал | "in" меня зовут | краткий итог см. Talk.Line
}

func (c *CoDrinkC) Kind() string { return "codrink" }

func CoDrink(e *core.Entity) *CoDrinkC { c, _ := e.Get("codrink").(*CoDrinkC); return c }

func ensureCoDrink(w *core.World, e *core.Entity) *CoDrinkC {
	if c := CoDrink(e); c != nil {
		return c
	}
	c := &CoDrinkC{}
	w.Attach(e, c)
	return c
}

// ClearCoDrink снимает приглашение с игрока и его партнёра (на разрыве связи/выходе).
func ClearCoDrink(w *core.World, e *core.Entity) {
	c := CoDrink(e)
	if c == nil || c.State == "" {
		return
	}
	if pe := w.Get(c.Partner); pe != nil {
		if pc := CoDrink(pe); pc != nil && pc.Partner == e.ID {
			pc.State, pc.Partner = "", 0
		}
	}
	c.State, c.Partner = "", 0
}

// InviteCoDrink — позвать ДРУГОГО ИГРОКА выпить. controlled(id) — под управлением ли сессии (игрок).
func InviteCoDrink(w *core.World, e *core.Entity, target core.ID, controlled func(core.ID) bool) (bool, string) {
	if target == e.ID {
		return false, "сам с собой не выпьешь"
	}
	tgt := w.Get(target)
	if tgt == nil || controlled == nil || !controlled(target) {
		return false, "позвать можно только игрока рядом"
	}
	if !atTavern(e) || !atTavern(tgt) {
		return false, "оба должны быть у стойки корчмы"
	}
	p, tp := ScenePos(e), ScenePos(tgt)
	if p == nil || tp == nil || math.Hypot(p.X-tp.X, p.Y-tp.Y) > talkReachBar {
		return false, "подойдите друг к другу"
	}
	mine := ensureCoDrink(w, e)
	theirs := ensureCoDrink(w, tgt)
	if theirs.State == "active" || mine.State == "active" {
		return false, "уже за кружкой"
	}
	mine.Partner, mine.State = target, "out"
	theirs.Partner, theirs.State = e.ID, "in"
	return true, ""
}

// AcceptCoDrink — принять приглашение: оба платят по кружке и пьют вместе. Атомарно.
func AcceptCoDrink(w *core.World, e *core.Entity) (bool, string) {
	c := CoDrink(e)
	if c == nil || c.State != "in" {
		return false, "тебя никто не звал"
	}
	a := w.Get(c.Partner) // пригласивший
	if a == nil {
		ClearCoDrink(w, e)
		return false, "позвавший ушёл"
	}
	if !atTavern(e) || !atTavern(a) {
		return false, "оба должны быть у стойки"
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return false, "не в поселении"
	}
	s := core.Settle(w.Get(pl.Settlement))
	if s == nil || s.Store == nil || s.Store[BeerGood] < 2 {
		return false, "мало пива в корчме"
	}
	pa, pb := core.Purse(a), core.Purse(e)
	if pa == nil || pb == nil || pa.Coins < BeerPrice || pb.Coins < BeerPrice {
		return false, "у кого-то не хватает монет"
	}
	// атомарно: каждый платит за свою кружку, оба веселеют
	pa.Coins -= BeerPrice
	pb.Coins -= BeerPrice
	s.Store[BeerGood] -= 2
	for _, x := range []*core.Entity{a, e} {
		if v := core.Vitals(x); v != nil {
			v.Morale = math.Min(100, v.Morale+treatSelfMoral+treatMorale)
		}
	}
	// знакомим обоих (выпили вместе — теперь знают друг друга)
	ensureAcquaint(w, a).Known[e.ID] = true
	ensureAcquaint(w, e).Known[a.ID] = true
	line := "Вы чокнулись кружками — хорошо посидели!"
	if t := ensureTalk(w, e); t != nil {
		t.Line = line
	}
	if t := ensureTalk(w, a); t != nil {
		t.Line = line
	}
	ClearCoDrink(w, e)
	return true, ""
}
