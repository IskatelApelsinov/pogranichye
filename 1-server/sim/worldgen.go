package sim

import (
	"fmt"

	"pogranichye/arcane"
	"pogranichye/battle"
	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

const mapSize = 1000.0 // сторона карты мира в верстах (для путей, торговли, досягаемости налога)

const mageSparkChance = 0.015 // редкая врождённая искра дара (латент); пробуждение — отдельно (magic.go)

var villageNames = []string{"Белогорье", "Отрадное", "Соловьёво", "Усть-Ворон", "Лещина",
	"Яворово", "Тихий Брод", "Межгорье", "Краснолесье", "Студёное", "Вербное", "Залесье",
	"Каменка", "Рыбное", "Горелово", "Светлояр", "Новосёлки", "Дубрава", "Погост", "Броды"}

var realmNames = []string{"Залесская земля", "Понизовье", "Горный удел"}

// Generate создаёт живой мир-затравку с экономикой на законе сохранения: у поселений есть
// рельеф и КОНЕЧНЫЕ угодья (пашни, лес, пастбище, где можно — рыбное место и шахта), склад
// с запасом еды и семян, жители с ремёслами по весам (пахарей больше — хлеб основа). Державы
// делят землю и берут десятину. Разброс — через кости, без клонов и идеальной ровности.
func Generate(seed string, settlements int) (*core.World, *dice.Hub) {
	w := core.NewWorld(seed)
	hub := dice.NewHub(seed)
	g := hub.Stream("worldgen")

	regions := makeRegions(w, g, settlements)

	var settleIDs []core.ID
	for i := 0; i < settlements; i++ {
		reg := regions[i%len(regions)] // расселяем по краям по кругу — кластеры, а не каша
		se := NewSettlement(w, g, settlementName(w, g, i), reg)
		pop := g.Between(6, 10)
		var residents []*core.Entity
		for j := 0; j < pop; j++ {
			p := spawnPerson(w, g, se.ID)
			// костяк деревни задаём жёстко, чтобы не было нежизнеспособных (без пахарей/повара
			// деревня обречена голодать). Остальные — по весам ремёсел.
			switch j {
			case 0, 1:
				setRole(p, "farmer")
			case 2:
				setRole(p, "cook")
			case 3:
				setRole(p, "herder") // кто-то пасёт скот — молоко/шерсть/мясо
			}
			residents = append(residents, p)
		}
		if len(residents) > 0 {
			k := residents[g.Intn(len(residents))]
			core.Ident(k).Role = "tavernkeeper"
			core.Settle(se).Keeper = k.ID
		}
		settleIDs = append(settleIDs, se.ID)
	}
	makeRealms(w, g, settleIDs)                             // державы делят землю и берут десятину
	w.Spawn(&chronicle.ChronicleC{Tally: map[string]int{}}) // летопись мира — память о заметных событиях
	chronicle.Record(w, 0, "начало", fmt.Sprintf("мир начался: %d поселений", len(settleIDs)))
	return w, hub
}

// GenerateStarter — ОДНА стартовая деревня для начала игры и тестов тонкого клиента: предсказуемый,
// самодостаточный мирок с полным живым составом (все ключевые ремёсла, несколько пар для прироста),
// запасами на гладкий старт. Детерминирован от семени — удобно подключаться всегда к одному месту.
func GenerateStarter(seed string) (*core.World, *dice.Hub) {
	w := core.NewWorld(seed)
	hub := dice.NewHub(seed)
	g := hub.Stream("worldgen")

	reg := w.Spawn(&core.RegionC{Name: "Ополье", Biome: "plain", X: 500, Y: 500, Mana: 1.0})
	se := NewSettlement(w, g, "Починок", reg)
	// припасы на гладкий старт: сверх базового склада немного руды/снадобья/инструмента
	st := core.Settle(se).Store
	st[econ.GoodOre] += 40
	st[econ.GoodRemedy] += 20
	st[econ.GoodTools] += 10
	// шахта, чтобы кузнецу/рудокопу было из чего работать (равнина сама по себе без руды)
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeMine, Good: econ.GoodOre,
		Settlement: se.ID, Stock: 500, Cap: 600, Regen: 0})

	// состав деревни: роль, пол, возраст, свой ли дом
	roster := []struct {
		role string
		sex  string
		age  float64
		home bool
	}{
		{"farmer", "м", 34, true}, {"farmer", "ж", 31, true},
		{"farmer", "м", 27, false}, {"cook", "ж", 40, true},
		{"herder", "м", 45, true}, {"hunter", "м", 29, false},
		{"herbalist", "ж", 38, true}, {"smith", "м", 42, true},
		{"woodcutter", "м", 24, false}, {"weaver", "ж", 33, true},
		{"miner", "м", 36, false}, {"tavernkeeper", "ж", 48, true},
		{"idler", "м", 7, false}, {"idler", "ж", 4, false}, // дети
		// больше народу — деревня живёт полнее
		{"farmer", "м", 52, true}, {"farmer", "ж", 48, true}, {"farmer", "м", 19, false},
		{"fisher", "м", 35, true}, {"fisher", "ж", 30, false}, {"hunter", "м", 41, true},
		{"woodcutter", "м", 33, true}, {"miner", "м", 28, false}, {"smith", "м", 23, false},
		{"weaver", "ж", 26, false}, {"cook", "ж", 55, true}, {"herder", "ж", 22, false},
		{"herbalist", "ж", 60, true}, {"idler", "м", 12, false}, {"idler", "ж", 9, false},
		{"idler", "м", 2, false}, {"farmer", "м", 44, true}, {"farmer", "ж", 39, true},
	}
	var people []*core.Entity
	for _, r := range roster {
		people = append(people, starterPerson(w, g, se.ID, r.role, r.sex, r.age, r.home))
	}
	// корчмарь — хозяин села
	core.Settle(se).Keeper = people[11].ID
	// две семьи — чтобы деревня прирастала сама (М+Ж в паре)
	bond(w, people[0], people[1]) // пахари-супруги
	bond(w, people[4], people[6]) // пастух и знахарка

	ids := []core.ID{se.ID}
	// соседние деревни — стартовая локация это ЦЕНТР «Мира», прочее достижимо путешествием
	for _, hn := range []string{"Берёзовка", "Залесье", "Крутой Яр"} {
		hse := NewSettlement(w, g, hn, reg)
		ids = append(ids, hse.ID)
		hroster := []struct {
			role string
			sex  string
			age  float64
			home bool
		}{
			{"farmer", "м", 33, true}, {"farmer", "ж", 30, true}, {"fisher", "м", 28, false},
			{"smith", "м", 40, true}, {"hunter", "м", 26, false}, {"tavernkeeper", "ж", 44, true},
			{"weaver", "ж", 35, true}, {"herder", "м", 50, true}, {"cook", "ж", 37, true},
			{"idler", "м", 8, false}, {"idler", "ж", 6, false},
		}
		var hp []*core.Entity
		for _, r := range hroster {
			hp = append(hp, starterPerson(w, g, hse.ID, r.role, r.sex, r.age, r.home))
		}
		core.Settle(hse).Keeper = hp[5].ID
		bond(w, hp[0], hp[1])
	}
	makeRealms(w, g, ids)
	generateOuterWorld(w, g) // внешний мир: десятки деревень и несколько городов за горами (телепорт)
	w.Each("settlement", func(se *core.Entity) { EnsureTavernRooms(w, se) })
	return w, hub
}

