package sim

import (
	"fmt"
	"math"
	"sort"
	"strings"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Просьбы и задания (v1.10). Канон: ничего из ниоткуда. Задание — это НУЖДА, которая реально есть в
// мире прямо сейчас: амбар пустеет, волки расплодились, кузнецу нечего плавить, сосед голоден или
// ранен. Нужда пропала (кто-то другой помог, зверь ушёл) — пропала и просьба. Награду платят из
// НАСТОЯЩЕГО кошеля просящего (староста/корчмарь — за село), а не из воздуха. Клиент подаёт это
// речью: доска у колодца, слово корчмаря, просьба самого жителя.

func init() {
	core.Register("quests", func() core.Component { return &QuestC{} })
}

// Quest — одна просьба.
type Quest struct {
	ID         string  `json:"id"`
	Kind       string  `json:"kind"` // deliver|cull|feed|heal|cheer
	Via        string  `json:"via"`  // board|keeper|npc — кто и как просит
	Giver      core.ID `json:"-"`
	GiverID    string  `json:"giver"` // id просящего жителя ("" — доска)
	GiverName  string  `json:"giver_name"`
	Settlement core.ID `json:"-"`
	Good       string  `json:"good,omitempty"`
	Species    string  `json:"species,omitempty"`
	Qty        int     `json:"qty"`
	Base       int     `json:"-"` // сколько было убито на момент принятия
	Text       string  `json:"text"`
	Coins      float64 `json:"coins"` // обещанная плата
}

// QuestC — принятые просьбы игрока и его добрая слава по поселениям.
type QuestC struct {
	Active []Quest         `json:"active"`
	Closed []string        `json:"closed"` // недавно закрытые id (чтобы та же нужда не предлагалась тут же)
	Count  int             `json:"count"`
	Rep    map[core.ID]int `json:"rep"`
	Healed map[core.ID]int `json:"healed"` // кого лечил плетением (для просьб «залечи»)
	OpeningErrandDone bool `json:"opening_errand_done,omitempty"` // монотонный факт сдачи вводного поручения
}

func (c *QuestC) Kind() string { return "quests" }

func questsOf(w *core.World, e *core.Entity) *QuestC {
	c, _ := e.Get("quests").(*QuestC)
	if c == nil {
		c = &QuestC{}
		w.Attach(e, c)
	}
	if c.Rep == nil {
		c.Rep = map[core.ID]int{}
	}
	if c.Healed == nil {
		c.Healed = map[core.ID]int{}
	}
	return c
}

func noteHealed(w *core.World, e *core.Entity, who core.ID) { questsOf(w, e).Healed[who]++ }

func (c *QuestC) has(id string) bool {
	for _, q := range c.Active {
		if q.ID == id {
			return true
		}
	}
	for _, x := range c.Closed {
		if x == id {
			return true
		}
	}
	return false
}

var goodRu = map[string]string{
	"wood": "дров", "fish": "рыбы", "meat": "мяса", "ore": "руды", "bread": "хлеба", "herb": "трав",
	"hide": "шкур", "metal": "железа",
}

func nameOf(e *core.Entity, viewer *core.Entity) string {
	id := core.Ident(e)
	if id == nil {
		return "житель"
	}
	if viewer != nil && Knows(viewer, e.ID) {
		return id.Name
	}
	return DescFor(id.Role)
}

// QuestOffers — какие просьбы СЕЙЧАС есть в поселении игрока (из реального состояния мира).
func QuestOffers(w *core.World, e *core.Entity, day int64) []Quest {
	se := settlementEnt(w, e)
	if se == nil || OnRoad(e) {
		return nil
	}
	st := core.Settle(se)
	qc := questsOf(w, e)
	week := day / 7
	res := residentsOf(w, se.ID)
	pop := math.Max(1, float64(len(res)))
	var out []Quest
	add := func(q Quest) {
		q.Settlement = se.ID
		if q.Giver != 0 {
			q.GiverID = eidStr(q.Giver)
		}
		if !qc.has(q.ID) && len(out) < 8 {
			out = append(out, q)
		}
	}
	keeper := w.Get(st.Keeper)
	kname := "староста"
	if keeper != nil {
		kname = nameOf(keeper, e)
	}
	store := st.Store
	if store == nil {
		store = map[string]float64{}
	}
	// 1) село: еда на исходе
	if days := econ.FeedableFood(se) / pop; days < 8 {
		good := []string{"fish", "meat", "fish"}[int(week)%3]
		add(Quest{ID: fmt.Sprintf("food:%d:%d", se.ID, week), Kind: "deliver", Via: "board", Giver: st.Keeper,
			GiverName: kname, Good: good, Qty: 4, Coins: 6,
			Text: fmt.Sprintf("Амбар пустеет, еды на %d дн. Принеси 4 %s в общий амбар.", int(days), goodRu[good])})
	}
	// 2) село: дрова
	if store[econ.GoodWood] < pop*0.6 {
		add(Quest{ID: fmt.Sprintf("wood:%d:%d", se.ID, week), Kind: "deliver", Via: "board", Giver: st.Keeper,
			GiverName: kname, Good: "wood", Qty: 4, Coins: 4,
			Text: "Дровяник пуст, а ночи холодны. Нужно 4 охапки дров."})
	}
	// 3) кузня без руды/железа
	if store[econ.GoodMetal] < 2 && store[econ.GoodOre] < 3 {
		for _, r := range res {
			if id := core.Ident(r); id != nil && id.Role == "smith" {
				add(Quest{ID: fmt.Sprintf("ore:%d:%d", se.ID, week), Kind: "deliver", Via: "npc", Giver: r.ID,
					GiverName: nameOf(r, e), Good: "ore", Qty: 3, Coins: 7,
					Text: "Кузнец: «Горн стынет — плавить нечего. Добудь 3 меры руды на руднике»."})
				break
			}
		}
	}
	// 4) угодья: расплодился опасный зверь
	if fa := battle.Fauna(se); fa != nil {
		for _, sp := range []struct {
			id   string
			at   float64
			n    int
			coin float64
			txt  string
		}{
			{"wolf", 4, 2, 8, "Волки режут скот у самой околицы. Убей двух."},
			{"bear", 2, 1, 12, "Медведь повадился к пасеке. Завали его."},
			{"boar", 6, 2, 6, "Кабаны травят огороды. Убей двух."},
		} {
			if fa.Pop[sp.id] >= sp.at {
				add(Quest{ID: fmt.Sprintf("cull:%s:%d:%d", sp.id, se.ID, week), Kind: "cull", Via: "board",
					Giver: st.Keeper, GiverName: kname, Species: sp.id, Qty: sp.n, Coins: sp.coin, Text: sp.txt})
			}
		}
	}
	// 5) корчма: к столу нечего подать
	if st.Tavern && store[econ.GoodMeat] < 3 {
		add(Quest{ID: fmt.Sprintf("table:%d:%d", se.ID, week), Kind: "deliver", Via: "keeper", Giver: st.Keeper,
			GiverName: kname, Good: "meat", Qty: 2, Coins: 5,
			Text: "Корчмарь: «Гостей кормить нечем. Принеси пару кусков мяса — в долгу не останусь»."})
	}
	// 6) личные нужды жителей: голоден, ранен, тоскует
	sort.Slice(res, func(i, j int) bool { return res[i].ID < res[j].ID })
	personal := 0
	for _, r := range res {
		if personal >= 3 || r.ID == e.ID || Player(r) != nil {
			continue
		}
		v := core.Vitals(r)
		if v == nil || v.Age < 15 {
			continue
		}
		nm := nameOf(r, e)
		switch {
		case battle.Body(r) != nil && battle.Body(r).Severity() > 0.12:
			add(Quest{ID: fmt.Sprintf("heal:%d:%d", r.ID, week), Kind: "heal", Via: "npc", Giver: r.ID, GiverName: nm, Qty: 1, Coins: 3,
				Text: nm + " ранен и мается. Перевяжи его (повязка/снадобье) или залечи плетением."})
			personal++
		case v.Hunger > 72:
			add(Quest{ID: fmt.Sprintf("feed:%d:%d", r.ID, day), Kind: "feed", Via: "npc", Giver: r.ID, GiverName: nm, Qty: 1, Coins: 1,
				Text: nm + " давно не ел. Поделись едой — хлебом, рыбой, мясом."})
			personal++
		case v.Morale < 32 && st.Tavern:
			add(Quest{ID: fmt.Sprintf("cheer:%d:%d", r.ID, week), Kind: "cheer", Via: "npc", Giver: r.ID, GiverName: nm, Qty: 1,
				Text: nm + " совсем пал духом. Угости его кружкой в корчме."})
			personal++
		}
	}
	return out
}

// QuestAccept — взять просьбу (она должна быть среди актуальных предложений).
func QuestAccept(w *core.World, e *core.Entity, id string, day int64) (bool, string) {
	qc := questsOf(w, e)
	if len(qc.Active) >= 5 {
		return false, "слишком много обещаний — сперва выполни взятые"
	}
	for _, q := range QuestOffers(w, e, day) {
		if q.ID == id {
			if q.Kind == "cull" {
				q.Base = ensureCombat(w, e).Kills[q.Species]
			}
			if q.Kind == "heal" {
				q.Base = qc.Healed[q.Giver]
			}
			qc.Active = append(qc.Active, q)
			say(w, e, "quest", "Ты взялся помочь: "+q.Text)
			return true, ""
		}
	}
	return false, "этой просьбы уже нет"
}

// questProgress — сколько сделано и готово ли к сдаче.
func questProgress(w *core.World, e *core.Entity, q Quest) (int, bool) {
	switch q.Kind {
	case "deliver":
		have := int(ensureStashOf(w, e).Goods[q.Good])
		return have, have >= q.Qty
	case "cull":
		n := ensureCombat(w, e).Kills[q.Species] - q.Base
		return n, n >= q.Qty
	case "feed":
		st := ensureStashOf(w, e)
		for _, g := range []string{"bread", "fish", "meat", "game", "veg", "cheese"} {
			if st.Goods[g] >= 1 {
				return 1, true
			}
		}
		if Inv(e) != nil && (Inv(e).Items["stew"] > 0 || Inv(e).Items["roast"] > 0) {
			return 1, true
		}
		return 0, false
	case "heal":
		if questsOf(w, e).Healed[q.Giver] > q.Base {
			return 1, true
		}
		if i := Inv(e); i != nil && (i.Items["bandage"] > 0 || i.Items["potion"] > 0) {
			return 1, true
		}
		return 0, false
	case "cheer":
		pu := core.Purse(e)
		ok := pu != nil && pu.Coins >= BeerPrice*2
		return 0, ok
	}
	return 0, false
}

// QuestTurnIn — сдать просьбу: проверяет РЕАЛЬНОЕ выполнение, переносит товар, платит из кошеля просящего.
func QuestTurnIn(w *core.World, e *core.Entity, id string) (bool, string) {
	qc := questsOf(w, e)
	for i, q := range qc.Active {
		if q.ID != id {
			continue
		}
		pl := core.Place(e)
		if pl == nil || pl.Settlement != q.Settlement || OnRoad(e) {
			return false, "сдать можно там, где просили"
		}
		if _, ready := questProgress(w, e, q); !ready {
			return false, "ещё не выполнено"
		}
		giver := w.Get(q.Giver)
		se := w.Get(q.Settlement)
		st := ensureStashOf(w, e)
		switch q.Kind {
		case "deliver":
			st.Goods[q.Good] -= float64(q.Qty)
			if q.Via == "npc" && giver != nil {
				addStash(w, giver, q.Good, float64(q.Qty)) // личная просьба: добро ложится в закрома просившего
			} else if s := core.Settle(se); s != nil {
				if s.Store == nil {
					s.Store = map[string]float64{}
				}
				s.Store[q.Good] += float64(q.Qty) // товар реально лёг на склад села
			}
		case "feed":
			if giver == nil {
				break
			}
			fed := false
			for _, g := range []string{"bread", "fish", "meat", "game", "veg", "cheese"} {
				if st.Goods[g] >= 1 {
					st.Goods[g]--
					fed = true
					break
				}
			}
			if inv := Inv(e); !fed && inv != nil {
				for _, it := range []string{"stew", "roast"} {
					if inv.Items[it] > 0 {
						inv.Items[it]--
						break
					}
				}
			}
			if v := core.Vitals(giver); v != nil {
				v.Hunger = math.Max(0, v.Hunger-45)
				v.Morale = math.Min(100, v.Morale+8)
			}
		case "heal":
			if giver == nil {
				break
			}
			if qc.Healed[q.Giver] <= q.Base { // не плетением — значит повязкой/снадобьем
				inv := ensureInv(w, e)
				if inv.Items["bandage"] > 0 {
					inv.Items["bandage"]--
				} else {
					inv.Items["potion"]--
				}
				if b := battle.Body(giver); b != nil {
					b.Mend(14)
					b.Bleeding = 0
				}
				if v := core.Vitals(giver); v != nil {
					v.Health = math.Min(100, v.Health+15)
				}
			}
		case "cheer":
			if giver == nil {
				break
			}
			s := core.Settle(se)
			pu := core.Purse(e)
			if s == nil || s.Store[BeerGood] < 2 {
				return false, "в корчме нет пива"
			}
			pu.Coins -= BeerPrice * 2
			s.Store[BeerGood] -= 2
			if v := core.Vitals(giver); v != nil {
				v.Morale = math.Min(100, v.Morale+20)
			}
			EnsurePlayer(w, e).DrankBeer = true
			fireSocial(w, e, "treat", giver.ID)
		}
		// плата — из НАСТОЯЩЕГО кошеля просящего (сколько есть)
		paid := 0.0
		if giver != nil && q.Coins > 0 {
			if gp := core.Purse(giver); gp != nil && gp.Coins > 0 {
				paid = math.Min(q.Coins, math.Floor(gp.Coins))
				gp.Coins -= paid
			}
		}
		if pu := core.Purse(e); pu != nil {
			pu.Coins += paid
		}
		if giver != nil && Player(giver) == nil { // благодарный житель называет себя
			a := ensureAcquaint(w, e)
			a.Known[giver.ID] = true
		}
		qc.Rep[q.Settlement]++
		qc.Count++
		if strings.HasPrefix(q.ID, "errand:") {
			qc.OpeningErrandDone = true
		}
		qc.Closed = append(qc.Closed, q.ID)
		if len(qc.Closed) > 24 {
			qc.Closed = qc.Closed[len(qc.Closed)-24:]
		}
		qc.Active = append(qc.Active[:i], qc.Active[i+1:]...)
		thanks := "Спасибо тебе."
		if paid > 0 {
			thanks = fmt.Sprintf("Тебе отсчитали %d монет.", int(paid))
		} else if q.Coins > 0 {
			thanks = "Платить нечем — но добро здесь помнят."
		}
		say(w, e, "quest", "Просьба выполнена. "+thanks)
		return true, ""
	}
	return false, "нет такой просьбы"
}

// QuestDrop — отказаться от взятой просьбы.
func QuestDrop(w *core.World, e *core.Entity, id string) (bool, string) {
	qc := questsOf(w, e)
	for i, q := range qc.Active {
		if q.ID == id {
			qc.Active = append(qc.Active[:i], qc.Active[i+1:]...)
			return true, ""
		}
	}
	return false, "нет такой просьбы"
}

// QuestsView — просьбы для кадра (me.quests): что предлагают здесь, что взято (с прогрессом), слава.
func QuestsView(w *core.World, e *core.Entity, day int64) map[string]any {
	qc := questsOf(w, e)
	offers := QuestOffers(w, e, day)
	if offers == nil {
		offers = []Quest{}
	}
	active := []map[string]any{}
	for _, q := range qc.Active {
		n, ready := questProgress(w, e, q)
		active = append(active, map[string]any{"id": q.ID, "kind": q.Kind, "via": q.Via, "giver": q.GiverID,
			"giver_name": q.GiverName, "text": q.Text, "good": q.Good, "species": q.Species,
			"qty": q.Qty, "have": n, "ready": ready, "coins": q.Coins})
	}
	rep := 0
	if pl := core.Place(e); pl != nil {
		rep = qc.Rep[pl.Settlement]
	}
	return map[string]any{"offers": offers, "active": active, "done": qc.Count, "rep": rep}
}
