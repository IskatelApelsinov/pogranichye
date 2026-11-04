package sim

import (
	"testing"

	"pogranichye/core"
)

// drinker ставит игрока у стойки корчмы с монетами, в поселении с запасом пива.
func drinker(w *core.World, coins, beer float64) (*core.Entity, *core.Entity) {
	se := w.Spawn(&core.SettlementC{
		Name: "Корчма", Tavern: true,
		Store: map[string]float64{BeerGood: beer},
	})
	e := w.Spawn(
		&core.IdentC{Name: "Питок", Role: "farmer", Sex: "м"},
		&core.VitalsC{Morale: 50, Hunger: 40},
		&core.PurseC{Coins: coins, Debts: map[string]float64{}},
		&core.PlaceC{Settlement: se.ID},
	)
	tx, ty := poiByKind("tavern")
	w.Attach(e, &ScenePosC{X: tx, Y: ty, Stam: 1}) // прямо у стойки
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