// generateOuterWorld создаёт «начало конечной карты»: ~20 деревень и несколько городов в отдельных
// регионах за горами. Достижимы только телепортом (op:"travel"); пешком — никак. Нагружает сервер для
// теста мощности. Население на поселение держим умеренным, но суммарно — сотни сущностей.
func generateOuterWorld(w *core.World, g *dice.Stream) {
	roles := []string{"farmer", "farmer", "fisher", "smith", "hunter", "woodcutter", "miner", "weaver", "cook", "herder", "herbalist", "tavernkeeper"}
	sexes := []string{"м", "ж"}
	villageNames := []string{"Дубрава", "Лозовая", "Ивантеево", "Боровое", "Калинов", "Медвежий Лог", "Сосновка", "Горелое", "Тихий Брод", "Вязники", "Соколье", "Рябинино", "Мшары", "Устье", "Белёво", "Кремёнки", "Тёмный Яр", "Ольховка", "Погост", "Залучье"}
	cityNames := []string{"Белокамень", "Торжище", "Златоверхий", "Каменный Стол", "Воронец"}
	regions := []string{"Залесский край", "Поморье", "Каменные горы", "Дикое поле"}
	// регионы внешнего мира
	var regIDs []*core.Entity
	for i, rn := range regions {
		regIDs = append(regIDs, w.Spawn(&core.RegionC{Name: rn, Biome: "plain", Outer: true,
			X: 1000 + float64(i*400), Y: 1000 + float64(i*250), Mana: 0.8 + 0.1*float64(i%3)}))
	}
	// деревни
	for i, nm := range villageNames {
		reg := regIDs[i%len(regIDs)]
		se := NewSettlement(w, g, nm, reg)
		n := 6 + i%4
		var first *core.Entity
		for j := 0; j < n; j++ {
			role := roles[(i+j)%len(roles)]
			sex := sexes[(i + j) % 2]
			age := 18 + float64((i*7+j*5)%50)
			p := starterPerson(w, g, se.ID, role, sex, age, j%3 == 0)
			if first == nil {
				first = p
			}
		}
		if first != nil {
			core.Settle(se).Keeper = first.ID
		}
	}
	// города — крупнее
	for i, nm := range cityNames {
		reg := regIDs[i%len(regIDs)]
		se := NewSettlement(w, g, nm, reg)
		core.Settle(se).Tier = "city"
		n := 18 + i*3
		var first *core.Entity
		for j := 0; j < n; j++ {
			role := roles[(i*3+j)%len(roles)]
			sex := sexes[(i + j) % 2]
			age := 18 + float64((i*11+j*3)%55)
			p := starterPerson(w, g, se.ID, role, sex, age, j%3 == 0)
			if first == nil {
				first = p
			}
		}
		if first != nil {
			core.Settle(se).Keeper = first.ID
		}
	}
}

