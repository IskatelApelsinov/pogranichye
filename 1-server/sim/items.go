package sim

import (
	"math"
	"sort"

	"pogranichye/core"
	"pogranichye/econ"
)

// Каталог вещей (v1.22). Раньше свойства вещей были размазаны по коду: цена и место продажи — в
// одной таблице, слот — в другой, урон — в третьей, а одежда не делала ничего. Теперь у вещи одно
// описание, из которого заполняются прежние таблицы (itemSource, itemPrice, wearSlot, handItem,
// weapons) — старый код и старые сохранения работают как раньше, а новая вещь добавляется одной
// строкой и сразу продаётся, надевается и действует.
//
// Клиенты получают прилавок из кадра (place.wares) и названия оттуда же: добавление вещи на сервере
// не требует правки клиентов.

// ItemDef — описание вещи.
type ItemDef struct {
	ID    string
	Name  string  // как назвать игроку
	At    string  // у какого места села продают ("" — не продают, только делают сами)
	Price float64 // цена в лавке
	Slot  string  // куда надевают ("" — не носят)
	Hand  bool    // берут в руку
	Armor float64 // доля удара, которую гасит надетая вещь (0.25 — четверть)
	Block float64 // во сколько раз сильнее блок со щитом (0 — не щит)
	Carry float64 // прибавка к вместимости ноши
	Pace  float64 // прибавка к скорости ходьбы (0.06 — на 6% быстрее)
	Rest  float64 // прибавка к отдыху на привале в диком месте
}

// ItemDefs — все вещи с описанием. Порядок — как на прилавке.
var ItemDefs = []ItemDef{
	// инструмент и оружие
	{ID: "axe", Name: "топор", At: "woods", Price: 8, Hand: true},
	{ID: "rod", Name: "удочка", At: "river", Price: 6, Hand: true},
	{ID: "knife", Name: "нож", At: "smithy", Price: 5, Hand: true},
	{ID: "sword", Name: "меч", At: "smithy", Price: 18, Hand: true},
	{ID: "spear", Name: "копьё", At: "smithy", Price: 11, Hand: true},
	{ID: "mace", Name: "булава", At: "smithy", Price: 14, Hand: true},
	{ID: "staff", Name: "посох", At: "woods", Price: 3, Hand: true},
	{ID: "bow", Name: "лук", At: "smithy", Price: 15, Hand: true},
	{ID: "crossbow", Name: "самострел", At: "smithy", Price: 26, Hand: true},
	{ID: "pickaxe", Name: "кирка", At: "smithy", Price: 9, Hand: true},
	{ID: "torch", Name: "факел", At: "market", Price: 2, Hand: true},
	{ID: "lantern", Name: "фонарь", At: "market", Price: 6, Hand: true},
	// одежда и доспех
	{ID: "hat", Name: "шапка", At: "granary", Price: 3, Slot: "head"},
	{ID: "helmet", Name: "шлем", At: "smithy", Price: 12, Slot: "head", Armor: 0.08},
	{ID: "shirt", Name: "холщовая рубаха", At: "granary", Price: 4, Slot: "shirt"},
	{ID: "cloak", Name: "плащ", At: "granary", Price: 8, Slot: "body", Rest: 4},
	{ID: "furcoat", Name: "тулуп", At: "", Slot: "body", Armor: 0.05, Rest: 8},
	{ID: "jerkin", Name: "кожаная куртка", At: "", Slot: "armor", Armor: 0.25},
	{ID: "mail", Name: "кольчуга", At: "smithy", Price: 34, Slot: "armor", Armor: 0.4},
	{ID: "gloves", Name: "рукавицы", At: "granary", Price: 3, Slot: "hands", Armor: 0.03},
	{ID: "boots", Name: "сапоги", At: "granary", Price: 6, Slot: "feet", Pace: 0.06},
	{ID: "shield", Name: "щит", At: "", Slot: "offhand", Block: 1},
	{ID: "purse", Name: "кожаный кошель", At: "", Slot: "belt"},
	// ноша и припасы
	{ID: "backpack", Name: "заплечный мешок", At: "market", Price: 14, Slot: "back", Carry: 30},
	{ID: "satchel", Name: "холщовая сума", At: "market", Price: 6, Slot: "back", Carry: 12},
	{ID: "bedroll", Name: "скатка-постель", At: "market", Price: 7, Rest: 12},
	{ID: "rope", Name: "верёвка", At: "market", Price: 3},
	{ID: "waterskin", Name: "бурдюк", At: "market", Price: 4},
	{ID: "arrow", Name: "стрелы"},
	{ID: "bandage", Name: "повязка"},
	{ID: "potion", Name: "снадобье"},
	{ID: "tonic", Name: "бодрящий отвар"},
	{ID: "salve", Name: "мазь от ран"},
	{ID: "stew", Name: "уха"},
	{ID: "roast", Name: "жаркое"},
	{ID: "porridge", Name: "каша"},
	{ID: "pie", Name: "пирог"},
	{ID: "honeycake", Name: "медовый пряник"},
	{ID: "ration", Name: "дорожный паёк"},
	{ID: "omelet", Name: "яичница"},
}

