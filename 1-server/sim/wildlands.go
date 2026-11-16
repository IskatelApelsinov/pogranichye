package sim

import (
	"math"
	"sort"
	"strconv"
	"strings"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Окрестности (v1.22): в какую бы сторону ни пошёл путник, он куда-нибудь придёт.
//
// Крупные дикие места (пещеры, башни, руины — wildsites.go) редки: два-четыре на край и не ближе
// девяти километров от сёл. Между ними лежало пустое поле. Теперь поле заселено УРОЧИЩАМИ — мелкими
// местами без жителей: стоянка, родник, ягодник, бор, выход руды, заводь, избушка, курган, развалины,
// нора. Урочище — не сущность, а вывод из семени мира: земля поделена на клетки по 1200 м, и в
// клетке либо есть урочище (какое и где — решает хеш семени и клетки, вид — по лесистости, влаге и
// высоте места), либо нет. Поэтому урочища есть ВЕЗДЕ, сколько бы мир ни рос, ничего не стоят, пока
// рядом никого нет, и одинаковы для всех, кто туда придёт.
//
// Что у урочища хранится — только то, что люди в нём изменили: сколько осталось травы, руды, рыбы
// (запас восполняется по дням, но не бесконечен — «ничего из ниоткуда») и кто когда обыскал
// развалины. Хранится лениво, в одной сущности WildUseC.
//
// Дела в урочище (op:"site") — обычные серверные действия: надо стоять рядом, иметь силы, а для
// рыбы — удочку. Жителей здесь нет; село, в чьих землях лежит урочище, остаётся «своим» для тела.

const (
	wildCell     = 1200.0 // сторона клетки, м
	wildChance   = 0.58   // доля клеток с урочищем
	wildReach    = 25.0   // на таком расстоянии от места можно делать дела
	wildNearMax  = 8      // сколько ближайших мест отдаёт кадр
	wildNearDist = 3000.0 // радиус, в котором путник знает о местах вокруг
)

// WildPlace — место без жителей: урочище (из семени) или крупное дикое место (сущность WildSiteC).
type WildPlace struct {
	ID     string
	Name   string
	Kind   string
	E, N   float64 // метры на плоскости кадра 0
	Z      float64 // пещера — вниз, башня — вверх
	Danger float64
}

type wildKind struct {
	kind  string
	names []string
	acts  []string // forage:herb|wood|ore|fish|veg, rest, drink, explore
}

var wildKinds = map[string]wildKind{
	"camp":    {"camp", []string{"Старое кострище", "Стоянка у камня", "Привал под сосной", "Пастуший стан"}, []string{"rest"}},
	"spring":  {"spring", []string{"Студёный ключ", "Родник в овраге", "Ключ под берёзой", "Гремячий ключ"}, []string{"drink", "rest"}},
	"berry":   {"berry", []string{"Ягодник", "Малиновый лог", "Брусничная поляна", "Земляничный склон"}, []string{"forage:veg"}},
	"herbs":   {"herbs", []string{"Травяной луг", "Медуничная поляна", "Зверобойный косогор", "Заливной луг"}, []string{"forage:herb"}},
	"thicket": {"thicket", []string{"Глухой бор", "Сухостойник", "Бурелом", "Еловая грива"}, []string{"forage:wood"}},
	"outcrop": {"outcrop", []string{"Рудная осыпь", "Ржавый обрыв", "Каменный лоб", "Старая копь"}, []string{"forage:ore"}},
	"pond":    {"pond", []string{"Тихая заводь", "Лесное озерцо", "Старица", "Бобровая запруда"}, []string{"forage:fish", "rest"}},
	"hut":     {"hut", []string{"Охотничья избушка", "Зимовье", "Сторожка", "Брошенный хутор"}, []string{"rest", "explore"}},
	"barrow":  {"barrow", []string{"Старый курган", "Могильный холм", "Каменная баба", "Кольцо камней"}, []string{"explore"}},
	"ruins":   {"ruins", []string{"Развалины", "Обвалившаяся башня", "Пепелище", "Заросший двор"}, []string{"explore"}},
	"den":     {"den", []string{"Тёмная нора", "Провал", "Лисий овраг", "Грот"}, []string{"explore"}},
	// крупные дикие места (сущности WildSiteC)
	"cave":  {"cave", nil, []string{"explore", "forage:ore"}},
	"tower": {"tower", nil, []string{"explore", "rest"}},
	"grove": {"grove", nil, []string{"forage:herb", "rest"}},
}

var forageRu = map[string]string{"herb": "травы", "wood": "дрова", "ore": "руду", "fish": "рыбу", "veg": "ягоды и коренья"}

// запас урочища: сколько можно взять и сколько восполняется за сутки
const (
	wildStockCap   = 6.0
	wildStockRegen = 2.0
)

func wildHash(seed uint64, cx, cy int64, salt uint64) uint64 {
	h := seed ^ (uint64(cx) * 0x9E3779B97F4A7C15) ^ (uint64(cy) * 0xC2B2AE3D27D4EB4F) ^ (salt * 0x165667B19E3779F9)
	h ^= h >> 33
	h *= 0xFF51AFD7ED558CCD
	h ^= h >> 33
	h *= 0xC4CEB9FE1A85EC53
	h ^= h >> 33
	return h
}

func unit(h uint64) float64 { return float64(h>>11) / float64(1<<53) }

// wildCellPlace — урочище клетки (ok=false — клетка пуста). Чистая функция семени мира и клетки.
func wildCellPlace(t *Terrain, cx, cy int64) (WildPlace, bool) {
	seed := t.seed ^ 0xA5A5F00D
	if unit(wildHash(seed, cx, cy, 1)) > wildChance {
		return WildPlace{}, false
	}
	e := (float64(cx) + 0.15 + 0.7*unit(wildHash(seed, cx, cy, 2))) * wildCell
	n := (float64(cy) + 0.15 + 0.7*unit(wildHash(seed, cx, cy, 3))) * wildCell
	for _, a := range t.anchors { // в самом селе и у околицы урочищ нет
		if math.Hypot(e-a[0], n-a[1]) < 320 {
			return WildPlace{}, false
		}
	}
	// вид — по характеру места: лесистость, влага, высота над дном котла
	woods := noise(seed+11, e, n, 1900)
	wet := noise(seed+12, e, n, 2700)
	high := t.raw(e, n) - 120
	var pool []string
	switch {
	case high > 260:
		pool = []string{"outcrop", "den", "outcrop", "ruins", "camp"}
	case wet > 0.64:
		pool = []string{"pond", "spring", "pond", "herbs", "camp"}
	case woods > 0.56:
		pool = []string{"thicket", "berry", "hut", "thicket", "den", "herbs"}
	default:
		pool = []string{"camp", "herbs", "barrow", "ruins", "spring", "berry", "hut"}
	}
	k := wildKinds[pool[wildHash(seed, cx, cy, 4)%uint64(len(pool))]]
	z := 0.0
	if k.kind == "den" {
		z = -4 - 10*unit(wildHash(seed, cx, cy, 6))
	}
	return WildPlace{
		ID:   "w:" + strconv.FormatInt(cx, 10) + ":" + strconv.FormatInt(cy, 10),
		Name: k.names[wildHash(seed, cx, cy, 5)%uint64(len(k.names))], Kind: k.kind,
		E: e, N: n, Z: z, Danger: 0.15 + 0.7*unit(wildHash(seed, cx, cy, 7)),
	}, true
}

// WildPlaceByID — место по id: "w:cx:cy" — урочище, иначе id сущности крупного дикого места.
func WildPlaceByID(w *core.World, t *Terrain, id string) (WildPlace, bool) {
	if strings.HasPrefix(id, "w:") {
		parts := strings.Split(id, ":")
		if len(parts) != 3 {
			return WildPlace{}, false
		}
		cx, err1 := strconv.ParseInt(parts[1], 10, 64)
		cy, err2 := strconv.ParseInt(parts[2], 10, 64)
		if err1 != nil || err2 != nil || cx < -1<<30 || cx > 1<<30 || cy < -1<<30 || cy > 1<<30 {
			return WildPlace{}, false
		}
		return wildCellPlace(t, cx, cy)
	}
	n, err := strconv.ParseUint(id, 10, 64)
	if err != nil {
		return WildPlace{}, false
	}
	if se := w.Get(core.ID(n)); se != nil {
		if ws := WildSite(se); ws != nil {
			return wildFromEntity(w, se, ws), true
		}
	}
	return WildPlace{}, false
}

func wildFromEntity(w *core.World, se *core.Entity, ws *WildSiteC) WildPlace {
	p := EnsurePlanet(w)
	return WildPlace{ID: eidStr(se.ID), Name: ws.Name, Kind: ws.Type,
		E: (ws.X - 500) * p.KmPerUnit * 1000, N: (ws.Y - 500) * p.KmPerUnit * 1000, Z: ws.Z, Danger: ws.Danger}
}

// WildPlacesNear — места без жителей в радиусе от точки, ближние первыми.
func WildPlacesNear(w *core.World, t *Terrain, e, n, radius float64) []WildPlace {
	var out []WildPlace
	c0x, c1x := int64(math.Floor((e-radius)/wildCell)), int64(math.Floor((e+radius)/wildCell))
	c0y, c1y := int64(math.Floor((n-radius)/wildCell)), int64(math.Floor((n+radius)/wildCell))
	for cx := c0x; cx <= c1x; cx++ {
		for cy := c0y; cy <= c1y; cy++ {
			if p, ok := wildCellPlace(t, cx, cy); ok && math.Hypot(p.E-e, p.N-n) <= radius {
				out = append(out, p)
			}
		}
	}
	w.Each("wildsite", func(se *core.Entity) {
		if ws := WildSite(se); ws != nil {
			if p := wildFromEntity(w, se, ws); math.Hypot(p.E-e, p.N-n) <= radius {
				out = append(out, p)
			}
		}
	})
	sort.Slice(out, func(i, j int) bool {
		di, dj := math.Hypot(out[i].E-e, out[i].N-n), math.Hypot(out[j].E-e, out[j].N-n)
		if di != dj {
			return di < dj
		}
		return out[i].ID < out[j].ID
	})
	return out
}

func bodyWorldXY(w *core.World, e *core.Entity) (x, y float64, ok bool) {
	pl, p := core.Place(e), ScenePos(e)
	if pl == nil || p == nil || OnRoad(e) {
		return 0, 0, false
	}
	s := settleOf(w, pl.Settlement)
	if s == nil {
		return 0, 0, false
	}
	ax, ay := Anchor(w, s)
	return ax + p.X, ay + p.Y, true
}

// nearestWildSite — ближайшее место без жителей: имя, расстояние и сторона света (для «ока»).
func nearestWildSite(w *core.World, e *core.Entity) (name string, dist float64, dir string, ok bool) {
	x, y, has := bodyWorldXY(w, e)
	if !has {
		return "", 0, "", false
	}
	ps := WildPlacesNear(w, NewTerrain(w), x, y, wildNearDist)
	if len(ps) == 0 {
		return "", 0, "", false
	}
	p := ps[0]
	return p.Name, math.Hypot(p.E-x, p.N-y), compass(p.E-x, p.N-y), true
}

// --- что люди изменили в урочищах ---

func init() {
	core.Register("wilduse", func() core.Component { return &WildUseC{Stock: map[string]wildStock{}, Seen: map[string]int{}} })
}

type wildStock struct {
	Left float64 `json:"left"`
	Day  int     `json:"day"` // на какой день записан остаток (дальше восполняется)
}

// WildUseC — память мира об урочищах: остатки запасов и кто когда обыскал место.
type WildUseC struct {
	Stock map[string]wildStock `json:"stock"`
	Seen  map[string]int       `json:"seen"` // "<место>|<тело>" → день последнего обыска
	Foray map[string]forayMark `json:"foray,omitempty"` // место → когда и из какого села там были ходоки (v1.26)
}

func (c *WildUseC) Kind() string { return "wilduse" }

// peekWildUse — память об урочищах без создания (для кадра: чтение не должно менять мир).
func peekWildUse(w *core.World) *WildUseC {
	var c *WildUseC
	w.Each("wilduse", func(e *core.Entity) { c, _ = e.Get("wilduse").(*WildUseC) })
	return c
}

func wildUse(w *core.World) *WildUseC {
	c := peekWildUse(w)
	if c == nil {
		c = &WildUseC{Stock: map[string]wildStock{}, Seen: map[string]int{}}
		w.Spawn(c)
	}
	if c.Stock == nil {
		c.Stock = map[string]wildStock{}
	}
	if c.Seen == nil {
		c.Seen = map[string]int{}
	}
	return c
}

// wildLeft — сколько запаса сейчас в месте (с учётом восполнения по дням).
func wildLeft(u *WildUseC, key string, day int) float64 {
	if u == nil {
		return wildStockCap
	}
	st, ok := u.Stock[key]
	if !ok {
		return wildStockCap
	}
	return math.Min(wildStockCap, st.Left+float64(max(0, day-st.Day))*wildStockRegen)
}

const exploreEvery = 3 // раз во столько дней место можно обыскать заново

// SiteAct — дело в месте без жителей. action: rest | drink | explore | forage (что именно — решает
// вид места). Требует непустой reqid; повтор принятого reqid идемпотентен.
func SiteAct(w *core.World, e *core.Entity, siteID, action string, day int, reqid string, g *dice.Stream) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	x, y, ok := bodyWorldXY(w, e)
	if !ok {
		return false, "ты в пути"
	}
	site, ok := WildPlaceByID(w, NewTerrain(w), siteID)
	if !ok {
		return false, "нет такого места"
	}
	if math.Hypot(site.E-x, site.N-y) > wildReach {
		return false, "надо подойти ближе: " + site.Name
	}
	v := core.Vitals(e)
	if v == nil {
		return false, "некому"
	}
	kind := wildKinds[site.Kind]
	want := action
	if action == "forage" {
		want = ""
		for _, a := range kind.acts {
			if strings.HasPrefix(a, "forage:") {
				want = a
			}
		}
	}
	allowed := false
	for _, a := range kind.acts {
		if a == want {
			allowed = true
		}
	}
	if !allowed || want == "" {
		return false, "здесь этим не займёшься"
	}
	u := wildUse(w)
	switch {
	case want == "rest":
		if v.Energy >= 95 {
			return false, "ты и так полон сил"
		}
		key := siteID + "|rest|" + eidStr(e.ID)
		if last, seen := u.Seen[key]; seen && last == day {
			return false, "сегодня ты здесь уже отдыхал — второй привал сил не прибавит"
		}
		u.Seen[key] = day
		v.Energy = math.Min(100, v.Energy+30+restBonus(e))
		v.Hunger = math.Min(100, v.Hunger+4)
		say(w, e, "site", site.Name+": ты передохнул и собрался с силами.")
	case want == "drink":
		key := siteID + "|drink|" + eidStr(e.ID)
		if last, seen := u.Seen[key]; seen && last == day {
			return false, "ты уже напился здесь сегодня"
		}
		u.Seen[key] = day
		v.Energy = math.Min(100, v.Energy+10)
		v.Morale = math.Min(100, v.Morale+3)
		say(w, e, "site", site.Name+": холодная вода прояснила голову.")
	case want == "explore":
		if v.Energy < 10 {
			return false, "нет сил лазить по развалинам"
		}
		key := siteID + "|look|" + eidStr(e.ID)
		if last, seen := u.Seen[key]; seen && day-last < exploreEvery {
			return false, "ты недавно всё здесь обыскал — пусто"
		}
		u.Seen[key] = day
		v.Energy -= 10
		wildExplore(w, e, site, g)
	default: // forage:<товар>
		good := strings.TrimPrefix(want, "forage:")
		if v.Energy < 8 {
			return false, "нет сил"
		}
		if good == econ.GoodFish {
			if inv := Inv(e); inv == nil || inv.Hand != "rod" {
				return false, "без удочки в руке рыбу не взять"
			}
		}
		left := wildLeft(u, siteID, day)
		if left < 1 {
			return false, "здесь уже всё выбрано — дай месту отдохнуть день-другой"
		}
		u.Stock[siteID] = wildStock{Left: left - 1, Day: day}
		v.Energy -= 8
		addStash(w, e, good, 1)
		skill := map[string]string{"herb": "herbalist", "wood": "woodcutter", "ore": "miner", "fish": "fisher", "veg": "herbalist"}[good]
		trainSkill(w, e, skill, 0.5)
		say(w, e, "site", site.Name+": ты набрал "+forageRu[good]+".")
	}
	pc.markDone(reqid)
	return true, ""
}

