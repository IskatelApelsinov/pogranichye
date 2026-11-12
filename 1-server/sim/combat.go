package sim

import (
	"math"
	"sort"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Бой в сцене (v1.10). Канон: ВВОД отделён от РАЗРЕШЕНИЯ. Клиент присылает «удар туда-то с такой-то
// точностью» (толстый — реальный прицел игрока, тонкий — кости), а сервер авторитетно решает исход:
// дальность, сектор, урон по части тела, ответ врага. Враги — НАСТОЯЩИЕ сущности сцены, поднятые из
// поголовья угодий (battle.FaunaC): убил волка — в угодьях на волка меньше. Ничего из ниоткуда.

func init() {
	core.Register("combat", func() core.Component { return &CombatC{Stance: "balanced", Kills: map[string]int{}} })
	core.Register("foe", func() core.Component { return &FoeC{} })
	core.Register("foecamp", func() core.Component { return &FoeCampC{} })
}

// CombatC — боевое состояние игрока: стойка, защита, скрытность, щит-плетение, откат удара.
type CombatC struct {
	Stance     string         `json:"stance"` // aggressive|balanced|defensive — стиль боя
	Guard      bool           `json:"guard"`  // держит блок
	Sneak      bool           `json:"sneak"`  // крадётся
	Feint      bool           `json:"feint"`  // заготовлен финт на следующий удар
	CD         float64        `json:"cd"`     // сек до следующего удара
	Dodge      float64        `json:"dodge"`  // сек остатка окна уворота
	Shield     float64        `json:"shield"` // очки щита-плетения
	ShieldLeft float64        `json:"shield_left"`
	Light      float64        `json:"light"` // сек остатка «светоча»
	Kills      map[string]int `json:"kills"` // всего убито по видам (нужно заданиям)
	KO         int            `json:"ko"`    // сколько раз падал без чувств
	Casting    string         `json:"-"`     // имя недавнего плетения (видно соседям)
	CastGlow   float64        `json:"-"`
}

func (c *CombatC) Kind() string { return "combat" }

func Combat(e *core.Entity) *CombatC { c, _ := e.Get("combat").(*CombatC); return c }

func ensureCombat(w *core.World, e *core.Entity) *CombatC {
	if c := Combat(e); c != nil {
		if c.Kills == nil {
			c.Kills = map[string]int{}
		}
		if c.Stance == "" {
			c.Stance = "balanced"
		}
		return c
	}
	c := &CombatC{Stance: "balanced", Kills: map[string]int{}}
	w.Attach(e, c)
	return c
}

// FoeC — враг/зверь в сцене поселения. Живёт только пока поселение «горячее» (рядом игрок).
type FoeC struct {
	Species    string  `json:"species"`
	Settlement core.ID `json:"settlement"`
	X          float64 `json:"x"`
	Y          float64 `json:"y"`
	Facing     float64 `json:"facing"`
	HP         float64 `json:"hp"`
	Max        float64 `json:"max"`
	State      string  `json:"state"` // roam|chase|flee|eat|dead
	Target     core.ID `json:"target"`
	Aware      bool    `json:"aware"`
	HomeX      float64 `json:"hx"`
	HomeY      float64 `json:"hy"`
	CD         float64 `json:"cd"`
	Stun       float64 `json:"stun"`
	Windup     float64 `json:"windup"` // читаемая пауза перед атакой: даёт игроку окно на реакцию
	Blind      float64 `json:"blind"`
	Bait       float64 `json:"bait"`
	Slow       bool    `json:"slow"`
	Weak       bool    `json:"weak"`
	Bleed      float64 `json:"bleed"` // урон/с от жгучего плетения
	BleedLeft  float64 `json:"bleed_left"`
	Dead       float64 `json:"dead"` // сек до исчезновения туши
	WX         float64 `json:"-"`    // точка блуждания
	WY         float64 `json:"-"`
	WT         float64 `json:"-"`
}

func (c *FoeC) Kind() string { return "foe" }

func Foe(e *core.Entity) *FoeC { c, _ := e.Get("foe").(*FoeC); return c }

// FoeCampC — состояние «дикой стороны» поселения: таймер подсева и разбойничий стан за рекой.
type FoeCampC struct {
	Timer   float64 `json:"timer"`
	Bandits float64 `json:"bandits"` // сколько лихих людей в стане (отрастает со временем)
	Init    bool    `json:"init"`
}

func (c *FoeCampC) Kind() string { return "foecamp" }

type foeStat struct {
	hp, speed, dmg, reach, aggro, cd float64
	flee                             bool
	cap                              int // сколько голов максимум поднимаем в сцену
}

var foeStats = map[string]foeStat{
	"hare":   {hp: 6, speed: 4.6, flee: true, cap: 3},
	"fox":    {hp: 12, speed: 4.0, flee: true, cap: 2},
	"boar":   {hp: 40, speed: 3.0, dmg: 9, reach: 1.8, aggro: 8, cd: 1.7, cap: 2},
	"wolf":   {hp: 30, speed: 3.5, dmg: 8, reach: 1.7, aggro: 16, cd: 1.3, cap: 3},
	"bear":   {hp: 85, speed: 2.7, dmg: 16, reach: 2.2, aggro: 12, cd: 2.1, cap: 1},
	"bandit": {hp: 45, speed: 2.6, dmg: 10, reach: 1.9, aggro: 18, cd: 1.5, cap: 2},
}

var foeOrder = []string{"hare", "fox", "boar", "wolf", "bear", "bandit"}

// FoeName — человеческое имя вида.
func FoeName(species string) string {
	if b, ok := battle.Beasts[species]; ok {
		return b.Name
	}
	return species
}

// дикие зоны общей геометрии деревни: западный лес, заречье (юг), окрестности рудника (восток).
type wildZone struct{ x0, y0, x1, y1 float64 }

var wildZones = map[string][]wildZone{
	"hare":   {{-135, -50, -80, 110}},
	"fox":    {{-135, -50, -80, 110}},
	"boar":   {{-140, 20, -85, 130}, {100, 50, 140, 120}},
	"wolf":   {{-140, -60, -90, 60}, {-120, -140, 60, -100}},
	"bear":   {{60, -140, 140, -100}, {105, 60, 140, 130}},
	"bandit": {{-60, -140, -20, -105}},
}

func foeSpot(species string, settlement core.ID, n int) (float64, float64) {
	zs := wildZones[species]
	h := uint64(settlement)*2654435761 + uint64(n)*40503 + hashReq(species)
	z := zs[int(h%uint64(len(zs)))]
	fx := float64((h/7)%1000) / 1000
	fy := float64((h/7919)%1000) / 1000
	x, y := z.x0+(z.x1-z.x0)*fx, z.y0+(z.y1-z.y0)*fy
	if Blocked(x, y) {
		x, y = (z.x0+z.x1)/2, (z.y0+z.y1)/2
	}
	return x, y
}

func inVillageCore(x, y float64) bool {
	return math.Abs(x) <= VillageHalf && math.Abs(y) <= VillageHalf
}

// --- подсев врагов из поголовья угодий ---

// populateFoes поднимает в сцену горячего поселения зверей по РЕАЛЬНОМУ поголовью (и стан лихих людей).
func populateFoes(w *core.World, se *core.Entity, dt float64) {
	camp, _ := se.Get("foecamp").(*FoeCampC)
	if camp == nil {
		camp = &FoeCampC{}
		w.Attach(se, camp)
	}
	if !camp.Init {
		camp.Init, camp.Bandits = true, 2
	}
	camp.Bandits = math.Min(3, camp.Bandits+dt/600) // стан понемногу пополняется пришлыми
	camp.Timer -= dt
	if camp.Timer > 0 {
		return
	}
	camp.Timer = 12
	alive := map[string]int{}
	total := 0
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Settlement == se.ID && f.State != "dead" {
			alive[f.Species]++
			total++
		}
	})
	fauna := battle.Fauna(se)
	for _, sp := range foeOrder {
		st := foeStats[sp]
		want := 0
		if sp == "bandit" {
			want = int(camp.Bandits)
		} else if fauna != nil {
			want = int(fauna.Pop[sp] / 2)
		}
		if want > st.cap {
			want = st.cap
		}
		for n := alive[sp]; n < want && total < 12; n++ {
			x, y := foeSpot(sp, se.ID, n+int(camp.Bandits*10)+total)
			w.Spawn(&FoeC{Species: sp, Settlement: se.ID, X: x, Y: y, HomeX: x, HomeY: y,
				HP: st.hp, Max: st.hp, State: "roam", WX: x, WY: y})
			total++
		}
	}
}