// starterPerson — житель стартовой деревни с заданной ролью/полом/возрастом (азы ремесла, монеты).
func starterPerson(w *core.World, g *dice.Stream, se core.ID, role, sex string, age float64, owned bool) *core.Entity {
	ident := makeIdentity(g, sex, "", role)
	home := "none"
	if owned {
		home = "owned"
	}
	skill := 10.0
	if role == "idler" {
		skill = 0
	}
	e := w.Spawn(
		ident,
		&core.VitalsC{Age: age, Health: 95, Hunger: 20, Energy: 85, Morale: 70},
		&core.DrivesC{Ambition: g.Uniform(0, 40), Social: g.Uniform(0, 40), Dwelling: g.Uniform(0, 40)},
		&core.MindC{Goal: "", LastAct: "—"},
		&core.SkillsC{Skills: map[string]float64{role: skill}},
		&core.PurseC{Coins: 30, Debts: map[string]float64{}},
		&core.HomeC{Status: home, Comfort: 0, Settlement: se},
		&core.PlaceC{Settlement: se},
	)
	return e
}

// NewSettlement создаёт ПОСЕЛЕНИЕ с рельефом и конечными угодьями (без жителей) — общий
// код для стартовой генерации и для ОСНОВАНИЯ новых деревень живым миром. Так мир растёт
// не только населением, но и числом мест.
func NewSettlement(w *core.World, g *dice.Stream, name string, region *core.Entity) *core.Entity {
	// положение и рельеф диктует КРАЙ (биом): у побережья — рыбное место, в холмах — руда и т.д.
	x, y := g.Uniform(0, mapSize), g.Uniform(0, mapSize)
	biome := "plain"
	mana := 1.0
	var regID core.ID
	if region != nil {
		if r := core.Region(region); r != nil {
			biome = r.Biome
			x = r.X + g.Uniform(-110, 110) // внешний мир продолжает общую плоскость за пределами стартового квадрата
			y = r.Y + g.Uniform(-110, 110)
			if !r.Outer { // только стартовая область ограничена своим legacy map square
				x = clampf(x, 0, mapSize)
				y = clampf(y, 0, mapSize)
			}
			if r.Mana > 0 {
				mana = clampf(r.Mana*g.Uniform(0.8, 1.2), 0.3, 2.2) // место наследует ману края с разбросом
			}
		}
		regID = region.ID
	}
	coastal := biome == "coast" || (biome != "hills" && g.Chance(0.25))
	mountain := biome == "hills" || (biome != "coast" && g.Chance(0.2))

	se := w.Spawn(&core.SettlementC{
		Name: name, Founded: 0, Tier: "village", Tavern: true,
		X: x, Y: y, Region: regID, Mana: mana,
		Coin: 600, // монеты в обороте рынка на старте (ходят покупатель→котёл→работник)
		Store: map[string]float64{
			econ.GoodBread: 400,
			econ.GoodWheat: econ.SowReserve() + 160, // посевной резерв + немного на кашу
			econ.GoodWood:  30,
			BeerGood:       12,
		},
	})
	// живая фауна угодий: мелкая дичь везде, крупный зверь гуще там, где мана плотнее (канон)
	w.Attach(se, battle.SeedFauna(biome, mana, g))
	for f := 0; f < 5; f++ { // пять полей, одно под паром (трёхполье упрощённо)
		w.Spawn(&econ.ResourceNodeC{Node: econ.NodeField, Good: econ.GoodWheat,
			Settlement: se.ID, Fallow: f == 4})
	}
	// лес гуще в лесном краю, пастбище шире в степи — живая география
	forestStock := 400.0
	pastureStock := 200.0
	if biome == "forest" {
		forestStock = 700
	}
	if biome == "steppe" {
		pastureStock = 350
	}
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeForest, Good: econ.GoodWood,
		Settlement: se.ID, Stock: forestStock, Cap: forestStock, Regen: 3})
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodePasture, Good: econ.GoodGame,
		Settlement: se.ID, Stock: pastureStock, Cap: pastureStock, Regen: 4})
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeGarden, Good: econ.GoodVeg, Settlement: se.ID})
	// промыслы: лён (ткачу на холст), травы (знахарю на снадобье), пасека (мёд на медовуху)
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeFlax, Good: econ.GoodFlax, Settlement: se.ID})
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeHerbs, Good: econ.GoodHerb, Settlement: se.ID})
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeApiary, Good: econ.GoodHoney, Settlement: se.ID})
	herdKind := []string{econ.HerdCattle, econ.HerdSheep, econ.HerdPoultry}[g.Intn(3)]
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeHerd, HerdKind: herdKind,
		Settlement: se.ID, Stock: g.Uniform(15, 30), Cap: 45, Regen: 0.4})
	if coastal {
		w.Spawn(&econ.ResourceNodeC{Node: econ.NodeFishery, Good: econ.GoodFish,
			Settlement: se.ID, Stock: 300, Cap: 300, Regen: 6})
	}
	if mountain {
		w.Spawn(&econ.ResourceNodeC{Node: econ.NodeMine, Good: econ.GoodOre,
			Settlement: se.ID, Stock: g.Uniform(300, 600), Cap: 600, Regen: 0})
	}
	return se
}