// wildExplore — обыск развалин, кургана, норы, башни: находка или беда, по костям и опасности места.
func wildExplore(w *core.World, e *core.Entity, site WildPlace, g *dice.Stream) {
	TrainStat(w, e, "perception", 0.05)
	roll := g.Uniform(0, 1)
	inv := ensureInv(w, e)
	switch {
	case roll < 0.22*site.Danger+0.04: // беда: обвал, гнилой пол, потревоженный зверь
		hurt := 6 + 14*site.Danger*g.Uniform(0.5, 1)
		if v := core.Vitals(e); v != nil {
			v.Health = math.Max(6, v.Health-hurt)
		}
		Emit(w, e, FeedItem{Kind: "site", Text: site.Name + ": под ногой поехал камень — ты расшибся.", Dmg: math.Round(hurt)})
	case roll < 0.55:
		say(w, e, "site", site.Name+": старое, пустое место. Ничего, кроме пыли и тишины.")
	case roll < 0.80: // мелочь
		coins := math.Round(1 + 4*g.Uniform(0, 1))
		if pu := core.Purse(e); pu != nil {
			pu.Coins += coins
		}
		say(w, e, "site", site.Name+": в щели между камнями нашлось несколько старых монет.")
	default: // вещь, оставленная прежними хозяевами
		finds := []string{"torch", "rope", "knife", "bandage", "waterskin", "lantern", "arrow", "staff"}
		if site.Danger > 0.6 {
			finds = append(finds, "spear", "helmet", "potion")
		}
		it := finds[g.Intn(len(finds))]
		n := 1
		if it == "arrow" {
			n = 5
		}
		inv.Items[it] += n
		say(w, e, "site", site.Name+": тебе досталась находка — "+ItemName(it)+".")
	}
}

