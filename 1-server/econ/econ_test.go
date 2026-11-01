package econ

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// минимальный мир: поселение + (опц.) работник нужного ремесла.
func mini(store map[string]float64, role string, age float64) (*core.World, *core.Entity) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "Тест", Tier: "village", Store: store})
	if role != "" {
		w.Spawn(
			&core.IdentC{Name: "Р", Role: role, Sex: "м"},
			&core.VitalsC{Age: age, Health: 100},
			&core.PlaceC{Settlement: se.ID},
		)
	}
	return w, se
}

func dayHour(day int) core.Clock { return core.Clock{TotalHours: int64(day-1) * core.HoursPerDay} }

func run(w *core.World, day int) {
	hub := dice.NewHub("t")
	EconomySystem{}.Hour(w, dayHour(day), hub)
}

// Без пахаря пашня не засевается — хлеба не будет (сохранение: нет труда → нет блага).
func TestNoFarmerNoSowing(t *testing.T) {
	w, se := mini(map[string]float64{GoodWheat: 500}, "", 0)
	w.Spawn(&ResourceNodeC{Node: NodeField, Good: GoodWheat, Settlement: se.ID})
	run(w, 1) // день 1 — весна
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	if n.Sown {
		t.Fatal("без пахаря пашня не должна засеваться")
	}
}

// С пахарём весной сеют (зерно убывает в землю), осенью жнут (зерно прибывает, сохранение).
func TestSowAndHarvest(t *testing.T) {
	store := map[string]float64{GoodWheat: 500}
	w, se := mini(store, "farmer", 30)
	w.Spawn(&ResourceNodeC{Node: NodeField, Good: GoodWheat, Settlement: se.ID})
	run(w, 1) // весна: сев
	if store[GoodWheat] >= 500 {
		t.Fatalf("сев не израсходовал зерно: %.0f", store[GoodWheat])
	}
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	n.Ripe = 1
	run(w, 50) // осень: жатва
	// урожай должен заметно превысить остаток (сам-N), но не больше физически возможного
	if store[GoodWheat] < 100 {
		t.Fatalf("осенью должна быть жатва, а зерна лишь %.0f", store[GoodWheat])
	}
	if store[GoodWheat] > 500+seedPerField*yieldRatioMax {
		t.Fatalf("урожай больше физически возможного: %.0f", store[GoodWheat])
	}
}

// Бережливость — поведение, а не замок: повар НЕ мелет зерно ниже посевного резерва (sowReserve),
// придерживая его под сев. Проверяем, что из запаса чуть выше резерва смелется лишь излишек.
func TestCookKeepsSowReserve(t *testing.T) {
	store := map[string]float64{GoodWheat: sowReserve + 20}
	w, _ := mini(store, "cook", 30)
	run(w, 30) // лето — повар печёт
	if store[GoodWheat] < sowReserve-1 {
		t.Fatalf("повар проел посевной резерв: осталось %.0f (резерв %.0f)", store[GoodWheat], sowReserve)
	}
	if store[GoodBread] <= 0 {
		t.Fatal("повар должен был испечь хлеб из излишка зерна")
	}
}

// Трагедия «съели семена» эмерджентна: голод (нет печёного/промысла, только зерно) заставляет
// есть зерно — и оно уходит НИЖЕ посевного резерва (движок этого не запрещает).
func TestFamineEatsSowGrain(t *testing.T) {
	se := &core.Entity{ID: 1, Comps: map[string]core.Component{}}
	se.Add(&core.SettlementC{Store: map[string]float64{GoodWheat: sowReserve}})
	ate := 0
	for i := 0; i < 50; i++ {
		if Eat(se) { // ничего кроме зерна нет → в голод едят зерно
			ate++
		}
	}
	if ate == 0 {
		t.Fatal("в голод люди должны есть зерно (в т.ч. посевное)")
	}
	if core.Settle(se).Store[GoodWheat] >= sowReserve {
		t.Fatal("посевной резерв должен был просесть — это и есть «съели семена»")
	}
}

// Рыбное место закрыто в нерест (весна) и открыто летом — перелов невозможен весной.
func TestFisheryNerest(t *testing.T) {
	w, se := mini(map[string]float64{}, "fisher", 30)
	w.Spawn(&ResourceNodeC{Node: NodeFishery, Good: GoodFish, Settlement: se.ID, Stock: 100, Cap: 100})
	run(w, 1) // весна — нерест
	if core.Settle(se).Store[GoodFish] > 0 {
		t.Fatal("в нерест (весной) ловить нельзя")
	}
	run(w, 30) // лето — лов открыт
	if core.Settle(se).Store[GoodFish] <= 0 {
		t.Fatal("летом рыбу должны ловить")
	}
}