// dropFoes убирает врагов остывшего поселения: поголовье уже учтено в FaunaC, сцена не нужна.
func dropFoes(w *core.World, settlement core.ID) {
	var ids []core.ID
	w.Each("foe", func(fe *core.Entity) {
		if Foe(fe).Settlement == settlement {
			ids = append(ids, fe.ID)
		}
	})
	for _, id := range ids {
		w.Remove(id)
	}
}

// --- шаг врагов и боевых таймеров (реальное время) ---

// StepCombat продвигает бой на dt секунд: таймеры игроков, ИИ врагов, их удары. controlled(id) — тело
// ведёт живой игрок. Враги есть только там, где есть игроки.
func StepCombat(w *core.World, dt float64, controlled func(core.ID) bool, s *dice.Stream) {
	if w == nil || dt <= 0 {
		return
	}
	w.Each("combat", func(e *core.Entity) {
		c := Combat(e)
		c.CD = math.Max(0, c.CD-dt)
		c.Dodge = math.Max(0, c.Dodge-dt)
		c.Light = math.Max(0, c.Light-dt)
		c.CastGlow = math.Max(0, c.CastGlow-dt)
		if c.ShieldLeft > 0 {
			if c.ShieldLeft -= dt; c.ShieldLeft <= 0 {
				c.Shield, c.ShieldLeft = 0, 0
			}
		}
		if c.Guard { // блок держится запалом
			if p := ScenePos(e); p != nil {
				if p.Stam = math.Max(0, p.Stam-0.03*dt); p.Stam <= 0 {
					c.Guard = false
				}
			}
		}
	})
	// игроки по поселениям (в пути — не в сцене)
	players := map[core.ID][]*core.Entity{}
	w.Each("player", func(e *core.Entity) {
		if controlled != nil && !controlled(e.ID) {
			return
		}
		if OnRoad(e) {
			return
		}
		if pl := core.Place(e); pl != nil && pl.Settlement != 0 && ScenePos(e) != nil {
			players[pl.Settlement] = append(players[pl.Settlement], e)
		}
	})
	sids := make([]core.ID, 0, len(players))
	for sid := range players {
		sids = append(sids, sid)
	}
	sort.Slice(sids, func(i, j int) bool { return sids[i] < sids[j] })
	for _, sid := range sids {
		if se := w.Get(sid); se != nil {
			populateFoes(w, se, dt)
		}
	}
	var gone []core.ID
	var stale []core.ID
	w.Each("foe", func(fe *core.Entity) {
		f := Foe(fe)
		ps := players[f.Settlement]
		if len(ps) == 0 {
			stale = append(stale, fe.ID)
			return
		}
		if f.State == "dead" {
			if f.Dead -= dt; f.Dead <= 0 {
				gone = append(gone, fe.ID)
			}
			return
		}
		stepFoe(w, fe, f, ps, dt, s)
	})
	for _, id := range append(gone, stale...) {
		w.Remove(id)
	}
}

