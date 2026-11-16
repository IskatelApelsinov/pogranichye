package sim

import (
	"testing"

	"pogranichye/core"
)

// drinker ставит игрока у стойки корчмы с монетами, в поселении с запасом пива и с корчмарём,
// который прямо сейчас обслуживает стойку (comms 0223: покупка требует и то, и другое).
func drinker(w *core.World, coins, beer float64) (*core.Entity, *core.Entity) {
	se := w.Spawn(&core.SettlementC{
		Name: "Корчма", Tavern: true,
		Store: map[string]float64{BeerGood: beer},
	})
	keeper := w.Spawn(
		&core.IdentC{Name: "Корчмарь", Role: "tavernkeeper", Sex: "м"},
		&core.VitalsC{Health: 100, Morale: 50, Hunger: 40},
		&core.MindC{ActCode: "keep_tavern"},
		&core.PlaceC{Settlement: se.ID},
	)
	core.Settle(se).Keeper = keeper.ID
	e := w.Spawn(
		&core.IdentC{Name: "Питок", Role: "farmer", Sex: "м"},
		&core.VitalsC{Health: 100, Morale: 50, Hunger: 40},
		&core.PurseC{Coins: coins, Debts: map[string]float64{}},
		&core.PlaceC{Settlement: se.ID},
	)
	cx, cy, _, ok := StarterLayout().TavernCounter()
	if !ok {
		panic("drinker: у стартовой раскладки должна быть геометрия корчмы со стойкой")
	}
	w.Attach(e, &ScenePosC{X: cx, Y: cy, Stam: 1}) // прямо у стойки
	return e, se
}

// Покупка у стойки: списывает монету и пиво, даёт кружку.
func TestBuyBeer(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	ok, why := BuyBeer(w, e, "r1", 14)
	if !ok {
		t.Fatalf("покупка должна пройти: %s", why)
	}
	if !Mug(e).Has {
		t.Fatal("после покупки кружка должна быть в руке")
	}
	if got := core.Purse(e).Coins; got != 5-BeerPrice {
		t.Fatalf("монеты не списаны верно: %.2f", got)
	}
	if got := core.Settle(se).Store[BeerGood]; got != 9 {
		t.Fatalf("пиво в корчме не убавилось: %.1f", got)
	}
}

// Идемпотентность: повтор того же reqid не списывает второй раз.
func TestBuyBeerIdempotent(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	BuyBeer(w, e, "r1", 14)
	ok, _ := BuyBeer(w, e, "r1", 14) // тот же запрос
	if !ok {
		t.Fatal("повтор того же reqid — успех (уже куплено)")
	}
	if core.Purse(e).Coins != 5-BeerPrice || core.Settle(se).Store[BeerGood] != 9 {
		t.Fatal("повтор reqid не должен списывать дважды")
	}
}

// Нет денег — состояние не меняется (частичных изменений нет).
func TestBuyBeerNoCoins(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 0.1, 10)
	ok, _ := BuyBeer(w, e, "r1", 14)
	if ok || Mug(e).Has {
		t.Fatal("без монет покупки быть не должно")
	}
	if core.Settle(se).Store[BeerGood] != 10 {
		t.Fatal("при отказе пиво в корчме не трогаем")
	}
}

// Далеко от стойки — покупки нет.
func TestBuyBeerTooFar(t *testing.T) {
	w := core.NewWorld("t")
	e, _ := drinker(w, 5, 10)
	p := ScenePos(e)
	p.X, p.Y = 25, 25 // ушёл на край площадки
	if ok, _ := BuyBeer(w, e, "r1", 14); ok {
		t.Fatal("издалека заказать нельзя")
	}
}

// Пусто в корчме — покупки нет.
func TestBuyBeerDry(t *testing.T) {
	w := core.NewWorld("t")
	e, _ := drinker(w, 5, 0)
	if ok, _ := BuyBeer(w, e, "r1", 14); ok {
		t.Fatal("пустую корчму не наливают")
	}
}

// Питьё: тратит кружку, поднимает настрой; повтор reqid не повторяет эффект.
func TestDrinkBeer(t *testing.T) {
	w := core.NewWorld("t")
	e, _ := drinker(w, 5, 10)
	BuyBeer(w, e, "r1", 14)
	before := core.Vitals(e).Morale
	ok, why := DrinkBeer(w, e, "d1")
	if !ok {
		t.Fatalf("выпить должно получиться: %s", why)
	}
	if Mug(e).Has {
		t.Fatal("после питья кружки нет")
	}
	if core.Vitals(e).Morale <= before {
		t.Fatal("кружка должна поднять настрой")
	}
	m := core.Vitals(e).Morale
	DrinkBeer(w, e, "d1") // повтор того же reqid
	if core.Vitals(e).Morale != m {
		t.Fatal("повтор reqid не должен повторять эффект")
	}
}

