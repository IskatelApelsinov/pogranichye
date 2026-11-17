package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Лошадь — не рыночный товар: её нет в списке рынка, купить/продать через рынок нельзя, а «лошадь»-
// вещь из старого сохранения становится настоящей лошадью или возвращается в конюшню.
func TestHorseIsNotAMarketGood(t *testing.T) {
	w, e, se := starterPlayer(t)
	s := core.Settle(se)
	stableStock(s)
	core.Purse(e).Coins = 100
	for _, row := range MarketView(se, e) {
		if row["good"] == horseGood {
			t.Fatal("лошадь видна на рынке как товар")
		}
	}
	if ok, _ := Trade(w, e, horseGood, "buy", 1, "t1"); ok {
		t.Fatal("лошадь купилась на рынке")
	}
	if econ.Stash(e) != nil && econ.Stash(e).Goods[horseGood] > 0 {
		t.Fatal("лошадь попала в суму вещью")
	}

	// старое сохранение: две «лошади»-вещи в суме у игрока без лошади
	before := s.Store[horseGood]
	addStash(w, e, horseGood, 2)
	if n := FixHorseGoods(w); n != 2 {
		t.Fatalf("исправлено %d из 2", n)
	}
	if _, h := horseOf(w, e); h == nil || h.Settlement != se.ID {
		t.Fatal("первая «лошадь»-вещь должна стать настоящей лошадью")
	}
	if econ.Stash(e).Goods[horseGood] != 0 {
		t.Fatal("вещь «лошадь» осталась в суме")
	}
	if s.Store[horseGood] != before+1 {
		t.Fatalf("вторая должна вернуться в конюшню: было %.0f, стало %.0f", before, s.Store[horseGood])
	}
	if FixHorseGoods(w) != 0 {
		t.Fatal("повторная починка не должна ничего менять")
	}
}