func stepFoe(w *core.World, fe *core.Entity, f *FoeC, ps []*core.Entity, dt float64, s *dice.Stream) {
	st := foeStats[f.Species]
	f.CD = math.Max(0, f.CD-dt)
	f.Blind = math.Max(0, f.Blind-dt)
	if f.BleedLeft > 0 { // жгучее плетение доедает
		f.BleedLeft -= dt
		f.HP -= f.Bleed * dt
		if f.HP <= 0 {
			killFoe(w, fe, f, w.Get(f.Target))
			return
		}
	}
	if f.Stun > 0 {
		f.Windup = 0
		f.Stun -= dt
		return
	}
	if f.Bait > 0 { // жрёт приманку — не до тебя
		f.Windup = 0
		f.Bait -= dt
		f.State = "eat"
		return
	}
	// ближайший игрок
	var near *core.Entity
	nd := math.MaxFloat64
	for _, p := range ps {
		pp := ScenePos(p)
		if d := math.Hypot(pp.X-f.X, pp.Y-f.Y); d < nd {
			nd, near = d, p
		}
	}
	pp := ScenePos(near)
	pc := Combat(near)
	sneaking := pc != nil && pc.Sneak
	speed := st.speed
	if f.Slow {
		speed *= 0.5
	}
	if st.flee { // дичь: бежит от человека
		r := 14.0
		if sneaking {
			r = 5
		}
		if nd < r {
			f.State, f.Aware = "flee", true
			moveFoe(f, f.X-(pp.X-f.X), f.Y-(pp.Y-f.Y), speed*dt)
			return
		}
		f.Aware = false
		wanderFoe(f, dt, s)
		return
	}
	aggro := st.aggro
	if sneaking && !f.Aware {
		aggro *= 0.4
	}
	safe := inVillageCore(pp.X, pp.Y) // село под защитой людей — зверь туда не идёт
	leash := math.Hypot(f.X-f.HomeX, f.Y-f.HomeY) > 75
	hunting := (f.Aware || nd <= aggro) && !safe && !leash
	if f.Species == "wolf" && f.HP < f.Max*0.25 { // подранок уходит
		hunting = false
		if nd < 20 {
			f.State = "flee"
			moveFoe(f, f.X-(pp.X-f.X), f.Y-(pp.Y-f.Y), speed*dt)
			return
		}
	}
	if !hunting {
		f.Windup = 0
		f.Aware, f.Target = false, 0
		wanderFoe(f, dt, s)
		return
	}
	f.Aware, f.Target, f.State = true, near.ID, "chase"
	if f.Blind > 0 { // ослеплён — мечется
		moveFoe(f, f.X+s.Uniform(-1, 1), f.Y+s.Uniform(-1, 1), speed*0.5*dt)
		return
	}
	if nd > st.reach {
		f.Windup = 0 // цель успела выйти из радиуса — атака сорвана
		moveFoe(f, pp.X, pp.Y, math.Min(speed*dt, nd-st.reach*0.8))
		return
	}
	f.Facing = facingDeg(pp.X-f.X, pp.Y-f.Y, f.Facing)
	if f.Windup > 0 {
		f.State = "windup"
		f.Windup = math.Max(0, f.Windup-dt)
		if f.Windup <= 0 {
			f.CD = st.cd
			foeStrike(w, fe, f, near, s)
		}
		return
	}
	if f.CD <= 0 {
		// Не бьём мгновенно при входе в радиус: показываем явный замах.
		f.Windup = 0.45
		f.State = "windup"
	}
}