var itemDefByID = map[string]*ItemDef{}

func init() {
	for i := range ItemDefs {
		d := &ItemDefs[i]
		itemDefByID[d.ID] = d
		if d.At != "" && d.Price > 0 {
			itemSource[d.ID], itemPrice[d.ID] = d.At, d.Price
		}
		if d.Slot != "" {
			wearSlot[d.ID] = d.Slot
		}
		if d.Hand {
			handItem[d.ID] = true
		}
	}
	// новое оружие: копьё достаёт дальше, булава бьёт тяжелее, посох — длинная палка, самострел бьёт
	// сильнее лука, кирка — как топор, но ею ещё и руду берут
	weapons["spear"] = weaponStat{dmg: 13, reach: 3.2, skill: "polearm"}
	weapons["mace"] = weaponStat{dmg: 16, reach: 2.1, skill: "blunt"}
	weapons["staff"] = weaponStat{dmg: 7, reach: 2.9, skill: "polearm"}
	weapons["pickaxe"] = weaponStat{dmg: 11, reach: 2.0, skill: "blunt"}
	weapons["crossbow"] = weaponStat{dmg: 23, reach: 40, skill: "archery", ranged: true}
}

// ItemName — название вещи для игрока.
func ItemName(id string) string {
	if d := itemDefByID[id]; d != nil {
		return d.Name
	}
	return id
}

// wornSum — сумма свойства по всему надетому.
func wornSum(e *core.Entity, get func(*ItemDef) float64) float64 {
	inv := Inv(e)
	if inv == nil {
		return 0
	}
	sum := 0.0
	for _, it := range inv.Worn {
		if d := itemDefByID[it]; d != nil {
			sum += get(d)
		}
	}
	return sum
}

// armorMul — во сколько раз надетое ослабляет удар (1 — ничего не надето; не ниже 0.45).
func armorMul(e *core.Entity) float64 {
	return math.Max(0.45, 1-wornSum(e, func(d *ItemDef) float64 { return d.Armor }))
}

// carryBonus — прибавка к вместимости ноши от надетого мешка или сумы.
func carryBonus(e *core.Entity) float64 {
	return wornSum(e, func(d *ItemDef) float64 { return d.Carry })
}

// paceBonus — прибавка к скорости от обуви.
func paceBonus(e *core.Entity) float64 { return wornSum(e, func(d *ItemDef) float64 { return d.Pace }) }

// restBonus — насколько лучше отдых на привале: тёплая одежда на теле и постель в поклаже.
func restBonus(e *core.Entity) float64 {
	b := wornSum(e, func(d *ItemDef) float64 { return d.Rest })
	if inv := Inv(e); inv != nil && inv.Items["bedroll"] > 0 {
		b += itemDefByID["bedroll"].Rest
	}
	return b
}

