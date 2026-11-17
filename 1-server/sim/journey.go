package sim

import (
	"fmt"
	"math"
	"sort"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Дорога и отряд (v1.10). Канон: путь — это риск и время, а не телепорт. Игрок выходит на тракт,
// идёт (реальное время), и дорога отвечает встречами: засада лихих людей, зверь, путник, привал,
// находка. Каждая встреча ждёт РЕШЕНИЯ игрока (драться/откупиться/бежать…). С друзьями идут вместе:
// отряд выходит разом, в драке плечом к плечу. Между демо-землями и внешним миром по-прежнему горы —
// туда только через врата (op:"travel", ADR 0003).

func init() {
	core.Register("journey", func() core.Component { return &JourneyC{} })
	core.Register("party", func() core.Component { return &PartyC{} })
}

// RoadEvent — встреча в пути.
type RoadEvent struct {
	At      float64  `json:"at"` // на какой доле пути
	Kind    string   `json:"kind"`
	Foe     string   `json:"foe,omitempty"`
	Text    string   `json:"text"`
	Options []string `json:"options"`
}

// JourneyC — идущее путешествие.
type JourneyC struct {
	From    core.ID     `json:"from"`
	To      core.ID     `json:"to"`
	Total   float64     `json:"total"` // сек пути
	Done    float64     `json:"done"`
	Lead    core.ID     `json:"lead"` // 0 — сам ведёт; иначе идёт за вожаком отряда
	Plan    []RoadEvent `json:"plan"`
	Pending *RoadEvent  `json:"pending"`
	Note    string      `json:"note"` // чем кончилась последняя встреча
	Via     []string    `json:"via"`  // сёла, через которые лежит тракт
}

func (c *JourneyC) Kind() string { return "journey" }

func journeyOf(e *core.Entity) *JourneyC { c, _ := e.Get("journey").(*JourneyC); return c }

// OnRoad — в пути ли сущность (в сцене поселения её в это время нет).
func OnRoad(e *core.Entity) bool { return e != nil && journeyOf(e) != nil }

// PartyC — отряд: кто вожак и от кого висит приглашение.
type PartyC struct {
	Leader     core.ID `json:"leader"` // 0 — не в отряде
	InviteFrom core.ID `json:"invite_from"`
}

func (c *PartyC) Kind() string { return "party" }

func partyOf(w *core.World, e *core.Entity) *PartyC {
	c, _ := e.Get("party").(*PartyC)
	if c == nil {
		c = &PartyC{}
		w.Attach(e, c)
	}
	return c
}

// PartyMembers — все члены отряда игрока (включая его самого); один он — если отряда нет.
func PartyMembers(w *core.World, e *core.Entity) []*core.Entity {
	c, _ := e.Get("party").(*PartyC)
	if c == nil || c.Leader == 0 {
		return []*core.Entity{e}
	}
	var out []*core.Entity
	w.Each("party", func(o *core.Entity) {
		if oc, _ := o.Get("party").(*PartyC); oc != nil && oc.Leader == c.Leader {
			out = append(out, o)
		}
	})
	sort.Slice(out, func(i, j int) bool { return out[i].ID < out[j].ID })
	return out
}

// PartyInvite — позвать другого ИГРОКА в отряд (оба в одном поселении).
func PartyInvite(w *core.World, e *core.Entity, target core.ID, controlled func(core.ID) bool) (bool, string) {
	t := w.Get(target)
	if t == nil || t.ID == e.ID || controlled == nil || !controlled(target) {
		return false, "в отряд зовут живого игрока"
	}
	a, b := core.Place(e), core.Place(t)
	if a == nil || b == nil || a.Settlement != b.Settlement || OnRoad(e) || OnRoad(t) {
		return false, "он не здесь"
	}
	tc := partyOf(w, t)
	if tc.Leader != 0 {
		return false, "он уже в отряде"
	}
	tc.InviteFrom = e.ID
	say(w, t, "party", "Тебя зовут в отряд.")
	return true, ""
}

// PartyAccept — принять приглашение в отряд.
func PartyAccept(w *core.World, e *core.Entity) (bool, string) {
	c := partyOf(w, e)
	from := w.Get(c.InviteFrom)
	if from == nil {
		c.InviteFrom = 0
		return false, "приглашения нет"
	}
	fc := partyOf(w, from)
	if fc.Leader == 0 {
		fc.Leader = from.ID
	}
	if len(PartyMembers(w, from)) >= 6 {
		return false, "отряд полон"
	}
	c.Leader, c.InviteFrom = fc.Leader, 0
	say(w, from, "party", "В отряде пополнение.")
	say(w, e, "party", "Ты в отряде.")
	return true, ""
}

// PartyLeave — выйти из отряда (вожак уходит — отряд распадается).
func PartyLeave(w *core.World, e *core.Entity) {
	c, _ := e.Get("party").(*PartyC)
	if c == nil {
		return
	}
	c.InviteFrom = 0
	if c.Leader == 0 {
		return
	}
	lead := c.Leader
	members := PartyMembers(w, e)
	c.Leader = 0
	if lead == e.ID || len(members) <= 2 {
		for _, m := range members {
			if mc, _ := m.Get("party").(*PartyC); mc != nil {
				mc.Leader = 0
			}
		}
	}
}

// PartyView — отряд для кадра (me.party).
func PartyView(w *core.World, e *core.Entity) map[string]any {
	c := partyOf(w, e)
	members := []map[string]any{}
	if c.Leader != 0 {
		for _, m := range PartyMembers(w, e) {
			if m.ID == e.ID {
				continue
			}
			row := map[string]any{"id": eidStr(m.ID), "name": nameOf(m, e), "leader": m.ID == c.Leader, "road": OnRoad(m)}
			if v := core.Vitals(m); v != nil {
				row["health"] = math.Round(v.Health)
			}
			if p := ScenePos(m); p != nil {
				row["x"], row["y"] = round1x(p.X), round1x(p.Y)
			}
			members = append(members, row)
		}
	}
	var inv any
	if from := w.Get(c.InviteFrom); from != nil {
		inv = map[string]any{"from": eidStr(from.ID), "who": nameOf(from, e)}
	}
	lead := ""
	if c.Leader != 0 {
		lead = eidStr(c.Leader)
	}
	return map[string]any{"leader": lead, "members": members, "invite_from": inv}
}

// --- путь ---

// JourneyStart — выйти в путь к поселению. Отряд вожака выходит вместе с ним.
func JourneyStart(w *core.World, e *core.Entity, to core.ID, reqid string, s *dice.Stream) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) {
		return false, "ты уже в пути"
	}
	pl := core.Place(e)
	dst := w.Get(to)
	if pl == nil || dst == nil || core.Settle(dst) == nil || pl.Settlement == to {
		return false, "туда дороги нет"
	}
	src := w.Get(pl.Settlement)
	if src == nil || core.Settle(src) == nil {
		return false, "ты не в поселении"
	}
	a, b := core.Settle(src), core.Settle(dst)
	if isOuter(w, a) != isOuter(w, b) {
		return false, "между этими землями горы — только через врата"
	}
	if pc2, _ := e.Get("party").(*PartyC); pc2 != nil && pc2.Leader != 0 && pc2.Leader != e.ID {
		return false, "отряд ведёт вожак"
	}
	if v := core.Vitals(e); v == nil || v.Energy < 10 {
		return false, "нет сил на дорогу — отдохни"
	}
	// путь идёт ПО ТРАКТАМ: через промежуточные сёла, длина — сумма перегонов
	roads := RoadGraph(w, isOuter(w, a))
	route, dist := RoadRoute(roads, pl.Settlement, to)
	if len(route) < 2 {
		return false, "туда нет дороги"
	}
	if !knowsPlace(e, to) && len(route) > 2 {
		return false, "ты не знаешь туда дороги — расспроси людей"
	}
	total := math.Max(24, math.Min(110, 18+dist*0.35))
	if _, h := horseOf(w, e); h != nil && Mounted(e) { // верхом по тракту быстрее, лошадь устаёт
		total *= 0.6
		h.Stam = math.Max(0, h.Stam-25)
	}
	via := []string{}
	for _, id := range route[1 : len(route)-1] {
		via = append(via, core.Settle(w.Get(id)).Name)
	}
	n := 1 + s.Intn(3)
	plan := make([]RoadEvent, 0, n)
	for i := 0; i < n; i++ {
		ev := RoadEvent{At: (float64(i) + s.Uniform(0.35, 0.8)) / float64(n)}
		switch s.Weighted([]float64{0.24, 0.2, 0.22, 0.16, 0.18}) {
		case 0:
			ev.Kind, ev.Foe = "ambush", "bandit"
			ev.Text = "Из-за поваленной сосны выходят двое с дубьём: «Кошель или жизнь»."
			ev.Options = []string{"fight", "pay", "flee"}
		case 1:
			ev.Kind, ev.Foe = "beast", []string{"wolf", "boar", "wolf"}[s.Intn(3)]
			ev.Text = "На тропе, загораживая путь, стоит " + battle.Beasts[ev.Foe].Name + " и не думает уходить."
			ev.Options = []string{"fight", "scare", "flee"}
		case 2:
			ev.Kind = "traveller"
			ev.Text = "Навстречу бредёт путник с котомкой, приветливо поднимает руку."
			ev.Options = []string{"talk", "trade", "pass"}
		case 3:
			ev.Kind = "camp"
			ev.Text = "У дороги тлеет чей-то брошенный костёр, рядом сухой лапник."
			ev.Options = []string{"rest", "pass"}
		default:
			ev.Kind = "find"
			ev.Text = "В стороне от тропы — поляна, полная трав и ягод."
			ev.Options = []string{"gather", "pass"}
		}
		plan = append(plan, ev)
	}
	for _, m := range PartyMembers(w, e) {
		mp := core.Place(m)
		if m.ID != e.ID && (mp == nil || mp.Settlement != pl.Settlement || OnRoad(m)) {
			continue // кто не рядом — остаётся
		}
		j := &JourneyC{From: pl.Settlement, To: to, Total: total, Via: via}
		if m.ID == e.ID {
			j.Plan = plan
		} else {
			j.Lead = e.ID
		}
		w.Attach(m, j)
		EndTalk(m)
		if c := Combat(m); c != nil {
			c.Guard, c.Sneak = false, false
		}
		say(w, m, "road", "Ты вышел на тракт: путь лежит в "+b.Name+".")
	}
	pc.markDone(reqid)
	return true, ""
}