func moveFoe(f *FoeC, tx, ty, step float64) {
	dx, dy := tx-f.X, ty-f.Y
	d := math.Hypot(dx, dy)
	if d < 1e-6 || step <= 0 {
		return
	}
	nx, ny := ResolveMove(f.X, f.Y, f.X+dx/d*step, f.Y+dy/d*step)
	if inVillageCore(nx, ny) && !inVillageCore(f.X, f.Y) {
		return // в село не заходим
	}
	f.X, f.Y = nx, ny
	f.Facing = facingDeg(dx, dy, f.Facing)
}

func wanderFoe(f *FoeC, dt float64, s *dice.Stream) {
	f.State = "roam"
	f.WT -= dt
	if f.WT <= 0 || math.Hypot(f.WX-f.X, f.WY-f.Y) < 1 {
		f.WT = s.Uniform(4, 10)
		f.WX, f.WY = f.HomeX+s.Uniform(-14, 14), f.HomeY+s.Uniform(-14, 14)
	}
	moveFoe(f, f.WX, f.WY, foeStats[f.Species].speed*0.3*dt)
}

// foeStrike — удар врага по игроку: уворот, блок, щит-плетение, доспех, рана в часть тела.
func foeStrike(w *core.World, fe *core.Entity, f *FoeC, pe *core.Entity, s *dice.Stream) {
	c := ensureCombat(w, pe)
	v := core.Vitals(pe)
	p := ScenePos(pe)
	if v == nil || p == nil {
		return
	}
	name := FoeName(f.Species)
	fid := eidStr(fe.ID)
	if c.Dodge > 0 {
		Emit(w, pe, FeedItem{Kind: "dodge", Text: "Ты увернулся: " + name + " промахнулся.", Foe: fid})
		return
	}
	if !s.Chance(0.82) {
		Emit(w, pe, FeedItem{Kind: "miss", Text: name + " промахнулся.", Foe: fid})
		return
	}
	dmg := foeStats[f.Species].dmg * s.Uniform(0.8, 1.2)
	if f.Weak {
		dmg *= 0.6
	}
	switch c.Stance {
	case "aggressive":
		dmg *= 1.25
	case "defensive":
		dmg *= 0.7
	}
	inv := Inv(pe)
	if inv != nil && inv.Worn["armor"] == "jerkin" {
		dmg *= 0.75
	}
	if c.Guard { // блок работает только в лицо
		ang := math.Abs(angDiff(facingDeg(f.X-p.X, f.Y-p.Y, p.Facing), p.Facing))
		if ang <= 75 {
			k := 0.35
			if inv != nil && inv.Worn["offhand"] == "shield" {
				k = 0.15
			}
			if c.Stance == "defensive" {
				k *= 0.7
			}
			dmg *= k
			p.Stam = math.Max(0, p.Stam-0.12)
			Emit(w, pe, FeedItem{Kind: "block", Text: "Ты принял удар на блок.", Foe: fid})
		}
	}
	if c.Shield > 0 { // щит-плетение впитывает
		ab := math.Min(c.Shield, dmg)
		c.Shield -= ab
		dmg -= ab
		if dmg <= 0.01 {
			Emit(w, pe, FeedItem{Kind: "block", Text: "Щит плетения погасил удар.", Foe: fid})
			return
		}
	}
	part := []string{"torso", "torso", "arm", "leg", "arm", "leg", "head"}[s.Intn(7)]
	if b := battle.Body(pe); b != nil {
		b.HurtPart(part, dmg*0.5)
	}
	dmg /= statMod(pe, "endurance", 0.1)
	TrainStat(w, pe, "endurance", 0.04) // битый крепчает
	v.Health = math.Max(0, v.Health-dmg)
	Emit(w, pe, FeedItem{Kind: "hurt", Text: name + " ранил тебя: " + partRu(part) + ".", Foe: fid, Part: part, Dmg: math.Round(dmg)})
	if v.Health <= 5 {
		knockOut(w, pe)
	}
}