var biomeNames = map[string][]string{
	"plain":  {"Ополье", "Равнина", "Раздолье"},
	"forest": {"Дубрава", "Чащоба", "Бор"},
	"hills":  {"Пригорье", "Каменный край", "Увалы"},
	"coast":  {"Поморье", "Взморье", "Лукоморье"},
	"steppe": {"Дикое поле", "Ковыльный край", "Степь"},
}
var biomeList = []string{"plain", "forest", "hills", "coast", "steppe"}

// makeRegions создаёт края-контейнеры с центрами и биомами — каркас материка.
func makeRegions(w *core.World, g *dice.Stream, settlements int) []*core.Entity {
	n := settlements/6 + 1 // примерно край на ~6 поселений
	var out []*core.Entity
	for i := 0; i < n; i++ {
		biome := biomeList[g.Intn(len(biomeList))]
		names := biomeNames[biome]
		re := w.Spawn(&core.RegionC{
			Name:  names[g.Intn(len(names))],
			Biome: biome,
			X:     g.Uniform(80, mapSize-80),
			Y:     g.Uniform(80, mapSize-80),
			Mana:  regionMana(g), // место силы / мёртвая зона / обычный край
		})
		out = append(out, re)
	}
	return out
}

// regionMana задаёт плотность маны края: большинство земель обычные (~1), изредка попадается
// место силы (мана гуще — там и колдовать легче, и опаснейший зверь кучнее) или мёртвая зона
// (маны мало — зверь смирный, магия чахнет). Так по миру разбросаны «горячие» и «глухие» углы.
func regionMana(g *dice.Stream) float64 {
	r := g.Uniform(0, 1)
	switch {
	case r < 0.15:
		return g.Uniform(1.4, 2.0) // место силы
	case r < 0.3:
		return g.Uniform(0.4, 0.7) // мёртвая зона
	default:
		return g.Uniform(0.85, 1.2) // обычный край
	}
}

func clampf(x, lo, hi float64) float64 {
	if x < lo {
		return lo
	}
	if x > hi {
		return hi
	}
	return x
}

