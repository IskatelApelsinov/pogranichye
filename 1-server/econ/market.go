package econ

import "pogranichye/core"

// Рынок и деньги (экономика-2). Личное имущество — это монеты и долги каждого жителя
// (core.PurseC). Труд приносит ДОХОД: работник продаёт излишек своего ремесла на рынок и
// получает плату из общего котла торга (SettlementC.Coin). Еда стоит денег: едок ПЛАТИТ рынку
// (купил у чужих излишков); нет монет — берёт В ДОЛГ (ест в кредит). Цены плавают от
// спроса/предложения: дефицит дорожает, изобилие дешевеет. Монеты ходят по кругу
// покупатель→котёл→работник, а не куются из воздуха — та же дисциплина сохранения, что у товаров.

const debtKey = "торг" // кому должен едок, купивший в долг (общине/рынку)

// базовые цены товаров в монетах за меру (грубая иерархия ценности средневековья).
var basePrice = map[string]float64{
	GoodWheat: 1.0, GoodBread: 1.4, GoodFish: 1.2, GoodGame: 1.3, GoodVeg: 0.9,
	GoodMilk: 1.0, GoodCheese: 1.8, GoodEgg: 1.1, GoodMeat: 2.0, GoodFlour: 1.2,
	GoodWood: 0.6, GoodOre: 1.5, GoodMetal: 3.0, GoodTools: 4.0, GoodHay: 0.4,
	GoodWool: 1.6, GoodCloth: 4.0, GoodHide: 1.4, GoodLeather: 3.0, GoodFeather: 0.8,
	GoodFlax: 1.2, GoodLinen: 3.5, GoodHerb: 1.0, GoodRemedy: 3.5, GoodHoney: 1.8, GoodMead: 2.5,
}

func priceBase(good string) float64 {
	if p, ok := basePrice[good]; ok {
		return p
	}
	return 1.0
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

// updatePrices пересчитывает рыночные цены поселения: дефицитное дорого, изобильное дёшево.
// Спрос у еды — население; у изделий — грубо «постоянный интерес». Цена = база × дефицитность.
func updatePrices(se *core.Entity, pop int) {
	st := store(se)
	set := core.Settle(se)
	if set.Prices == nil {
		set.Prices = map[string]float64{}
	}
	p := float64(pop)
	if p < 1 {
		p = 1
	}
	for good := range basePrice {
		supply := st[good]
		// дефицитность: сколько на душу приходится; мало на душу → дорого
		perCap := supply / p
		scarcity := clampf(4.0/(perCap+1.5), 0.3, 6.0)
		set.Prices[good] = priceBase(good) * scarcity
	}
}

// Price — текущая рыночная цена товара в поселении (0, если рынок ещё не считался).
func Price(se *core.Entity, good string) float64 {
	set := core.Settle(se)
	if set.Prices == nil {
		return priceBase(good)
	}
	if v, ok := set.Prices[good]; ok {
		return v
	}
	return priceBase(good)
}

// MealPrice — во что обходится одна мера еды (по лучшему доступному съестному).
func MealPrice(se *core.Entity) float64 {
	st := store(se)
	for _, g := range edibles {
		if g == GoodWheat {
			if st[GoodWheat]-sowReserve >= 1 {
				return Price(se, GoodWheat)
			}
			continue
		}
		if st[g] >= 1 {
			return Price(se, g)
		}
	}
	return 0
}

// BuyMeal — житель покупает и съедает меру еды с рынка: платит цену в котёл торга, а нет монет —
// ест В ДОЛГ (долг растёт). Возвращает true, если поел. Так «покупают у других» и «берут в долг».
func BuyMeal(se *core.Entity, e *core.Entity) bool {
	price := MealPrice(se)
	if !Eat(se) { // физически съесть (списывает товар со склада-рынка)
		return false
	}
	pu := core.Purse(e)
	if pu == nil {
		return true
	}
	if pu.Coins >= price {
		pu.Coins -= price // заплатил за еду (монеты ушли в оборот и далее на оплату труда)
	} else {
		if pu.Debts == nil {
			pu.Debts = map[string]float64{}
		}
		need := price - pu.Coins
		pu.Coins = 0
		pu.Debts[debtKey] += need // не хватило — доел в кредит (долг рынку)
	}
	return true
}

// goodOfRole — какой товар ремесло выносит на рынок (от его цены зависит заработок).
func goodOfRole(role string) string {
	switch role {
	case "farmer":
		return GoodWheat
	case "fisher":
		return GoodFish
	case "hunter":
		return GoodMeat
	case "herder":
		return GoodMilk
	case "cook":
		return GoodBread
	case "smith":
		return GoodTools
	case "weaver":
		return GoodCloth
	case "herbalist":
		return GoodRemedy
	case "miner":
		return GoodOre
	case "woodcutter":
		return GoodWood
	case "tavernkeeper":
		return GoodMead
	}
	return GoodWheat
}

// Wage — заработок за труд: работник продал на рынке излишек своего ремесла. Чем дороже его
// товар (дефицит), тем больше плата — и это толкает заниматься тем, чего не хватает. Заработок
// не меньше прожиточного минимума (чтобы в сытые времена можно было прокормиться), а в дефицит
// растёт. Часть сразу гасит долг по еде; что останется — в личный кошель.
func Wage(se *core.Entity, e *core.Entity, role string) float64 {
	// плата за ОДИН час труда (система зовётся каждый рабочий час). За рабочий день (~8 ч)
	// выходит примерно стоимость дневной еды с бытом; в дефицит товар дороже — заработок выше.
	const baseWage = 0.22
	pay := baseWage + 0.03*Price(se, goodOfRole(role))
	pu := core.Purse(e)
	if pu == nil {
		return pay
	}
	if pu.Debts != nil && pu.Debts[debtKey] > 0 { // сперва отдать долг рынку
		cut := pay * 0.5
		if cut > pu.Debts[debtKey] {
			cut = pu.Debts[debtKey]
		}
		pu.Debts[debtKey] -= cut
		pay -= cut
		if pu.Debts[debtKey] <= 0.01 {
			delete(pu.Debts, debtKey)
		}
	}
	pu.Coins += pay
	return pay
}

// Upkeep — дневной быт (дрова, свечи, утварь): сток монет, который делает достаток осмысленным.
// Кто трудится — покрывает; кто нет — влезает в долг. Заодно гасит инфляцию монет.
func Upkeep(e *core.Entity) {
	pu := core.Purse(e)
	if pu == nil {
		return
	}
	const daily = 2.5
	if pu.Coins >= daily {
		pu.Coins -= daily
		return
	}
	need := daily - pu.Coins
	pu.Coins = 0
	if pu.Debts == nil {
		pu.Debts = map[string]float64{}
	}
	pu.Debts[debtKey] += need
}