// knockOut — игрок упал без чувств: его дотащили до очага (мир не убивает игрока одним волком, но
// берёт плату: раны остаются, часть монет пропала, силы на нуле).
func knockOut(w *core.World, e *core.Entity) {
	c := ensureCombat(w, e)
	c.KO++
	c.Guard, c.Shield, c.ShieldLeft, c.Sneak = false, 0, 0, false
	if v := core.Vitals(e); v != nil {
		v.Health = 25
		v.Energy = math.Min(v.Energy, 15)
	}
	if pu := core.Purse(e); pu != nil {
		pu.Coins = math.Floor(pu.Coins*0.8*10) / 10
	}
	GoTo(w, e, "hearth")
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Target == e.ID {
			f.Target, f.Aware = 0, false
		}
	})
	say(w, e, "ko", "Темнота… Ты очнулся у очага: кто-то дотащил тебя до деревни. Кошель полегчал.")
}

func partRu(p string) string {
	switch p {
	case "head":
		return "голова"
	case "arm":
		return "рука"
	case "leg":
		return "нога"
	}
	return "корпус"
}

func angDiff(a, b float64) float64 {
	d := math.Mod(a-b+540, 360) - 180
	return d
}

func eidStr(id core.ID) string { return sid(id) }

// --- удар игрока ---

// StrikeInput — ВВОД удара от клиента. Aimed=true: клиент сам целился (первое лицо), Acc — качество
// попадания 0..1 от его hit-test; сервер проверяет дальность и сектор взгляда. Aimed=false: тонкий
// клиент — сервер сам поворачивает к цели и бросает кости на попадание от навыка.
type StrikeInput struct {
	Target core.ID
	Aim    string  // head|torso|arm|leg
	Style  string  // slash|thrust|heavy (ближний) / snap|drawn (лук)
	Acc    float64 // 0..1 (только Aimed)
	Power  float64 // 0..1 натяжение лука/замах (только Aimed; 0 = полный)
	Aimed  bool
	ReqID  string
}

type weaponStat struct {
	dmg, reach float64
	skill      string
	ranged     bool
}

var weapons = map[string]weaponStat{
	"":      {dmg: 4, reach: 1.9, skill: "brawl"},
	"knife": {dmg: 8, reach: 1.9, skill: "blade"},
	"axe":   {dmg: 12, reach: 2.2, skill: "blade"},
	"sword": {dmg: 14, reach: 2.5, skill: "blade"},
	"bow":   {dmg: 17, reach: 45, skill: "archery", ranged: true},
}

func skillOf(e *core.Entity, name string) float64 {
	if sk := core.Skills(e); sk != nil {
		return sk.Level(name)
	}
	return 0
}

// nearestFoe — ближайший живой враг в поселении игрока в пределах r (и, если cone>0, в секторе взгляда).
func nearestFoe(w *core.World, e *core.Entity, r, cone float64) (*core.Entity, float64) {
	p, pl := ScenePos(e), core.Place(e)
	if p == nil || pl == nil {
		return nil, 0
	}
	var best *core.Entity
	bd := r
	w.Each("foe", func(fe *core.Entity) {
		f := Foe(fe)
		if f.Settlement != pl.Settlement || f.State == "dead" {
			return
		}
		d := math.Hypot(f.X-p.X, f.Y-p.Y)
		if d > bd {
			return
		}
		if cone > 0 && d > 0.5 && math.Abs(angDiff(facingDeg(f.X-p.X, f.Y-p.Y, p.Facing), p.Facing)) > cone {
			return
		}
		best, bd = fe, d
	})
	return best, bd
}