// comms 0223, тест 2: у старого внешнего порога (дверь/вход), но НЕ у самой стойки — отказ. Это
// ровно тот случай, который раньше (до comms 0223) считался "у корчмы" и давал покупку.
func TestBuyBeerAtOldDoorNotAtCounter(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	tx, ty := StarterLayout().POI("tavern") // старый внешний порог — теперь этого не хватает
	ScenePos(e).X, ScenePos(e).Y = tx, ty
	ok, why := BuyBeer(w, e, "r1", 14)
	if ok {
		t.Fatal("у двери снаружи, но не у стойки — покупки быть не должно")
	}
	if why == "" {
		t.Fatal("отказ должен сопровождаться причиной")
	}
	if Mug(e).Has || core.Purse(e).Coins != 5 || core.Settle(se).Store[BeerGood] != 10 {
		t.Fatal("при отказе ничего не меняется")
	}
}

// comms 0223, тест 3а: корчмаря вовсе нет в мире (умер/не загружен) — отказ, даже стоя у стойки.
func TestBuyBeerKeeperAbsent(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	w.Remove(core.Settle(se).Keeper) // корчмарь исчез
	ok, why := BuyBeer(w, e, "r1", 14)
	if ok {
		t.Fatal("без корчмаря за стойкой покупки быть не должно")
	}
	if why == "" {
		t.Fatal("отказ должен сопровождаться причиной")
	}
	if core.Settle(se).Store[BeerGood] != 10 || core.Purse(e).Coins != 5 {
		t.Fatal("при отказе запас пива и монеты не трогаем")
	}
}

// TestGoToTavernLandsAtCounterNotDoor — task_ai 006 (issue #651), протокол 3.6: «после goto
// действия, завязанные на близость (buy/fish/chop/talk), работают, т.к. тело уже на месте» — до
// этого PR goto("tavern") ставил тонкого клиента у ВНЕШНЕГО порога (l.POIs["tavern"]), а BuyBeer
// после ужесточения 0223 требует стойку внутри (atTavernCounter, 2.2 м от центра) — контракт был
// нарушен конкретно для корчмы (owner: «нельзя нормально взаимодействовать с корчмой»). Тело
// ДЕЙСТВИТЕЛЬНО двигалось (не баг перемещения) — просто не туда, куда нужно для buy. Не трогаем
// ADR 0002/0040 («без ходьбы») — goto остаётся дискретным, просто целится в правильную точку.
func TestGoToTavernLandsAtCounterNotDoor(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "Корчма", Tavern: true, Store: map[string]float64{BeerGood: 10}})
	keeper := w.Spawn(
		&core.IdentC{Name: "Корчмарь", Role: "tavernkeeper", Sex: "м"},
		&core.VitalsC{Health: 100, Morale: 50, Hunger: 40},
		&core.MindC{ActCode: "keep_tavern"},
		&core.PlaceC{Settlement: se.ID},
	)
	core.Settle(se).Keeper = keeper.ID
	e := w.Spawn(
		&core.IdentC{Name: "Питок", Role: "farmer", Sex: "м"},
		&core.VitalsC{Health: 100, Morale: 50, Hunger: 40},
		&core.PurseC{Coins: 5, Debts: map[string]float64{}},
		&core.PlaceC{Settlement: se.ID},
	)
	if ok, why := GoTo(w, e, "tavern"); !ok {
		t.Fatalf("goto(tavern) должен сработать: %s", why)
	}
	if ok, why := BuyBeer(w, e, "r1", 14); !ok {
		t.Fatalf("после goto(tavern) buy должен сработать без отдельного перемещения (протокол 3.6): %s", why)
	}
}

// comms 0223, тест 3б: корчмарь в мире и жив, но сейчас занят НЕ стойкой (другое занятие) — отказ.
// comms 0242/task 012: ActCode != keep_tavern больше не отказ САМ ПО СЕБЕ в дневное окно (10..22) —
// только ночью (keeper "занят отдыхом/сном" и час вне окна) отказ остаётся честным.
func TestBuyBeerKeeperBusy(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	keeper := w.Get(core.Settle(se).Keeper)
	core.Mind(keeper).ActCode = "sleep" // занят другим, не стойкой
	ok, _ := BuyBeer(w, e, "r1", 2)     // глубокая ночь — вне дневного окна
	if ok {
		t.Fatal("ночью, если корчмарь занят не стойкой — покупки быть не должно")
	}
	if core.Settle(se).Store[BeerGood] != 10 {
		t.Fatal("при отказе запас пива не трогаем")
	}
}