// StepJourneys продвигает всех путников на dt секунд реального времени.
func StepJourneys(w *core.World, dt float64) {
	if w == nil || dt <= 0 {
		return
	}
	var leads, follows []*core.Entity
	w.Each("journey", func(e *core.Entity) {
		if journeyOf(e).Lead == 0 {
			leads = append(leads, e)
		} else {
			follows = append(follows, e)
		}
	})
	for _, e := range leads {
		j := journeyOf(e)
		if j.Pending != nil {
			continue // дорога ждёт решения
		}
		j.Done += dt
		if len(j.Plan) > 0 && j.Done/j.Total >= j.Plan[0].At {
			ev := j.Plan[0]
			j.Plan = j.Plan[1:]
			j.Pending = &ev
			say(w, e, "road", ev.Text)
			continue
		}
		if j.Done >= j.Total {
			arrive(w, e, j)
		}
	}
	for _, e := range follows {
		j := journeyOf(e)
		lead := w.Get(j.Lead)
		lj := journeyOf(lead)
		if lead == nil || lj == nil { // вожак дошёл/пропал — доходим следом
			arrive(w, e, j)
			continue
		}
		j.Done, j.Total, j.Pending, j.Note = lj.Done, lj.Total, lj.Pending, lj.Note
	}
}