// Strike — РАЗРЕШЕНИЕ удара игрока (меч/топор/нож/кулак/лук) по врагу сцены.
func Strike(w *core.World, e *core.Entity, in StrikeInput, s *dice.Stream) (bool, string) {
	c := ensureCombat(w, e)
	pc := EnsurePlayer(w, e)
	if in.ReqID != "" && pc.alreadyDone(in.ReqID) {
		return true, ""
	}
	p, v := ScenePos(e), core.Vitals(e)
	if p == nil || v == nil {
		return false, "нет тела в сцене"
	}
	if OnRoad(e) {
		return false, "ты в пути"
	}
	if c.CD > 0 {
		return false, "ещё не готов к удару"
	}
	inv := ensureInv(w, e)
	ws, ok := weapons[inv.Hand]
	if !ok {
		ws = weapons[""] // факел/удочка — бьём как кулаком
	}
	if ws.ranged && inv.Items["arrow"] < 1 {
		return false, "нет стрел"
	}
	// цель
	var fe *core.Entity
	dist := 0.0
	if in.Target != 0 {
		fe = w.Get(in.Target)
		if fe == nil || Foe(fe) == nil || Foe(fe).State == "dead" || Foe(fe).Settlement != core.Place(e).Settlement {
			return false, "цели нет"
		}
		dist = math.Hypot(Foe(fe).X-p.X, Foe(fe).Y-p.Y)
	} else {
		cone := 0.0
		if in.Aimed {
			cone = 60
		}
		fe, dist = nearestFoe(w, e, ws.reach*1.15, cone)
	}
	// стиль
	mult, cd, stam := 1.0, 0.8, 0.08
	switch in.Style {
	case "thrust":
		mult, cd, stam = 0.85, 0.65, 0.07
	case "heavy":
		mult, cd, stam = 1.6, 1.5, 0.18
	case "snap":
		mult, cd, stam = 0.65, 0.9, 0.05
	}
	if ws.ranged && in.Style != "snap" {
		cd, stam = 1.6, 0.1
	}
	c.CD = cd
	if p.Stam < stam { // выдохся — удар вялый
		mult *= 0.6
	}
	p.Stam = math.Max(0, p.Stam-stam)
	if ws.ranged {
		inv.Items["arrow"]--
	}
	trainSkill(w, e, ws.skill, 0.25)
	if in.ReqID != "" {
		pc.markDone(in.ReqID)
	}
	c.Sneak = c.Sneak && fe == nil // удар выдаёт
	feint := c.Feint
	c.Feint = false
	if fe == nil {
		say(w, e, "miss", "Удар рассёк воздух.")
		return true, ""
	}
	f := Foe(fe)
	fid := eidStr(fe.ID)
	if dist > ws.reach*1.15 {
		say(w, e, "miss", "Слишком далеко.")
		return true, ""
	}
	skill := skillOf(e, ws.skill)
	aim := in.Aim
	if aim != "head" && aim != "arm" && aim != "leg" {
		aim = "torso"
	}
	acc := in.Acc
	if in.Aimed {
		cone := 60.0
		if ws.ranged {
			cone = 30
		}
		if dist > 0.6 && math.Abs(angDiff(facingDeg(f.X-p.X, f.Y-p.Y, p.Facing), p.Facing)) > cone {
			acc = 0 // смотрел не туда — сервер прицелу не верит
		}
	} else { // тонкий клиент: кости от навыка
		p.Facing = facingDeg(f.X-p.X, f.Y-p.Y, p.Facing)
		chance := 0.58 + skill*0.012 + (Stat(e, "perception")-statBase)*0.006 - drunkPenalty(e)
		switch aim {
		case "head":
			chance -= 0.25
		case "arm", "leg":
			chance -= 0.1
		}
		if ws.ranged {
			chance -= dist / 110
		}
		if in.Style == "thrust" {
			chance += 0.08
		}
		if feint {
			chance += 0.22
		}
		if !f.Aware {
			chance += 0.2
		}
		acc = 0
		if s.Chance(chance) {
			acc = s.Uniform(0.45, 1)
		}
	}
	wasAware := f.Aware
	f.Aware, f.Target = true, e.ID
	if acc <= 0.01 {
		Emit(w, e, FeedItem{Kind: "miss", Text: "Мимо: " + FoeName(f.Species) + " цел.", Foe: fid})
		return true, ""
	}
	if acc > 1 {
		acc = 1
	}
	dmg := ws.dmg * mult * math.Min(1.7, 0.75+skill/40) * (0.6 + 0.6*acc)
	if ws.ranged {
		dmg *= statMod(e, "perception", 0.12)
	} else {
		dmg *= statMod(e, "strength", 0.15)
	}
	if ws.ranged && in.Aimed && in.Power > 0 {
		dmg *= 0.45 + 0.55*math.Min(1, in.Power)
	}
	switch aim {
	case "head":
		dmg *= 1.8
	case "arm", "leg":
		dmg *= 0.7
	}
	switch c.Stance {
	case "aggressive":
		dmg *= 1.25
	case "defensive":
		dmg *= 0.75
	}
	if v.Energy < 20 {
		dmg *= 0.7
	}
	text := "Попал: " + FoeName(f.Species) + ", " + partRu(aim) + "."
	if !wasAware { // исподтишка
		dmg *= 2
		text = "Удар исподтишка! " + text
		trainSkill(w, e, "cunning", 0.4)
	}
	if feint {
		dmg *= 1.25
		f.CD += 0.6
	}
	switch aim {
	case "head":
		f.Stun = math.Max(f.Stun, 0.8)
	case "arm":
		f.Weak = true
	case "leg":
		f.Slow = true
	}
	if in.Style == "heavy" {
		f.Stun = math.Max(f.Stun, 1.0)
	}
	f.HP -= dmg
	Emit(w, e, FeedItem{Kind: "hit", Text: text, Foe: fid, Part: aim, Dmg: math.Round(dmg)})
	if f.HP <= 0 {
		killFoe(w, fe, f, e)
	}
	return true, ""
}

