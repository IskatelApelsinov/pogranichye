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
	ok, why := BuyBeer(w, e, "r1")
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
	BuyBeer(w, e, "r1")
	ok, _ := BuyBeer(w, e, "r1") // тот же запрос
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
	ok, _ := BuyBeer(w, e, "r1")
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
	if ok, _ := BuyBeer(w, e, "r1"); ok {
		t.Fatal("издалека заказать нельзя")
	}
}

// Пусто в корчме — покупки нет.
func TestBuyBeerDry(t *testing.T) {
	w := core.NewWorld("t")
	e, _ := drinker(w, 5, 0)
	if ok, _ := BuyBeer(w, e, "r1"); ok {
		t.Fatal("пустую корчму не наливают")
	}
}

// Питьё: тратит кружку, поднимает настрой; повтор reqid не повторяет эффект.
func TestDrinkBeer(t *testing.T) {
	w := core.NewWorld("t")
	e, _ := drinker(w, 5, 10)
	BuyBeer(w, e, "r1")
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
	ok, why := BuyBeer(w, e, "r1")
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
	ok, why := BuyBeer(w, e, "r1")
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

// comms 0223, тест 3б: корчмарь в мире и жив, но сейчас занят НЕ стойкой (другое занятие) — отказ.
func TestBuyBeerKeeperBusy(t *testing.T) {
	w := core.NewWorld("t")
	e, se := drinker(w, 5, 10)
	keeper := w.Get(core.Settle(se).Keeper)
	core.Mind(keeper).ActCode = "sleep" // занят другим, не стойкой
	ok, _ := BuyBeer(w, e, "r1")
	if ok {
		t.Fatal("если корчмарь занят не стойкой — покупки быть не должно")
	}
	if core.Settle(se).Store[BeerGood] != 10 {
		t.Fatal("при отказе запас пива не трогаем")
	}
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
	ok1, why1 := BuyBeer(w, e1, "p1-r1")
	ok2, why2 := BuyBeer(w, e2, "p2-r1")
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
