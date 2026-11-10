package sim

import (
	"math"
	"strings"

	"pogranichye/arcane"
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
)

// Магия плетениями (v1.10). Канон: плетение — узор, который маг удерживает в уме, пропуская сквозь
// него ману. Стихий нет; предел — СЛОЖНОСТЬ, которую ум способен удержать (arcane.Capacity).
// Сорванное плетение бьёт по самому магу. Узор ЛОГИЧЕН: действие выводится из состава узлов, а не из
// таблицы — понявший грамматику сочиняет свои плетения. Знакомая схема держится увереннее; свободное
// плетение — риск.
//
// Грамматика (игрок собирает узор кнопками/мышью из шести узлов):
//   knot   узел      — источник, держит ману: сила плетения
//   thread нить      — несёт плетение вдаль: дальность
//   loop   петля     — удерживает: щит; с нитью — заживление; с шипом — жжёт после удара
//   fork   развилка  — делит: ещё одна цель / на отряд
//   barb   шип       — ранит
//   seal   печать    — замыкает узор
// Узор обязан начинаться узлом и кончаться печатью — иначе он «не замкнут» и рвётся гораздо чаще.

func init() {
	core.Register("grimoire", func() core.Component { return &GrimoireC{} })
}

// WeaveNodeInfo — описание узла для клиента (кнопки сборки плетения).
type WeaveNodeInfo struct {
	ID   string `json:"id"`
	Name string `json:"name"`
	Hint string `json:"hint"`
}

var WeaveNodes = []WeaveNodeInfo{
	{"knot", "узел", "источник: сила плетения"},
	{"thread", "нить", "несёт вдаль: дальность"},
	{"loop", "петля", "удерживает: щит, заживление, жжение"},
	{"fork", "развилка", "делит на цели/отряд"},
	{"barb", "шип", "ранит"},
	{"seal", "печать", "замыкает узор"},
}

const weaveMaxLen = 12

// Schema — схема плетения (известная магу или из общего свода).
type Schema struct {
	Name  string   `json:"name"`
	Nodes []string `json:"nodes"`
}

// свод именованных схем: их можно ОТКРЫТЬ свободным плетением (совпал узор — запомнил под именем).
var schemaLore = []Schema{
	{"Светоч", []string{"knot", "seal"}},
	{"Искра", []string{"knot", "barb", "seal"}},
	{"Толчок", []string{"knot", "thread", "seal"}},
	{"Щит", []string{"knot", "loop", "seal"}},
	{"Стрела", []string{"knot", "thread", "barb", "seal"}},
	{"Заплата", []string{"knot", "loop", "thread", "seal"}},
	{"Жгучая плеть", []string{"knot", "loop", "barb", "seal"}},
	{"Цепь", []string{"knot", "fork", "barb", "barb", "seal"}},
	{"Круг", []string{"knot", "loop", "loop", "fork", "seal"}},
	{"Копьё", []string{"knot", "knot", "thread", "thread", "barb", "seal"}},
}

// GrimoireC — что маг знает наизусть (знакомые схемы держатся увереннее) и итог последнего плетения.
type GrimoireC struct {
	Known []Schema `json:"known"`
	Own   int      `json:"own"` // сколько своих безымянных узоров придумал
}

func (c *GrimoireC) Kind() string { return "grimoire" }

func grimoire(w *core.World, e *core.Entity) *GrimoireC {
	if c, _ := e.Get("grimoire").(*GrimoireC); c != nil {
		return c
	}
	c := &GrimoireC{Known: []Schema{schemaLore[0], schemaLore[1]}} // с рождения: Светоч и Искра
	w.Attach(e, c)
	return c
}

func sameNodes(a, b []string) bool { return strings.Join(a, ",") == strings.Join(b, ",") }

func (c *GrimoireC) knows(nodes []string) (string, bool) {
	for _, k := range c.Known {
		if sameNodes(k.Nodes, nodes) {
			return k.Name, true
		}
	}
	return "", false
}

// WeaveSpec — что ДЕЛАЕТ узор (выведено из состава).
type WeaveSpec struct {
	Kind       string  // light|push|ward|mend|harm
	Power      float64 // сила (урон/щит/лечение)
	Range      float64 // м
	Targets    int
	Linger     float64 // жжение, урон/с
	Complexity float64
	Cost       float64 // маны
	Closed     bool    // замкнут ли (узел…печать)
}