// TestBuyBeerDayVsNight — comms 0242, вариант A (task 012): в дневном окне обслуживания (10..22)
// живой корчмарь-тавернщик обслуживает стойку даже если его расписание сейчас не keep_tavern
// (отдых/еда/socialize — "честное расписание" не должно убивать демо-сценарий "пришли выпить
// днём"); глубокой ночью (вне окна) отказ остаётся, даже если ActCode почему-то keep_tavern.
func TestBuyBeerDayVsNight(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	keeper := w.Get(core.Settle(se).Keeper)
	core.Mind(keeper).ActCode = "eat" // не за стойкой по расписанию, но жив и "на смене"

	if ok, why := BuyBeer(w, e, "day", 15); !ok {
		t.Fatalf("днём (ActCode=eat, час 15) покупка должна пройти: %s", why)
	}

	core.Mind(keeper).ActCode = "keep_tavern" // даже формально "за стойкой" — ночью закрыто
	if ok, _ := BuyBeer(w, e, "night", 3); ok {
		t.Fatal("глубокой ночью (час 3) корчма должна быть закрыта независимо от ActCode")
	}
	if core.Settle(se).Store[BeerGood] != 9 { // только дневная покупка списала пиво
		t.Fatalf("ночной отказ не должен списывать пиво: осталось %.0f", core.Settle(se).Store[BeerGood])
	}
}

// TestCoDrinkDoesNotNeedKeepTavern — comms 0242: зафиксировать существующую асимметрию — co-drink
// (ChooseCoDrink/AcceptCoDrink, зона atTavern) НЕ проверяет bartenderServing/ActCode, только
// наличие стока и места в таверне; это сознательно НЕ меняется этим PR (0242: "выпить с другом"
// через co-drink уже проще, чем купить одному — выравнивание осталось бы отдельной задачей, если
// понадобится).
func TestCoDrinkDoesNotNeedKeepTavern(t *testing.T) {
	w := core.NewWorld("t")
	_, se := drinker(w, 5, 10)
	keeper := w.Get(core.Settle(se).Keeper)
	core.Mind(keeper).ActCode = "sleep" // корчмарь явно не на смене
	if bartenderServing(w, se, 3) {
		t.Fatal("для контроля: ночью+не-за-стойкой bartenderServing должен быть false")
	}
	// co-drink сознательно не зовёт bartenderServing — здесь фиксируем только сигнатуру/факт, что
	// функция существует и не требует параметра часа, в отличие от BuyBeer/TavernCounterView.
}

// comms 0223, тест 6: двое игроков у одной стойки подряд — запас пива списывается ровно дважды,
// без гонки в минус и без двойной выдачи кружки одному и тому же.
func TestBuyBeerTwoPlayersNoDoubleIssue(t *testing.T) {
	w := core.NewWorld("t")
	e1, se := drinker(w, 5, 10)
	cx, cy, _, _ := StarterLayout().TavernCounter()
	e2 := w.Spawn(
		&core.IdentC{Name: "Питок2", Role: "farmer", Sex: "ж"},
		&core.VitalsC{Health: 100, Morale: 50, Hunger: 40},
		&core.PurseC{Coins: 5, Debts: map[string]float64{}},
		&core.PlaceC{Settlement: se.ID},
		&ScenePosC{X: cx, Y: cy, Stam: 1},
	)
	ok1, why1 := BuyBeer(w, e1, "p1-r1", 14)
	ok2, why2 := BuyBeer(w, e2, "p2-r1", 14)
	if !ok1 || !ok2 {
		t.Fatalf("оба должны суметь купить: %s / %s", why1, why2)
	}
	if !Mug(e1).Has || !Mug(e2).Has {
		t.Fatal("у обоих должна появиться кружка")
	}
	if got := core.Settle(se).Store[BeerGood]; got != 8 {
		t.Fatalf("пиво должно списаться ровно дважды (10-1-1=8), получили %.1f", got)
	}
	if got := core.Purse(e1).Coins; got != 5-BeerPrice {
		t.Fatalf("у первого списана ровно одна кружка: %.2f", got)
	}
	if got := core.Purse(e2).Coins; got != 5-BeerPrice {
		t.Fatalf("у второго списана ровно одна кружка: %.2f", got)
	}
}
