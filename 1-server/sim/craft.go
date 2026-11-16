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
	// v1.22: больше еды, припасов, одежды и оружия — из того сырья, что уже добывает мир
	{"porridge", "Сварить кашу", "hearth", map[string]float64{"wheat": 1, "milk": 1}, "porridge", 1, false, "cook"},
	{"pie", "Испечь пирог", "hearth", map[string]float64{"flour": 1, "meat": 1, "wood": 1}, "pie", 1, false, "cook"},
	{"vegpie", "Испечь пирог с овощами", "hearth", map[string]float64{"flour": 1, "veg": 2, "wood": 1}, "pie", 1, false, "cook"},
	{"honeycake", "Испечь медовый пряник", "hearth", map[string]float64{"flour": 1, "honey": 1}, "honeycake", 2, false, "cook"},
	{"omelet", "Пожарить яичницу", "hearth", map[string]float64{"egg": 2}, "omelet", 1, false, "cook"},
	{"ration", "Собрать дорожный паёк", "hearth", map[string]float64{"bread": 1, "meat": 1}, "ration", 2, false, "cook"},
	{"fishration", "Навялить рыбы в дорогу", "hearth", map[string]float64{"fish": 2}, "ration", 2, false, "cook"},
	{"tonic", "Заварить бодрящий отвар", "hearth", map[string]float64{"herb": 1, "honey": 1}, "tonic", 1, false, "cook"},
	{"salve", "Растереть мазь от ран", "hearth", map[string]float64{"herb": 2, "honey": 1}, "salve", 1, false, "cook"},
	{"spear", "Насадить копьё", "smithy", map[string]float64{"metal": 1, "wood": 2}, "spear", 1, false, "smith"},
	{"mace", "Сковать булаву", "smithy", map[string]float64{"metal": 2, "wood": 1}, "mace", 1, false, "smith"},
	{"pickaxe", "Сковать кирку", "smithy", map[string]float64{"metal": 1, "wood": 1}, "pickaxe", 1, false, "smith"},
	{"helmet", "Выковать шлем", "smithy", map[string]float64{"metal": 2, "leather": 1}, "helmet", 1, false, "smith"},
	{"mail", "Сплести кольчугу", "smithy", map[string]float64{"metal": 5, "leather": 1}, "mail", 1, false, "smith"},
	{"crossbow", "Собрать самострел", "smithy", map[string]float64{"metal": 1, "wood": 3, "hide": 1}, "crossbow", 1, false, "smith"},
	{"staff", "Выстругать посох", "granary", map[string]float64{"wood": 2}, "staff", 1, false, "crafter"},
	{"fletch", "Оперить стрелы", "granary", map[string]float64{"wood": 1, "feather": 1}, "arrow", 8, false, "crafter"},
	{"shirt", "Сшить холщовую рубаху", "granary", map[string]float64{"linen": 2}, "shirt", 1, false, "crafter"},
	{"cloak", "Сшить суконный плащ", "granary", map[string]float64{"cloth": 3}, "cloak", 1, false, "crafter"},
	{"furcoat", "Сшить тулуп", "granary", map[string]float64{"hide": 3, "wool": 2}, "furcoat", 1, false, "crafter"},
	{"hat", "Свалять шапку", "granary", map[string]float64{"wool": 1}, "hat", 1, false, "crafter"},
	{"gloves", "Сшить рукавицы", "granary", map[string]float64{"wool": 1, "leather": 1}, "gloves", 1, false, "crafter"},
	{"boots", "Стачать сапоги", "granary", map[string]float64{"leather": 2}, "boots", 1, false, "crafter"},
	{"satchel", "Сшить холщовую суму", "granary", map[string]float64{"linen": 2}, "satchel", 1, false, "crafter"},
	{"backpack", "Сшить заплечный мешок", "granary", map[string]float64{"leather": 2, "linen": 1}, "backpack", 1, false, "crafter"},
	{"bedroll", "Скатать постель", "granary", map[string]float64{"wool": 2, "linen": 1}, "bedroll", 1, false, "crafter"},
	{"rope", "Свить верёвку", "granary", map[string]float64{"flax": 2}, "rope", 1, false, "crafter"},
	{"waterskin", "Сшить бурдюк", "granary", map[string]float64{"hide": 1}, "waterskin", 1, false, "crafter"},
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
	if OnRoad(e) || !atPoi(w, e, r.At) {
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
	// с киркой в руке тот же труд даётся легче (v1.22)
	energy := 12.0
	if inv := Inv(e); inv != nil && inv.Hand == "pickaxe" {
		energy = 8
	}
	return gatherAt(w, e, reqid, "mine", econ.GoodOre, energy, "miner", "надо подойти к руднику")
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
	if OnRoad(e) || !atPoi(w, e, poi) {
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
	fx, known := useEffects[item]
	if !known {
		return false, "это не едят и не прикладывают"
	}
	if fx.stash {
		if st.Goods[item] < 1 {
			return false, "такого нет в закромах"
		}
		st.Goods[item]--
	} else {
		if inv.Items[item] < 1 {
			return false, "такого нет в суме"
		}
		inv.Items[item]--
	}
	v.Hunger = math.Max(0, v.Hunger-fx.hunger)
	v.Energy = math.Min(100, v.Energy+fx.energy)
	v.Morale = math.Min(100, v.Morale+fx.morale)
	v.Health = math.Min(100, v.Health+fx.health)
	if b := battle.Body(e); b != nil {
		if fx.stopBleed {
			b.Bleeding = 0
		}
		if fx.mend > 0 {
			b.Mend(fx.mend)
		}
	}
	pc.markDone(reqid)
	return true, ""
}

// RecipesView — рецепты для кадра (me.recipes): что можно сделать и хватает ли сырья/места.
func RecipesView(w *core.World, e *core.Entity) []map[string]any {
	out := make([]map[string]any, 0, len(Recipes))
	for i := range Recipes {
		r := &Recipes[i]
		out = append(out, map[string]any{"id": r.ID, "name": r.Name, "at": r.At, "needs": r.Needs,
			"gives": r.Gives, "qty": r.Qty, "can": canCraft(e, r), "here": atPoi(w, e, r.At)})
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