func arrive(w *core.World, e *core.Entity, j *JourneyC) {
	w.Detach(e, "journey")
	pl := core.Place(e)
	dst := w.Get(j.To)
	if pl == nil || dst == nil || core.Settle(dst) == nil {
		return
	}
	pl.Settlement = j.To
	if _, h := horseOf(w, e); h != nil && Mounted(e) {
		h.Settlement = j.To // лошадь приходит вместе со всадником; оставленная — ждёт, где оставили
	}
	LearnVisited(w, e, j.To)
	GoTo(w, e, "well")
	if x, y, ok := arriveSpot(w, j.To, j.From); ok { // входишь с той стороны, откуда пришёл
		p := EnsureScenePos(w, e)
		p.X, p.Y = x, y
		p.Facing = facingDeg(-x, -y, p.Facing)
	}
	if v := core.Vitals(e); v != nil {
		v.Energy = math.Max(0, v.Energy-8)
		TrainStat(w, e, "endurance", 0.3)
		v.Hunger = math.Min(100, v.Hunger+6)
	}
	say(w, e, "road", "Дорога позади: ты в поселении "+core.Settle(dst).Name+".")
	OpenFrontier(w, e, j.To) // внешний мир растёт туда, куда дошёл игрок
}

// JourneyChoose — решение игрока на встрече в пути.
func JourneyChoose(w *core.World, e *core.Entity, choice string, s *dice.Stream) (bool, string) {
	j := journeyOf(e)
	if j == nil || j.Pending == nil {
		return false, "выбирать нечего"
	}
	if j.Lead != 0 {
		return false, "решает вожак отряда"
	}
	ev := j.Pending
	okc := false
	for _, o := range ev.Options {
		okc = okc || o == choice
	}
	if !okc {
		return false, "так здесь не поступить"
	}
	v := core.Vitals(e)
	pu := core.Purse(e)
	inv := ensureInv(w, e)
	st := ensureStashOf(w, e)
	party := 0
	w.Each("journey", func(o *core.Entity) {
		if journeyOf(o).Lead == e.ID {
			party++
		}
	})
	note := ""
	switch choice {
	case "fight":
		ws, ok := weapons[inv.Hand]
		if !ok {
			ws = weapons[""]
		}
		q := 0.35 + ws.dmg/40 + 0.15*float64(party)
		out := battle.Resolve(skillOf(e, ws.skill)+skillOf(e, "hunter")*0.5, q, ev.Foe, battle.Body(e), s)
		if v != nil {
			v.Health = math.Max(6, v.Health-out.Hurt)
		}
		trainSkill(w, e, ws.skill, 0.8)
		if out.Win {
			ensureCombat(w, e).Kills[ev.Foe]++
			if ev.Foe == "bandit" {
				gain := float64(2 + s.Intn(5))
				if pu != nil {
					pu.Coins += gain
				}
				note = fmt.Sprintf("Ты отбился. У лихих людей нашлось %d монет.", int(gain))
			} else {
				st.Goods[econ.GoodMeat] += math.Max(1, math.Round(out.Meat/4))
				st.Goods[econ.GoodHide] += math.Ceil(out.Hide / 2)
				note = "Зверь повержен, добыча в суме."
			}
		} else {
			if ev.Foe == "bandit" && pu != nil {
				pu.Coins = math.Floor(pu.Coins * 0.7)
			}
			note = "Тебя потрепали — еле унёс ноги."
		}
		if out.Hurt > 0.5 {
			note += " Ранен: " + partRu(out.Part) + "."
		}
	case "pay":
		pay := 0.0
		if pu != nil {
			pay = math.Min(pu.Coins, 5)
			pu.Coins -= pay
		}
		note = fmt.Sprintf("Ты отсчитал %d монет, и тебя пропустили.", int(pay))
	case "flee":
		p := ScenePos(e)
		ch := 0.45
		if p != nil {
			ch += 0.3 * p.Stam
		}
		if s.Chance(ch) {
			j.Done = math.Max(0, j.Done-j.Total*0.08)
			note = "Ты рванул в чащу и ушёл, дав крюк."
		} else {
			if v != nil {
				v.Health = math.Max(6, v.Health-8)
			}
			if b := battle.Body(e); b != nil {
				b.HurtPart("leg", 4)
			}
			if ev.Foe == "bandit" && pu != nil {
				pu.Coins = math.Floor(pu.Coins * 0.85)
			}
			note = "Уйти чисто не вышло: тебя достали в спину."
		}
	case "scare":
		ch := 0.4
		if inv.Hand == "torch" || inv.Hand == "lantern" {
			ch = 0.95
		}
		if c := Combat(e); c != nil && c.Light > 0 {
			ch = 0.95
		}
		if s.Chance(ch) {
			note = "Зверь попятился от огня и крика и ушёл с тропы."
		} else {
			if v != nil {
				v.Health = math.Max(6, v.Health-7)
			}
			note = "Зверь не испугался — цапнул и только потом отступил."
		}
	case "talk":
		var names []core.ID
		w.Each("settlement", func(se *core.Entity) {
			if k := Knowledge(e); k == nil || !k.Places[se.ID].Known && !k.Places[se.ID].Visited {
				names = append(names, se.ID)
			}
		})
		sort.Slice(names, func(a, b int) bool { return names[a] < names[b] })
		if len(names) > 0 {
			id := names[s.Intn(len(names))]
			LearnKnown(w, e, id)
			note = "Путник рассказал о месте под названием " + core.Settle(w.Get(id)).Name + "."
		} else {
			note = "Путник делится дорожными байками. Нового ты не узнал, но идти веселее."
		}
		if v != nil {
			v.Morale = math.Min(100, v.Morale+3)
		}
	case "trade":
		if pu != nil && pu.Coins >= 1 {
			pu.Coins--
			st.Goods[econ.GoodBread] += 2
			note = "За монету путник отдал две краюхи хлеба."
		} else {
			note = "Платить нечем — путник пожал плечами."
		}
	case "rest":
		if v != nil {
			v.Energy = math.Min(100, v.Energy+25)
		}
		j.Total += 6
		note = "Ты посидел у огня. Ноги отошли."
	case "gather":
		st.Goods[econ.GoodHerb] += float64(1 + s.Intn(2))
		trainSkill(w, e, "herbalist", 0.4)
		note = "Ты набрал трав в дорогу."
	case "pass":
		note = "Ты прошёл мимо."
	}
	j.Pending, j.Note = nil, note
	say(w, e, "road", note)
	return true, ""
}