// killFoe — враг пал: туша лежит, поголовье угодий убыло, добыча — в закрома убившего.
func killFoe(w *core.World, fe *core.Entity, f *FoeC, by *core.Entity) {
	f.State, f.HP, f.Dead, f.Aware, f.Target = "dead", 0, 25, false, 0
	se := w.Get(f.Settlement)
	if f.Species == "bandit" {
		if se != nil {
			if camp, _ := se.Get("foecamp").(*FoeCampC); camp != nil {
				camp.Bandits = math.Max(0, camp.Bandits-1)
			}
		}
	} else if se != nil {
		if fa := battle.Fauna(se); fa != nil {
			fa.Pop[f.Species] = math.Max(0, fa.Pop[f.Species]-1)
		}
	}
	if by == nil || Player(by) == nil {
		return
	}
	c := ensureCombat(w, by)
	c.Kills[f.Species]++
	b := battle.Beasts[f.Species]
	loot := ""
	if f.Species == "bandit" {
		coins := 3 + float64(hashReq(eidStr(fe.ID))%5)
		if pu := core.Purse(by); pu != nil {
			pu.Coins += coins
		}
		ensureInv(w, by).Items["arrow"] += 2
		loot = "кошель и пара стрел"
	} else {
		meat := math.Max(1, math.Round(b.Meat/4))
		addStash(w, by, econ.GoodMeat, meat)
		loot = "мясо"
		if b.Hide > 0 {
			addStash(w, by, econ.GoodHide, math.Ceil(b.Hide/2))
			loot = "мясо и шкура"
		}
	}
	trainSkill(w, by, "hunter", 0.5)
	Emit(w, by, FeedItem{Kind: "kill", Text: FoeName(f.Species) + " повержен. Добыча: " + loot + ".", Foe: eidStr(fe.ID)})
}

// --- защита, стойки, хитрости ---

// SetGuard — поднять/опустить блок.
func SetGuard(w *core.World, e *core.Entity, on bool) {
	c := ensureCombat(w, e)
	if p := ScenePos(e); on && (p == nil || p.Stam <= 0.05) {
		return
	}
	c.Guard = on
}

// DodgeRoll — уворот: короткое окно, когда удары врагов проходят мимо. Стоит запала.
func DodgeRoll(w *core.World, e *core.Entity) (bool, string) {
	c := ensureCombat(w, e)
	p := ScenePos(e)
	if p == nil || p.Stam < 0.2 {
		return false, "нет сил уворачиваться"
	}
	if c.Dodge > 0 {
		return true, ""
	}
	p.Stam -= 0.2 / statMod(e, "agility", 0.2)
	c.Dodge = 0.7 * statMod(e, "agility", 0.15)
	TrainStat(w, e, "agility", 0.05)
	trainSkill(w, e, "cunning", 0.1)
	return true, ""
}

// SetStance — стиль боя: aggressive (бьёшь и получаешь сильнее), balanced, defensive.
func SetStance(w *core.World, e *core.Entity, stance string) (bool, string) {
	switch stance {
	case "aggressive", "balanced", "defensive":
		ensureCombat(w, e).Stance = stance
		return true, ""
	}
	return false, "нет такой стойки"
}