// Шахта конечна и НЕ возобновляется: запас только убывает, добыча не превышает запас.
func TestMineDepletes(t *testing.T) {
	w, se := mini(map[string]float64{}, "miner", 30)
	w.Spawn(&ResourceNodeC{Node: NodeMine, Good: GoodOre, Settlement: se.ID, Stock: 12, Cap: 600, Regen: 0})
	for d := 30; d < 60; d++ { // лето, много дней подряд
		run(w, d)
	}
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	if n.Stock > 0.01 {
		t.Fatalf("шахту должны были исчерпать, осталось %.2f", n.Stock)
	}
	// добыто не больше, чем было в жиле (сохранение)
	if core.Settle(se).Store[GoodOre] > 12.01 {
		t.Fatalf("добыто больше, чем было руды: %.2f", core.Settle(se).Store[GoodOre])
	}
}

// Торговля ПЕРЕМЕЩАЕТ зерно (с потерей на провоз), а не создаёт: сумма после ≤ суммы до.
func TestTradeConserves(t *testing.T) {
	w := core.NewWorld("t")
	rich := w.Spawn(&core.SettlementC{Name: "Богатое", Store: map[string]float64{GoodWheat: 1000}})
	poor := w.Spawn(&core.SettlementC{Name: "Бедное", Store: map[string]float64{GoodWheat: 0}})
	// по жителю в каждом, чтобы были «нужда» и «излишек»
	w.Spawn(&core.IdentC{Name: "A", Role: "farmer"}, &core.VitalsC{Age: 30}, &core.PlaceC{Settlement: rich.ID})
	w.Spawn(&core.IdentC{Name: "B", Role: "farmer"}, &core.VitalsC{Age: 30}, &core.PlaceC{Settlement: poor.ID})
	w.Spawn(&RealmC{Name: "Д", TaxRate: 0, Territory: []core.ID{rich.ID, poor.ID}})

	before := core.Settle(rich).Store[GoodWheat] + core.Settle(poor).Store[GoodWheat]
	TradeSystem{}.Hour(w, dayHour(30), dice.NewHub("t"))
	after := core.Settle(rich).Store[GoodWheat] + core.Settle(poor).Store[GoodWheat]

	if after > before+0.01 {
		t.Fatalf("торговля создала зерно из ниоткуда: было %.0f стало %.0f", before, after)
	}
	if core.Settle(poor).Store[GoodWheat] <= 0 {
		t.Fatal("бедное поселение должно было получить зерно по торговле")
	}
}

// добавить работника ремесла в существующее поселение.
func addWorker(w *core.World, sid core.ID, role string) {
	w.Spawn(&core.IdentC{Name: "Р", Role: role, Sex: "м"},
		&core.VitalsC{Age: 30, Health: 100}, &core.PlaceC{Settlement: sid})
}

// Стадо даёт возобновляемое (молоко) трудом пастуха, не из воздуха и не больше лимита с головы.
func TestHerdRenewable(t *testing.T) {
	w, se := mini(map[string]float64{GoodBread: 9999}, "herder", 30) // сыты → не режут
	w.Spawn(&ResourceNodeC{Node: NodeHerd, HerdKind: HerdCattle, Settlement: se.ID,
		Stock: 20, Cap: 45, Regen: 0})
	run(w, 30) // лето
	milk := core.Settle(se).Store[GoodMilk]
	if milk <= 0 {
		t.Fatal("пастух со стадом должен давать молоко")
	}
	if milk > herderRate+0.01 { // 1 пастух: не больше herderRate в день
		t.Fatalf("молока больше возможного: %.2f", milk)
	}
}

// Голодная деревня пускает скот под нож: голов меньше, появляется мясо и шкура, племя цело.
func TestHerdSlaughter(t *testing.T) {
	w, se := mini(map[string]float64{}, "herder", 30) // еды нет → голод
	w.Spawn(&ResourceNodeC{Node: NodeHerd, HerdKind: HerdSheep, Settlement: se.ID,
		Stock: 20, Cap: 45, Regen: 0})
	run(w, 30)
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	if n.Stock >= 20 {
		t.Fatalf("в голод скот должны были забить, осталось %.0f", n.Stock)
	}
	if n.Stock < herdMinKeep-0.01 {
		t.Fatalf("племенное ядро нельзя резать: осталось %.0f (минимум %.0f)", n.Stock, herdMinKeep)
	}
	if core.Settle(se).Store[GoodMeat] <= 0 || core.Settle(se).Store[GoodHide] <= 0 {
		t.Fatal("с забоя должны быть мясо и шкура")
	}
}