var roadScenes = [][]string{
	{"Околица позади. Тракт уходит в поля, колея полна вчерашней воды.", "Собаки отбрехали вслед и затихли. Впереди — только дорога."},
	{"Лес сомкнулся над трактом. Пахнет прелью и смолой, где-то стучит дятел.", "Тропа идёт вдоль оврага; внизу, в тумане, шумит ручей.", "Ветер гонит по небу рваные облака. Вороны кружат над дальним полем."},
	{"За холмом показались дымы — жильё близко.", "Запахло дымом и хлебом. Дорога пошла под уклон, к крышам."},
}

// JourneyView — путь для кадра (me.journey): куда, сколько пройдено, что вокруг, встреча и варианты.
func JourneyView(w *core.World, e *core.Entity, hour int, season string) any {
	j := journeyOf(e)
	if j == nil {
		return nil
	}
	dst := core.Settle(w.Get(j.To))
	name := ""
	if dst != nil {
		name = dst.Name
	}
	prog := 0.0
	if j.Total > 0 {
		prog = math.Min(1, j.Done/j.Total)
	}
	third := int(prog * 3)
	if third > 2 {
		third = 2
	}
	lines := roadScenes[third]
	scene := lines[int(uint64(j.To)+uint64(e.ID))%len(lines)]
	switch {
	case hour >= 22 || hour < 5:
		scene += " Ночь; дорогу едва видно."
	case hour < 8:
		scene += " Светает, трава в росе."
	case hour >= 19:
		scene += " Смеркается."
	}
	if season == "зима" {
		scene += " Снег скрипит под ногами."
	}
	out := map[string]any{"to": eidStr(j.To), "to_name": name, "progress": math.Round(prog*100) / 100,
		"eta": math.Round(math.Max(0, j.Total-j.Done)), "scene": scene, "note": j.Note,
		"follow": j.Lead != 0, "event": nil, "via": append([]string{}, j.Via...)}
	if j.Pending != nil {
		out["event"] = map[string]any{"kind": j.Pending.Kind, "foe": j.Pending.Foe, "text": j.Pending.Text, "options": j.Pending.Options}
	}
	return out
}
