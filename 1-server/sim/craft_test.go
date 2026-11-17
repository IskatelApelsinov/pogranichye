package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

func craftTestPlayer(t *testing.T, station string) (*core.World, *core.Entity) {
	t.Helper()
	w, e, _ := starterPlayer(t)
	if ok, why := GoTo(w, e, station); !ok {
		t.Fatalf("перейти к %s: %s", station, why)
	}
	ensureStashOf(w, e).Goods = map[string]float64{
		econ.GoodOre: 4,
		econ.GoodWood: 4,
		econ.GoodMetal: 2,
	}
	return w, e
}

func TestCraftConsumesMaterialsAndProducesOutput(t *testing.T) {
	w, e := craftTestPlayer(t, "smithy")
	beforeEnergy := core.Vitals(e).Energy

	ok, why := Craft(w, e, "metal", "craft-metal-1")
	if !ok {
		t.Fatalf("выплавка у кузни должна пройти: %s", why)
	}
	st := econ.Stash(e)
	if st.Goods[econ.GoodOre] != 2 || st.Goods[econ.GoodWood] != 3 || st.Goods[econ.GoodMetal] != 3 {
		t.Fatalf("неверный баланс сырья/изделия: %#v", st.Goods)
	}
	if core.Vitals(e).Energy != beforeEnergy-5 {
		t.Fatalf("ремесло должно потратить 5 энергии: %.1f", core.Vitals(e).Energy)
	}
}

func TestCraftRejectsMissingIngredientsWithoutMutation(t *testing.T) {
	w, e := craftTestPlayer(t, "smithy")
	st := econ.Stash(e)
	st.Goods[econ.GoodOre] = 1
	st.Goods[econ.GoodWood] = 0
	beforeEnergy := core.Vitals(e).Energy

	ok, why := Craft(w, e, "metal", "craft-no-materials")
	if ok || why != "не хватает сырья" {
		t.Fatalf("ожидался отказ из-за сырья, ok=%v reason=%q", ok, why)
	}
	if st.Goods[econ.GoodOre] != 1 || st.Goods[econ.GoodMetal] != 2 || core.Vitals(e).Energy != beforeEnergy {
		t.Fatal("отказ по сырью не должен менять запасы или энергию")
	}
}

func TestCraftRejectsWrongStationWithoutMutation(t *testing.T) {
	w, e := craftTestPlayer(t, "hearth")
	st := econ.Stash(e)
	beforeEnergy := core.Vitals(e).Energy

	ok, why := Craft(w, e, "metal", "craft-wrong-station")
	if ok || why == "" {
		t.Fatalf("ожидался отказ вне кузни, ok=%v reason=%q", ok, why)
	}
	if st.Goods[econ.GoodOre] != 4 || st.Goods[econ.GoodWood] != 4 || st.Goods[econ.GoodMetal] != 2 || core.Vitals(e).Energy != beforeEnergy {
		t.Fatal("отказ не должен менять запасы или энергию")
	}
}

func TestCraftDuplicateReqIDDoesNotDuplicateOutput(t *testing.T) {
	w, e := craftTestPlayer(t, "smithy")

	if ok, why := Craft(w, e, "metal", "craft-idempotent"); !ok {
		t.Fatalf("первое изготовление: %s", why)
	}
	afterFirst := econ.Stash(e).Goods[econ.GoodMetal]
	energyAfterFirst := core.Vitals(e).Energy

	if ok, why := Craft(w, e, "metal", "craft-idempotent"); !ok {
		t.Fatalf("повтор reqid должен быть безопасным подтверждением: %s", why)
	}
	if got := econ.Stash(e).Goods[econ.GoodMetal]; got != afterFirst {
		t.Fatalf("повтор reqid выдал второй продукт: до %.1f, после %.1f", afterFirst, got)
	}
	if core.Vitals(e).Energy != energyAfterFirst {
		t.Fatal("повтор reqid не должен повторно тратить энергию")
	}
}

func TestPurseCraftWearAndPublicAppearanceDoNotChangeCoins(t *testing.T) {
	w, e, _ := starterPlayer(t)
	if ok, why := GoTo(w, e, "granary"); !ok {
		t.Fatalf("перейти к амбару: %s", why)
	}
	st := ensureStashOf(w, e)
	st.Goods["leather"] = 1
	st.Goods["cloth"] = 1
	coins := core.Purse(e).Coins

	if ok, why := Craft(w, e, "purse", "craft-purse"); !ok {
		t.Fatalf("сшить кошель: %s", why)
	}
	if Inv(e).Items["purse"] != 1 || st.Goods["leather"] != 0 || st.Goods["cloth"] != 0 {
		t.Fatalf("кошель должен быть настоящим предметом, а материалы — списаны: inv=%#v goods=%#v", Inv(e).Items, st.Goods)
	}
	if ok, why := Wear(w, e, "purse", "wear-purse"); !ok {
		t.Fatalf("надеть кошель: %s", why)
	}
	if Inv(e).Worn["belt"] != "purse" {
		t.Fatalf("кошель должен занимать слот belt: %#v", Inv(e).Worn)
	}
	gear := PublicGear(e)
	if gear["worn"].(map[string]string)["belt"] != "purse" {
		t.Fatalf("кошель должен быть виден в публичной экипировке: %#v", gear)
	}
	if core.Purse(e).Coins != coins {
		t.Fatal("физический кошель не должен менять или дублировать денежный баланс")
	}
	if ok, why := Unwear(w, e, "purse", "unwear-purse"); !ok {
		t.Fatalf("снять кошель: %s", why)
	}
	if _, exists := Inv(e).Worn["belt"]; exists || Inv(e).Items["purse"] != 1 {
		t.Fatalf("снятие должно освободить пояс, сохранив предмет: %#v", Inv(e))
	}
	if core.Purse(e).Coins != coins {
		t.Fatal("снятие физического кошеля не должно трогать монеты")
	}
}