// ReadWeave выводит действие плетения из состава узлов. ok=false — в узоре неизвестный узел/он пуст.
func ReadWeave(nodes []string) (WeaveSpec, bool) {
	if len(nodes) == 0 || len(nodes) > weaveMaxLen {
		return WeaveSpec{}, false
	}
	n := map[string]float64{}
	for _, x := range nodes {
		switch x {
		case "knot", "thread", "loop", "fork", "barb", "seal":
			n[x]++
		default:
			return WeaveSpec{}, false
		}
	}
	sp := WeaveSpec{
		Closed:     nodes[0] == "knot" && nodes[len(nodes)-1] == "seal" && n["seal"] == 1,
		Complexity: float64(len(nodes)) + n["fork"],
		Cost:       4*float64(len(nodes)) + 4*n["knot"],
		Targets:    1 + int(n["fork"]),
	}
	if !sp.Closed {
		sp.Complexity += 3 // незамкнутый узор расползается в уме
	}
	k := math.Max(1, n["knot"])
	switch {
	case n["barb"] > 0:
		sp.Kind = "harm"
		sp.Power = 8*k + 6*n["barb"]
		sp.Range = 4 + 9*n["thread"]
		sp.Linger = 2 * n["loop"]
	case n["loop"] > 0 && n["thread"] > 0:
		sp.Kind = "mend"
		sp.Power = 7*n["loop"] + 3*k
		sp.Range = 3 + 6*n["thread"]
	case n["loop"] > 0:
		sp.Kind = "ward"
		sp.Power = 12*n["loop"] + 5*k
	case n["thread"] > 0:
		sp.Kind = "push"
		sp.Power = 1.5 * n["thread"]
		sp.Range = 4 + 6*n["thread"]
	default:
		sp.Kind = "light"
		sp.Power = 90 * k
	}
	return sp, true
}

// WeaveCast — сплести узор. target — id врага/человека (0 — сервер выберет ближайшую подходящую цель).
// Исход: удержал ли ум узор (arcane.Weave по сложности и навыку) → действие; сорвал → откат по магу.
func WeaveCast(w *core.World, e *core.Entity, nodes []string, target core.ID, reqid string, s *dice.Stream) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) {
		return false, "в дороге не до плетений"
	}
	sp, ok := ReadWeave(nodes)
	if !ok {
		return false, "узор не читается"
	}
	p := ScenePos(e)
	if p == nil {
		return false, "нет тела в сцене"
	}
	if pc.Mana < sp.Cost {
		return false, "не хватает маны"
	}
	if ok, why := weaveGate(e, sp.Complexity); !ok {
		return false, why
	}
	g := grimoire(w, e)
	c := ensureCombat(w, e)
	skill := skillOf(e, "mage")
	name, known := g.knows(nodes)
	eff := skill + (Stat(e, "intellect")-statBase)*0.8 - drunkPenalty(e)*40 // ум держит узор; хмель мешает
	if known {
		eff += 12 // знакомый узор держится увереннее
	}
	pc.Mana -= sp.Cost
	pc.markDone(reqid)
	trainSkill(w, e, "mage", 0.2+0.05*sp.Complexity)
	hold := arcane.MageC{Mana: sp.Complexity}
	held, backfire := hold.Weave(sp.Complexity, eff, s)
	if !held {
		hurt := backfire * 1.6 / statMod(e, "will", 0.15)
		TrainStat(w, e, "will", 0.08)
		part := "arm"
		if sp.Complexity > arcane.Capacity(skill)+2 {
			part = "head" // перенапряг ум
		}
		if b := battle.Body(e); b != nil {
			b.HurtPart(part, hurt*0.6)
		}
		if v := core.Vitals(e); v != nil {
			v.Health = math.Max(0, v.Health-hurt)
			if v.Health <= 5 {
				knockOut(w, e)
			}
		}
		Emit(w, e, FeedItem{Kind: "backfire", Text: "Плетение сорвалось и ударило по тебе.", Part: part, Dmg: math.Round(hurt)})
		return false, "плетение сорвалось"
	}
	if !known { // удержал незнакомый узор — запомнил
		name = ""
		for _, l := range schemaLore {
			if sameNodes(l.Nodes, nodes) {
				name = l.Name
			}
		}
		if name == "" {
			g.Own++
			name = "Свой узор " + roman(g.Own)
		}
		if len(g.Known) < 16 {
			g.Known = append(g.Known, Schema{Name: name, Nodes: append([]string{}, nodes...)})
			say(w, e, "learn", "Ты запомнил плетение: "+name+".")
		}
	}
	c.Casting, c.CastGlow = name, 3
	power := sp.Power * (1 + skill/60)
	switch sp.Kind {
	case "light":
		c.Light = math.Max(c.Light, power)
		if v := core.Vitals(e); v != nil {
			v.Morale = math.Min(100, v.Morale+1)
		}
		say(w, e, "weave", name+": над ладонью повис ровный свет.")
	case "ward":
		give := func(t *core.Entity) {
			tc := ensureCombat(w, t)
			tc.Shield, tc.ShieldLeft = math.Min(70, tc.Shield+power), 40
		}
		give(e)
		if sp.Targets > 1 { // развилка — щит и на отряд рядом
			for _, m := range PartyMembers(w, e) {
				if mp := ScenePos(m); m.ID != e.ID && mp != nil && math.Hypot(mp.X-p.X, mp.Y-p.Y) <= 12 {
					give(m)
					say(w, m, "weave", "Тебя укрыл чужой щит-плетение.")
				}
			}
		}
		say(w, e, "weave", name+": воздух вокруг тебя уплотнился щитом.")
	case "mend":
		t := e
		if target != 0 && target != e.ID {
			if o := w.Get(target); o != nil && core.Vitals(o) != nil {
				op, pl, opl := ScenePos(o), core.Place(e), core.Place(o)
				if pl == nil || opl == nil || pl.Settlement != opl.Settlement {
					return true, "его здесь нет — плетение ушло в пустоту"
				}
				if op != nil && math.Hypot(op.X-p.X, op.Y-p.Y) > sp.Range+2 {
					return true, "слишком далеко — плетение ушло в пустоту"
				}
				t = o
			}
		}
		if b := battle.Body(t); b != nil {
			b.Mend(power * 0.6)
		}
		if v := core.Vitals(t); v != nil {
			v.Health = math.Min(100, v.Health+power)
		}
		if t != e {
			noteHealed(w, e, t.ID)
			say(w, e, "weave", name+": рана под твоими пальцами стягивается.")
		} else {
			say(w, e, "weave", name+": боль отступает, раны стягиваются.")
		}
	case "push":
		fe := weaveFoe(w, e, target, sp.Range)
		if fe == nil {
			say(w, e, "weave", name+": толчок ушёл в пустоту.")
			break
		}
		f := Foe(fe)
		dx, dy := f.X-p.X, f.Y-p.Y
		d := math.Max(0.1, math.Hypot(dx, dy))
		f.X, f.Y = ResolveMove(f.X, f.Y, f.X+dx/d*5, f.Y+dy/d*5)
		f.Stun, f.Aware, f.Target = math.Max(f.Stun, math.Min(5, power)), true, e.ID
		Emit(w, e, FeedItem{Kind: "weave", Text: name + ": " + FoeName(f.Species) + " отброшен и оглушён.", Foe: eidStr(fe.ID)})
	case "harm":
		fe := weaveFoe(w, e, target, sp.Range)
		if fe == nil {
			say(w, e, "weave", name+": разряд ушёл в пустоту.")
			break
		}
		hit := []*core.Entity{fe}
		if sp.Targets > 1 { // развилка: перекидывается на соседних врагов
			f0 := Foe(fe)
			w.Each("foe", func(o *core.Entity) {
				f := Foe(o)
				if len(hit) >= sp.Targets || o.ID == fe.ID || f.State == "dead" || f.Settlement != f0.Settlement {
					return
				}
				if math.Hypot(f.X-f0.X, f.Y-f0.Y) <= 9 {
					hit = append(hit, o)
				}
			})
		}
		for _, he := range hit {
			f := Foe(he)
			f.HP -= power
			f.Aware, f.Target = true, e.ID
			if sp.Linger > 0 {
				f.Bleed, f.BleedLeft = sp.Linger, 4
			}
			Emit(w, e, FeedItem{Kind: "hit", Text: name + " бьёт: " + FoeName(f.Species) + ".", Foe: eidStr(he.ID), Dmg: math.Round(power)})
			if f.HP <= 0 {
				killFoe(w, he, f, e)
			}
		}
	}
	return true, ""
}