// settlementName выдаёт имя: из списка, пока хватает, затем процедурно (мир растёт — имён
// фиксированного списка не напасёшься).
func settlementName(w *core.World, g *dice.Stream, i int) string {
	if i < len(villageNames) {
		return villageNames[i]
	}
	return procToponym(g)
}

func makeRealms(w *core.World, g *dice.Stream, settles []core.ID) {
	if len(settles) == 0 {
		return
	}
	nRealms := 1 + len(settles)/5 // примерно одна держава на ~5 поселений; БЕЗ потолка (имён хватит)
	buckets := make([][]core.ID, nRealms)
	for i, sid := range settles {
		b := i % nRealms
		buckets[b] = append(buckets[b], sid)
	}
	for i := 0; i < nRealms; i++ {
		if len(buckets[i]) == 0 {
			continue
		}
		w.Spawn(&econ.RealmC{
			Name:      realmName(g, i), // из списка, пока хватает, затем процедурно — без потолка
			Capital:   buckets[i][0],
			TaxRate:   0.1, // десятина
			Territory: buckets[i],
		})
	}
}

// realmName — имя державы: из списка, пока хватает, затем процедурно (держав сколько угодно).
func realmName(g *dice.Stream, i int) string {
	if i < len(realmNames) {
		return realmNames[i]
	}
	return procRealmName(g)
}

func spawnPerson(w *core.World, s *dice.Stream, settlement core.ID) *core.Entity {
	sex := randomSex(s)
	ident := makeIdentity(s, sex, "", pickRole(s))
	age := s.Normal(30, 15)
	if age < 1 {
		age = 1
	}
	if age > 70 {
		age = 70
	}
	home := "none"
	if s.Chance(0.4) {
		home = "owned"
	}
	e := w.Spawn(
		ident,
		&core.VitalsC{Age: round1(age), Health: s.Uniform(80, 100),
			Hunger: s.Uniform(0, 40), Energy: s.Uniform(50, 100), Morale: s.Uniform(45, 80)},
		&core.DrivesC{Ambition: s.Uniform(0, 60), Social: s.Uniform(0, 50), Dwelling: s.Uniform(0, 60)},
		&core.MindC{Goal: "", LastAct: "—"},
		&core.SkillsC{Skills: map[string]float64{ident.Role: s.Uniform(2, 25)}},
		&core.PurseC{Coins: round1(s.Uniform(2, 40)), Debts: map[string]float64{}},
		&core.HomeC{Status: home, Comfort: 0, Settlement: settlement},
		&core.PlaceC{Settlement: settlement},
	)
	if s.Chance(mageSparkChance) {
		w.Attach(e, &arcane.MageC{Awakened: false}) // врождённая искра: латент (ремесло обычное, пока не пробудился)
	}
	return e
}

func round1(f float64) float64 { return float64(int(f*10+0.5)) / 10 }

// Describe — короткая сводка мира (для cmd/диагностики).
func Describe(w *core.World) string {
	people, settles, nodes, realms := 0, 0, 0, 0
	w.Each("ident", func(e *core.Entity) { people++ })
	w.Each("settlement", func(e *core.Entity) { settles++ })
	w.Each("resnode", func(e *core.Entity) { nodes++ })
	w.Each("realm", func(e *core.Entity) { realms++ })
	return fmt.Sprintf("мир: %d держав, %d поселений, %d угодий, %d живых душ",
		realms, settles, nodes, people)
}

// setRole жёстко назначает ремесло жителю и даёт начальные азы навыка (для костяка деревни).
func setRole(e *core.Entity, role string) {
	if id := core.Ident(e); id != nil {
		id.Role = role
	}
	if sk := core.Skills(e); sk != nil {
		sk.Skills[role] = 8
	}
}

// topoPrefix/Root/Suffix — для процедурных названий новых мест (мир растёт — имён не напасёшься).
var topoPrefix = []string{"Ново", "Старо", "Велико", "Мало", "Красно", "Бело", "Чёрно", "Злато", "Тихо", "Дальне"}
var topoRoot = []string{"лес", "брод", "гор", "озёр", "реч", "камен", "поль", "дуб", "яр", "студён", "соколь", "медвеж"}
var topoSuffix = []string{"ье", "ово", "ино", "ск", "ец", "поль", "град"}

func procToponym(g *dice.Stream) string {
	return topoPrefix[g.Intn(len(topoPrefix))] +
		topoRoot[g.Intn(len(topoRoot))] +
		topoSuffix[g.Intn(len(topoSuffix))]
}