// Цепочки животноводства: молоко→сыр (повар), шерсть→сукно (ткач).
func TestAnimalChains(t *testing.T) {
	w, se := mini(map[string]float64{GoodMilk: 50, GoodWool: 50, GoodBread: 9999}, "cook", 30)
	addWorker(w, se.ID, "weaver")
	run(w, 30)
	st := core.Settle(se).Store
	if st[GoodCheese] <= 0 {
		t.Fatal("повар должен делать сыр из молока")
	}
	if st[GoodCloth] <= 0 {
		t.Fatal("ткач должен делать сукно из шерсти")
	}
}

// Налог — действие государства с досягаемостью: сборщик достаёт ближнюю деревню, а дальнюю
// (за пределом taxReach от столицы) — нет, она вольна от податей.
func TestTaxReach(t *testing.T) {
	w := core.NewWorld("tax")
	capital := w.Spawn(&core.SettlementC{Name: "Столица", X: 0, Y: 0,
		Store: map[string]float64{GoodBread: 1000}})
	far := w.Spawn(&core.SettlementC{Name: "Дальняя", X: 0, Y: 900, // > taxReach
		Store: map[string]float64{GoodBread: 1000}})
	w.Spawn(&RealmC{Name: "Д", Capital: capital.ID, TaxRate: 0.1,
		Territory: []core.ID{capital.ID, far.ID}})

	EconomySystem{}.Hour(w, dayHour(60), dice.NewHub("tax")) // день 60 (не весна) — сбор идёт

	// столицу обложили (заметная убыль от налога ~10%), дальнюю лишь чуть тронула порча (не налог)
	if core.Settle(capital).Store[GoodBread] > 950 {
		t.Fatalf("ближнюю (столицу) сборщик должен заметно обложить, осталось %.1f",
			core.Settle(capital).Store[GoodBread])
	}
	if core.Settle(far).Store[GoodBread] < 990 {
		t.Fatalf("дальнюю деревню сборщик НЕ доходит — подати браться не должны (осталось %.1f)",
			core.Settle(far).Store[GoodBread])
	}
}

// Зимой без сена — падёж; с сеном — стадо переживает зиму. И летом сено косят впрок.
func TestWinterFodder(t *testing.T) {
	// А) зима, сыто (чтоб не забивали), но сена нет → поголовье падает
	w, se := mini(map[string]float64{GoodBread: 9999}, "herder", 30)
	w.Spawn(&ResourceNodeC{Node: NodeHerd, HerdKind: HerdCattle, Settlement: se.ID,
		Stock: 20, Cap: 45, Regen: 0})
	for d := 70; d < 80; d++ { // зимние дни
		run(w, d)
	}
	var n *ResourceNodeC
	w.Each("resnode", func(e *core.Entity) { n = Node(e) })
	if n.Stock >= 20 {
		t.Fatalf("без сена зимой должен быть падёж, осталось %.1f", n.Stock)
	}

	// Б) та же зима, но с запасом сена → стадо держится
	w2, se2 := mini(map[string]float64{GoodBread: 9999, GoodHay: 2000}, "herder", 30)
	w2.Spawn(&ResourceNodeC{Node: NodeHerd, HerdKind: HerdCattle, Settlement: se2.ID,
		Stock: 20, Cap: 45, Regen: 0})
	for d := 70; d < 80; d++ {
		run(w2, d)
	}
	var n2 *ResourceNodeC
	w2.Each("resnode", func(e *core.Entity) { n2 = Node(e) })
	if n2.Stock < 19 {
		t.Fatalf("с сеном стадо должно пережить зиму, осталось %.1f", n2.Stock)
	}

	// В) летом пастух косит сено впрок
	w3, se3 := mini(map[string]float64{}, "herder", 30)
	run(w3, 30) // лето
	if core.Settle(se3).Store[GoodHay] <= 0 {
		t.Fatal("летом пастух должен косить сено на зиму")
	}
}

// Новые цепочки ремёсел: лён→холст (ткач), травы→снадобье (знахарь), мёд→медовуха (корчмарь).
func TestCraftChains2(t *testing.T) {
	w, se := mini(map[string]float64{GoodFlax: 50, GoodHerb: 50, GoodHoney: 50, GoodBread: 9999}, "weaver", 30)
	addWorker(w, se.ID, "herbalist")
	addWorker(w, se.ID, "tavernkeeper")
	run(w, 30)
	st := core.Settle(se).Store
	if st[GoodLinen] <= 0 {
		t.Fatal("ткач должен делать холст из льна")
	}
	if st[GoodRemedy] <= 0 {
		t.Fatal("знахарь должен делать снадобье из трав")
	}
	if st[GoodMead] <= 0 {
		t.Fatal("корчмарь должен варить медовуху из мёда")
	}
}