func weaveFoe(w *core.World, e *core.Entity, target core.ID, rng float64) *core.Entity {
	p, pl := ScenePos(e), core.Place(e)
	if target != 0 {
		if fe := w.Get(target); fe != nil {
			if f := Foe(fe); f != nil && f.State != "dead" && pl != nil && f.Settlement == pl.Settlement &&
				math.Hypot(f.X-p.X, f.Y-p.Y) <= rng*1.1 {
				return fe
			}
		}
		return nil
	}
	fe, _ := nearestFoe(w, e, rng, 0)
	return fe
}

func roman(n int) string {
	r := []string{"I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI"}
	if n >= 1 && n <= len(r) {
		return r[n-1]
	}
	return "…"
}

// WeaveView — что игрок знает о плетениях (me.weave): узлы, ёмкость ума, знакомые схемы.
func WeaveView(w *core.World, e *core.Entity) map[string]any {
	g := grimoire(w, e)
	skill := skillOf(e, "mage")
	gift, canLearn, limit := GiftView(w, e)
	return map[string]any{
		"nodes":     WeaveNodes,
		"capacity":  round1x(arcane.Capacity(skill)), // сколько сложности ум держит без надрыва
		"max_len":   weaveMaxLen,
		"known":     g.Known,
		"gift":      gift,     // есть ли дар (v1.12)
		"limit":     limit,    // до какой сложности узор доступен этому телу
		"can_learn": canLearn, // можно ли здесь учиться (место силы или маг рядом) — act:"meditate"
	}
}

// CastingSeen — что видит сосед, когда игрок плетёт: опытный маг читает узор по имени, прочие — нет.
func CastingSeen(observer, caster *core.Entity) string {
	c := Combat(caster)
	if c == nil || c.CastGlow <= 0 {
		return ""
	}
	if skillOf(observer, "mage") >= 8 {
		return c.Casting
	}
	return "плетёт"
}