// CatalogView — каталог мира для клиентов (/api/catalog): что продают и у какого места, свойства
// вещей, названия вещей и товаров, что можно съесть или приложить, рецепты и узлы плетений. Каталог
// одинаков для всех сёл и не меняется на ходу, поэтому отдаётся отдельно, а не в каждом кадре.
// Покупка, надевание и употребление всё равно проверяются сервером при действии.
func CatalogView() map[string]any {
	items := make([]map[string]any, 0, len(ItemDefs))
	names := map[string]string{}
	for i := range ItemDefs {
		d := &ItemDefs[i]
		names[d.ID] = d.Name
		it := map[string]any{"item": d.ID, "name": d.Name}
		if d.At != "" && d.Price > 0 {
			it["at"], it["price"] = d.At, d.Price
		}
		if d.Slot != "" {
			it["slot"] = d.Slot
		}
		if d.Hand {
			it["hand"] = true
		}
		if ws, ok := weapons[d.ID]; ok {
			it["weapon"] = map[string]any{"dmg": ws.dmg, "reach": ws.reach, "skill": ws.skill, "ranged": ws.ranged}
		}
		for k, v := range map[string]float64{"armor": d.Armor, "carry": d.Carry, "pace": d.Pace, "rest": d.Rest} {
			if v != 0 {
				it[k] = v
			}
		}
		if fx, ok := useEffects[d.ID]; ok {
			it["use"] = map[string]any{"hunger": fx.hunger, "energy": fx.energy, "morale": fx.morale, "health": fx.health}
		}
		items = append(items, it)
	}
	for g, n := range goodNames {
		names[g] = n
	}
	recipes := make([]map[string]any, 0, len(Recipes))
	for i := range Recipes {
		r := &Recipes[i]
		recipes = append(recipes, map[string]any{"id": r.ID, "name": r.Name, "at": r.At, "needs": r.Needs, "gives": r.Gives, "qty": r.Qty})
	}
	return map[string]any{"v": 1, "items": items, "names": names, "usables": UsablesView(), "recipes": recipes,
		"weave_nodes": WeaveNodes, "weave_schemas": schemaLore}
}

var goodNames = map[string]string{
	econ.GoodWheat: "зерно", econ.GoodFlour: "мука", econ.GoodBread: "хлеб", econ.GoodFish: "рыба",
	econ.GoodGame: "дичь", econ.GoodVeg: "овощи", econ.GoodHay: "сено", econ.GoodOre: "руда",
	econ.GoodMetal: "железо", econ.GoodTools: "снасть", econ.GoodWood: "дрова", econ.GoodMilk: "молоко",
	econ.GoodCheese: "сыр", econ.GoodWool: "шерсть", econ.GoodCloth: "сукно", econ.GoodEgg: "яйца",
	econ.GoodFeather: "перо", econ.GoodMeat: "мясо", econ.GoodHide: "шкура", econ.GoodLeather: "кожа",
	econ.GoodFlax: "лён", econ.GoodLinen: "холст", econ.GoodHerb: "травы", econ.GoodRemedy: "лекарство",
	econ.GoodHoney: "мёд", econ.GoodMead: "медовуха",
}

// useEffect — что делает употреблённая вещь или еда.
type useEffect struct {
	stash                          bool    // берётся из закромов (сырая еда), а не из сумы
	hunger, energy, morale, health float64 // сытость (минус голод), силы, дух, здоровье
	mend                           float64 // заживление ран тела
	stopBleed                      bool
}

var useEffects = map[string]useEffect{
	// готовая еда (сума)
	"stew":      {hunger: 60, energy: 10},
	"roast":     {hunger: 50, morale: 5},
	"porridge":  {hunger: 45, energy: 6},
	"pie":       {hunger: 70, morale: 6},
	"honeycake": {hunger: 20, morale: 12, energy: 4},
	"ration":    {hunger: 40},
	"omelet":    {hunger: 35, energy: 5},
	// лечение и отвары
	"potion":  {health: 25, mend: 8},
	"bandage": {health: 8, mend: 5, stopBleed: true},
	"salve":   {health: 14, mend: 14, stopBleed: true},
	"tonic":   {energy: 30, morale: 3},
	// сырая еда (закрома)
	"bread": {stash: true, hunger: 30}, "fish": {stash: true, hunger: 30}, "meat": {stash: true, hunger: 30},
	"cheese": {stash: true, hunger: 30}, "veg": {stash: true, hunger: 30}, "game": {stash: true, hunger: 30},
	"egg": {stash: true, hunger: 30}, "honey": {stash: true, hunger: 12, morale: 6},
	"milk": {stash: true, hunger: 15, energy: 3},
}

// UsablesView — что можно съесть или приложить (me.usables), по алфавиту: клиент рисует кнопки отсюда.
func UsablesView() []string {
	out := make([]string, 0, len(useEffects))
	for k := range useEffects {
		out = append(out, k)
	}
	sort.Strings(out)
	return out
}

var _ = core.ID(0)