// SitesView — места без жителей вокруг игрока (place.sites): где, что за место, что там можно делать
// и стоит ли игрок уже рядом. Только ближайшие wildNearMax в радиусе wildNearDist.
func SitesView(w *core.World, t *Terrain, e *core.Entity, day int) []map[string]any {
	out := []map[string]any{}
	x, y, ok := bodyWorldXY(w, e)
	if !ok {
		return out
	}
	u := peekWildUse(w)
	for i, p := range WildPlacesNear(w, t, x, y, wildNearDist) {
		if i >= wildNearMax {
			break
		}
		d := math.Hypot(p.E-x, p.N-y)
		ground := t.Height(p.E, p.N)
		acts := []string{}
		for _, a := range wildKinds[p.Kind].acts {
			if strings.HasPrefix(a, "forage:") {
				a = "forage"
			}
			acts = append(acts, a)
		}
		row := map[string]any{"id": p.ID, "name": p.Name, "kind": p.Kind,
			"e": r3(p.E), "n": r3(p.N), "u": r3(ground + p.Z), "dist": math.Round(d), "dir": compass(p.E-x, p.N-y),
			"acts": acts, "here": d <= wildReach}
		for _, a := range wildKinds[p.Kind].acts {
			if strings.HasPrefix(a, "forage:") {
				row["gives"] = strings.TrimPrefix(a, "forage:")
				row["left"] = math.Floor(wildLeft(u, p.ID, day))
			}
		}
		if note := forayNote(u, p.ID, day); note != "" {
			row["note"] = note // v1.26: следы людей — сюда ходят из села
		}
		out = append(out, row)
	}
	return out
}
