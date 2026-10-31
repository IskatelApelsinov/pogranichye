package sim

import (
	"fmt"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

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

	var settleIDs []core.ID
	for i := 0; i < settlements; i++ {
		se := NewSettlement(w, g, settlementName(w, g, i))
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
	makeRealms(w, g, settleIDs) // державы делят землю и берут десятину
	return w, hub
}

// NewSettlement создаёт ПОСЕЛЕНИЕ с рельефом и конечными угодьями (без жителей) — общий
// код для стартовой генерации и для ОСНОВАНИЯ новых деревень живым миром. Так мир растёт
// не только населением, но и числом мест.
func NewSettlement(w *core.World, g *dice.Stream, name string) *core.Entity {
	coastal := g.Chance(0.4)
	mountain := g.Chance(0.3)
	se := w.Spawn(&core.SettlementC{
		Name: name, Founded: 0, Tier: "village", Tavern: true,
		Store: map[string]float64{
			econ.GoodBread: 400,
			econ.GoodWheat: 160,
			econ.GoodSeed:  econ.SeedTarget(),
			econ.GoodWood:  30,
		},
	})
	for f := 0; f < 5; f++ { // пять полей, одно под паром (трёхполье упрощённо)
		w.Spawn(&econ.ResourceNodeC{Node: econ.NodeField, Good: econ.GoodWheat,
			Settlement: se.ID, Fallow: f == 4})
	}
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodeForest, Good: econ.GoodWood,
		Settlement: se.ID, Stock: 400, Cap: 400, Regen: 3})
	w.Spawn(&econ.ResourceNodeC{Node: econ.NodePasture, Good: econ.GoodGame,
		Settlement: se.ID, Stock: 200, Cap: 200, Regen: 4})
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
	nRealms := 1 + len(settles)/5 // примерно одна держава на ~5 поселений
	if nRealms > len(realmNames) {
		nRealms = len(realmNames)
	}
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
			Name:      realmNames[i],
			Capital:   buckets[i][0],
			TaxRate:   0.1, // десятина
			Territory: buckets[i],
		})
	}
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
	return w.Spawn(
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