// Trick — хитрости: sand (песок в глаза), feint (финт), shove (толчок), bait (приманка), sneak (красться).
func Trick(w *core.World, e *core.Entity, name, reqid string) (bool, string) {
	c := ensureCombat(w, e)
	pc := EnsurePlayer(w, e)
	if reqid != "" && pc.alreadyDone(reqid) {
		return true, ""
	}
	p := ScenePos(e)
	if p == nil || OnRoad(e) {
		return false, "не сейчас"
	}
	done := func() (bool, string) {
		if reqid != "" {
			pc.markDone(reqid)
		}
		trainSkill(w, e, "cunning", 0.3)
		return true, ""
	}
	switch name {
	case "sneak":
		c.Sneak = !c.Sneak
		return true, ""
	case "feint":
		if p.Stam < 0.08 {
			return false, "нет сил на финт"
		}
		p.Stam -= 0.08
		c.Feint = true
		return done()
	case "sand":
		fe, _ := nearestFoe(w, e, 4.5, 0)
		if fe == nil {
			return false, "некому сыпать в глаза"
		}
		if p.Stam < 0.1 {
			return false, "нет сил"
		}
		p.Stam -= 0.1
		f := Foe(fe)
		f.Blind, f.Aware, f.Target = 4.5, true, e.ID
		Emit(w, e, FeedItem{Kind: "hit", Text: "Горсть песка в глаза: " + FoeName(f.Species) + " ослеплён.", Foe: eidStr(fe.ID)})
		return done()
	case "shove":
		fe, _ := nearestFoe(w, e, 2.6, 0)
		if fe == nil {
			return false, "некого толкать"
		}
		if p.Stam < 0.15 {
			return false, "нет сил"
		}
		p.Stam -= 0.15
		f := Foe(fe)
		dx, dy := f.X-p.X, f.Y-p.Y
		d := math.Max(0.1, math.Hypot(dx, dy))
		f.X, f.Y = ResolveMove(f.X, f.Y, f.X+dx/d*4, f.Y+dy/d*4)
		f.Stun, f.Aware, f.Target = math.Max(f.Stun, 1.3), true, e.ID
		Emit(w, e, FeedItem{Kind: "hit", Text: "Ты отшвырнул врага: " + FoeName(f.Species) + " пошатнулся.", Foe: eidStr(fe.ID)})
		return done()
	case "bait":
		st := ensureStashOf(w, e)
		if st.Goods[econ.GoodMeat] < 1 {
			return false, "нужен кусок мяса на приманку"
		}
		pl := core.Place(e)
		n := 0
		w.Each("foe", func(fe *core.Entity) {
			f := Foe(fe)
			if f.Settlement != pl.Settlement || f.State == "dead" || f.Species == "bandit" || foeStats[f.Species].flee {
				return
			}
			if math.Hypot(f.X-p.X, f.Y-p.Y) <= 22 {
				f.Bait, f.Aware, f.Target = 8, false, 0
				n++
			}
		})
		if n == 0 {
			return false, "зверя рядом нет"
		}
		st.Goods[econ.GoodMeat]--
		say(w, e, "loot", "Ты бросил мясо: зверь отвлёкся на приманку.")
		return done()
	}
	return false, "нет такой хитрости"
}

// FoesView — враги сцены поселения для кадра (place.foes).
func FoesView(w *core.World, settlement core.ID) []map[string]any {
	out := []map[string]any{}
	w.Each("foe", func(fe *core.Entity) {
		f := Foe(fe)
		if f.Settlement != settlement {
			return
		}
		out = append(out, map[string]any{
			"id": eidStr(fe.ID), "kind": f.Species, "name": FoeName(f.Species),
			"x": round1x(f.X), "y": round1x(f.Y), "facing": math.Round(f.Facing),
			"hp": math.Round(math.Max(0, f.HP)), "max": f.Max, "state": f.State, "aware": f.Aware,
			"slow": f.Slow, "weak": f.Weak, "blind": f.Blind > 0, "stun": f.Stun > 0,
			"windup": round1x(f.Windup),
		})
	})
	sort.Slice(out, func(i, j int) bool { return out[i]["id"].(string) < out[j]["id"].(string) })
	return out
}

// CombatView — боевое состояние игрока для кадра (me.combat).
func CombatView(w *core.World, e *core.Entity) map[string]any {
	c := ensureCombat(w, e)
	inv := ensureInv(w, e)
	return map[string]any{
		"stance": c.Stance, "guard": c.Guard, "sneak": c.Sneak, "feint": c.Feint,
		"cd": round1x(c.CD), "shield": math.Round(c.Shield), "light": math.Round(c.Light),
		"arrows": inv.Items["arrow"], "weapon": inv.Hand, "kills": c.Kills, "ko": c.KO,
	}
}

// Approach — ДИСКРЕТНЫЙ подход к врагу для менеджмент-клиента (тонкий web/mobile, ADR 0002): тап по
// зверю ставит тело игрока на дистанцию dist от него со стороны, откуда игрок шёл. Толстый клиент
// подходит ногами и этим не пользуется. Зверь при этом может заметить (если игрок не крадётся).
func Approach(w *core.World, e *core.Entity, foe core.ID, dist float64) (bool, string) {
	fe := w.Get(foe)
	pl := core.Place(e)
	if fe == nil || Foe(fe) == nil || pl == nil || Foe(fe).Settlement != pl.Settlement || OnRoad(e) {
		return false, "зверя не видно"
	}
	f := Foe(fe)
	if f.State == "dead" {
		return false, "он уже мёртв"
	}
	p := EnsureScenePos(w, e)
	if dist < 1.5 {
		dist = 1.5
	}
	if dist > 30 {
		dist = 30
	}
	dx, dy := p.X-f.X, p.Y-f.Y
	d := math.Hypot(dx, dy)
	if d < 0.1 {
		dx, dy, d = 1, 0, 1
	}
	x, y := f.X+dx/d*dist, f.Y+dy/d*dist
	if Blocked(x, y) {
		x, y = f.X, f.Y
	}
	p.X, p.Y = x, y
	p.Facing = facingDeg(f.X-x, f.Y-y, p.Facing)
	return true, ""
}
