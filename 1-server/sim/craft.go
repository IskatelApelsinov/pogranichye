package sim

import (
	"math"
	"sort"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Ремесло игрока (v1.10): добыча сырья (руда, травы) и рецепты у мест работы — кузня, очаг, амбар-
// мастерская. Цепочки многошаговые: руда → железо → меч; шкура → кожа → куртка. Сырьё берётся из
// личных закромов (их наполняют труд, охота, рынок), изделие — настоящая вещь в суме.

// Recipe — рецепт: где делают, что тратится (товары закромов), что выходит.
type Recipe struct {
	ID    string             `json:"id"`
	Name  string             `json:"name"`
	At    string             `json:"at"` // POI: smithy|hearth|granary
	Needs map[string]float64 `json:"needs"`
	Gives string             `json:"gives"`
	Qty   int                `json:"qty"`
	Good  bool               `json:"good"` // true — выход в закрома (товар), иначе вещь в суму
	Skill string             `json:"skill"`
}

var Recipes = []Recipe{
	{"metal", "Выплавить железо", "smithy", map[string]float64{"ore": 2, "wood": 1}, "metal", 1, true, "smith"},
	{"knife", "Сковать нож", "smithy", map[string]float64{"metal": 1}, "knife", 1, false, "smith"},
	{"axe", "Сковать топор", "smithy", map[string]float64{"metal": 1, "wood": 1}, "axe", 1, false, "smith"},
	{"sword", "Сковать меч", "smithy", map[string]float64{"metal": 2, "wood": 1}, "sword", 1, false, "smith"},
	{"shield", "Сбить щит", "smithy", map[string]float64{"wood": 2, "hide": 1}, "shield", 1, false, "smith"},
	{"arrow", "Настрогать стрел", "granary", map[string]float64{"wood": 1}, "arrow", 4, false, "crafter"},
	{"bow", "Согнуть лук", "granary", map[string]float64{"wood": 2, "hide": 1}, "bow", 1, false, "crafter"},
	{"rod", "Сладить удочку", "granary", map[string]float64{"wood": 1}, "rod", 1, false, "crafter"},
	{"leather", "Выделать кожу", "granary", map[string]float64{"hide": 1, "wood": 1}, "leather", 1, true, "crafter"},
	{"jerkin", "Сшить кожаную куртку", "granary", map[string]float64{"leather": 2}, "jerkin", 1, false, "crafter"},
	{"purse", "Сшить кожаный кошель", "granary", map[string]float64{"leather": 1, "cloth": 1}, "purse", 1, false, "crafter"},
	{"torch", "Смотать факел", "hearth", map[string]float64{"wood": 1}, "torch", 1, false, "crafter"},
	{"bandage", "Нарезать повязок", "hearth", map[string]float64{"hide": 1}, "bandage", 2, false, "cook"},
	{"potion", "Сварить снадобье", "hearth", map[string]float64{"herb": 2}, "potion", 1, false, "cook"},
	{"stew", "Сварить уху", "hearth", map[string]float64{"fish": 1, "wood": 1}, "stew", 1, false, "cook"},
	{"roast", "Зажарить мясо", "hearth", map[string]float64{"meat": 1, "wood": 1}, "roast", 1, false, "cook"},
}

func init() { // новые вещи: что берут в руку и что носят
	handItem["knife"] = true
	wearSlot["jerkin"] = "armor"
	wearSlot["shield"] = "offhand"
	wearSlot["purse"] = "belt"
}

func recipeByID(id string) *Recipe {
	for i := range Recipes {
		if Recipes[i].ID == id {
			return &Recipes[i]
		}
	}
	return nil
}

func canCraft(e *core.Entity, r *Recipe) bool {
	st := econ.Stash(e)
	if st == nil {
		return false
	}
	for g, n := range r.Needs {
		if st.Goods[g] < n {
			return false
		}
	}
	return true
}

// Craft — сделать вещь по рецепту: нужно стоять у места работы, иметь сырьё и силы.
func Craft(w *core.World, e *core.Entity, id, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	r := recipeByID(id)
	if r == nil {
		return false, "нет такого рецепта"
	}
	if OnRoad(e) || !atPoi(e, r.At) {
		return false, "это делают не здесь: " + poiRu(r.At)
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < 5 {
		return false, "нет сил работать"
	}
	if !canCraft(e, r) {
		return false, "не хватает сырья"
	}
	st := ensureStashOf(w, e)
	for g, n := range r.Needs {
		st.Goods[g] -= n
	}
	v.Energy -= 5
	if r.Good {
		st.Goods[r.Gives] += float64(r.Qty)
	} else {
		ensureInv(w, e).Items[r.Gives] += r.Qty
	}
	trainSkill(w, e, r.Skill, 0.6)
	pc.markDone(reqid)
	say(w, e, "craft", r.Name+": готово.")
	return true, ""
}

func poiRu(p string) string {
	switch p {
	case "smithy":
		return "нужна кузня"
	case "hearth":
		return "нужен очаг"
	case "granary":
		return "нужна мастерская у амбара"
	}
	return p
}

// Mine — добыть руду на руднике (тяжёлый труд; с киркой-топором спорее).
func Mine(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return gatherAt(w, e, reqid, "mine", econ.GoodOre, 12, "miner", "надо подойти к руднику")
}

// Gather — собрать травы у огородов/лугов.
func Gather(w *core.World, e *core.Entity, reqid string) (bool, string) {
	return gatherAt(w, e, reqid, "garden", econ.GoodHerb, 5, "herbalist", "травы растут у огородов")
}

func gatherAt(w *core.World, e *core.Entity, reqid, poi, good string, energy float64, skill, far string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) || !atPoi(e, poi) {
		return false, far
	}
	if why := siteLacks(w, e, poi); why != "" {
		return false, why
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < energy {
		return false, "нет сил"
	}
	v.Energy -= energy
	addStash(w, e, good, 1)
	trainSkill(w, e, skill, 0.6)
	pc.markDone(reqid)
	return true, ""
}

// Use — употребить вещь/еду: уха, жаркое, снадобье, повязка; сырую еду из закромов.
func Use(w *core.World, e *core.Entity, item, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	v := core.Vitals(e)
	if v == nil {
		return false, "некому"
	}
	inv := ensureInv(w, e)
	st := ensureStashOf(w, e)
	fromInv := inv.Items[item] > 0
	switch item {
	case "stew", "roast", "potion", "bandage":
		if !fromInv {
			return false, "такого нет в суме"
		}
		inv.Items[item]--
	case "bread", "fish", "meat", "cheese", "veg", "game", "egg":
		if st.Goods[item] < 1 {
			return false, "такого нет в закромах"
		}
		st.Goods[item]--
	default:
		return false, "это не едят и не прикладывают"
	}
	switch item {
	case "stew":
		v.Hunger = math.Max(0, v.Hunger-60)
		v.Energy = math.Min(100, v.Energy+10)
	case "roast":
		v.Hunger = math.Max(0, v.Hunger-50)
		v.Morale = math.Min(100, v.Morale+5)
	case "potion":
		v.Health = math.Min(100, v.Health+25)
		if b := battle.Body(e); b != nil {
			b.Mend(8)
		}
	case "bandage":
		if b := battle.Body(e); b != nil {
			b.Bleeding = 0
			b.Mend(5)
		}
		v.Health = math.Min(100, v.Health+8)
	default:
		v.Hunger = math.Max(0, v.Hunger-30)
	}
	pc.markDone(reqid)
	return true, ""
}

// RecipesView — рецепты для кадра (me.recipes): что можно сделать и хватает ли сырья/места.
func RecipesView(e *core.Entity) []map[string]any {
	out := make([]map[string]any, 0, len(Recipes))
	for i := range Recipes {
		r := &Recipes[i]
		out = append(out, map[string]any{"id": r.ID, "name": r.Name, "at": r.At, "needs": r.Needs,
			"gives": r.Gives, "qty": r.Qty, "can": canCraft(e, r), "here": atPoi(e, r.At)})
	}
	return out
}

// MarketView — прилавок поселения (place.market): что лежит на складе и почём (спрос/предложение).
func MarketView(se *core.Entity, e *core.Entity) []map[string]any {
	out := []map[string]any{}
	s := core.Settle(se)
	if s == nil {
		return out
	}
	seen := map[string]bool{}
	row := func(g string) {
		if seen[g] || g == BeerGood {
			return
		}
		seen[g] = true
		p := econ.Price(se, g)
		if p <= 0 {
			p = 1
		}
		out = append(out, map[string]any{"good": g, "stock": math.Floor(s.Store[g]), "price": round1x(p), "sell": round1x(p * 0.9)})
	}
	for g, n := range s.Store {
		if n >= 1 {
			row(g)
		}
	}
	if st := econ.Stash(e); st != nil {
		for g, n := range st.Goods {
			if n >= 1 {
				row(g)
			}
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i]["good"].(string) < out[j]["good"].(string) })
	return out
}
