package econ

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Настройки экономики — в одном месте, чтобы крутить баланс не трогая логику.
const (
	seedPerField = 60.0  // зерна на засев одной общинной пашни (крупная площадь; жатва кормит год)
	sowReserve   = 300.0 // зерна, которое ОБЩИНА старается придержать под будущий сев.
	//                        Это НЕ закон движка, а бережливость жителей: повар не мелет ниже,
	//                        торговцы не продают ниже. Но в голод отчаявшиеся проедают и его —
	//                        и остаются без сева. «Священность» делают НПС, не код.
	yieldRatioMin  = 3.0   // урожай: сам-3 в плохой год...
	yieldRatioMax  = 6.0   // ...сам-6 в хороший (реальные средневековые цифры)
	harvestRipe    = 0.75  // ниже спелости жатва почти пустая
	perWorkerDay   = 5.0   // сколько единиц в день тянет один работник из узла (лес/рыба/руда/дичь)
	spoilPerDay    = 0.002 // порча запасов в день (еда не копится вечно — лечит инфляцию склада)
	breadFromWheat = 0.8   // 1 зерно → 0.8 хлеба (помол+выпечка, потери) — цепочка wheat→bread
	cookGrindDay   = 30.0  // сколько зерна перемалывает один повар в день
	toolsFromOre   = 0.5   // 2 руды → 1 инструмент
	smithOreDay    = 4.0   // руды в день на кузнеца
	titheRate      = 0.1   // десятина

	// огород и животноводство
	vegPerFarmerDay = 3.0 // овощей с огорода на землепашца в день (тёплый сезон)
	herderRate      = 4.0 // продукта стада (молоко/шерсть/яйца) на пастуха в день
	perAnimalDay    = 0.5 // но не больше столько с головы в день (маленькое стадо — мало продукта)
	herdMinKeep     = 5.0 // ниже этого не режут — племенное ядро (иначе стадо вымрет)
	slaughterPerDay = 2.0 // голов под нож в день, когда решено забивать
	meatPerAnimal   = 6.0 // мяса с головы
	hidePerAnimal   = 1.0 // шкур с головы
	slaughterHunger = 8.0 // дней еды на душу, ниже — пускают скот под нож
	cheeseFromMilk  = 0.5 // 2 молока → 1 сыр
	cookCheeseDay   = 10.0
	clothFromWool   = 0.6 // шерсть → сукно
	leatherFromHide = 0.7 // шкура → кожа
	weaveDay        = 8.0 // сырья на ткача в день
)

// Съедобное (по предпочтению). Разнообразный стол: хлеб/мясо/рыба/сыр/яйца/овощи/дичь/молоко,
// а зерно (wheat) — ПОСЛЕДНЕЕ: его едят кашей, лишь когда прочего не осталось. Так в сытое время
// зерно копится под сев само, а в голод люди проедают и посевной резерв — трагедия «съели семена».
var edibles = []string{GoodBread, GoodMeat, GoodFish, GoodCheese, GoodEgg, GoodVeg, GoodGame, GoodMilk, GoodFlour, GoodWheat}

// SeedPerField — сколько зерна уходит на засев пашни (для генерации стартовых запасов).
func SeedPerField() float64 { return seedPerField }

// SowReserve — сколько зерна община бережёт под сев (поведенческая цель, не замок движка).
func SowReserve() float64 { return sowReserve }

// Edible сообщает, можно ли это съесть (для проверок рождений/голода снаружи пакета).
func Edible(good string) bool {
	for _, g := range edibles {
		if g == good {
			return true
		}
	}
	return false
}

// FoodOnHand — сколько еды (в мерах) лежит на складе поселения суммарно.
func FoodOnHand(se *core.Entity) float64 {
	st := store(se)
	t := 0.0
	for _, g := range edibles {
		t += st[g]
	}
	return t
}

// FeedableFood — еда, которой община реально готова кормиться: печёное/промысел плюс зерно
// СВЕРХ посевного резерва (сам резерв берегут под сев, пока не грянул настоящий голод).
// По ней решаем, сыта ли деревня для рождений.
func FeedableFood(se *core.Entity) float64 {
	st := store(se)
	t := 0.0
	for _, g := range edibles {
		if g == GoodWheat {
			if surplus := st[GoodWheat] - sowReserve; surplus > 0 {
				t += surplus // зерно считаем едой лишь сверх посевного резерва
			}
			continue
		}
		t += st[g]
	}
	return t
}

// store возвращает склад поселения (карту товаров), создавая при нужде.
func store(se *core.Entity) map[string]float64 {
	s := core.Settle(se)
	if s.Store == nil {
		s.Store = map[string]float64{}
	}
	return s.Store
}

// Eat — съесть одну меру лучшей доступной еды со склада поселения. true, если поел.
// Еда реальна и конечна: нет хлеба/рыбы/дичи — голод не унять (отсюда давление голода и закупок).
func Eat(se *core.Entity) bool {
	if se == nil {
		return false
	}
	st := store(se)
	for _, g := range edibles {
		if st[g] >= 1 {
			st[g]--
			return true
		}
	}
	return false
}

// EconomySystem — вся экономика одной дневной прокруткой (эффективно: один проход по людям).
// Час за часом ничего не делает; раз в сутки: сев/жатва по сезону, добыча трудом из конечных
// узлов, переработка цепочками, восстановление природы, десятина в казну держав.
type EconomySystem struct{}

func (EconomySystem) Name() string { return "economy" }

// labor — рабочие руки поселения: сколько кого по ремёслам и всего душ (строится за один проход).
type labor struct {
	byRole map[string]int
	people int
}

func (EconomySystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	season := clock.Season()
	s := hub.Stream("economy")

	// индекс «поселение → его жители по ремёслам» за ОДИН проход (масштабируемо)
	lab := map[core.ID]*labor{}
	w.Each("place", func(e *core.Entity) {
		id := core.Ident(e)
		v := core.Vitals(e)
		if id == nil || v == nil {
			return
		}
		pl := core.Place(e)
		if pl == nil || pl.Settlement == 0 {
			return
		}
		l := lab[pl.Settlement]
		if l == nil {
			l = &labor{byRole: map[string]int{}}
			lab[pl.Settlement] = l
		}
		l.people++
		if v.Age >= 15 && v.Age <= 60 { // рабочие руки
			l.byRole[id.Role]++
		}
	})

	// 1) природа восстанавливается; шахты — нет
	w.Each("resnode", func(ne *core.Entity) {
		n := Node(ne)
		if n.Regen > 0 && n.Stock < n.Cap {
			r := n.Regen
			if n.Node == NodeFishery && season == core.Spring {
				r *= 2.5 // нерест: рыба бурно восполняется весной
			}
			n.Stock = min(n.Cap, n.Stock+r)
		}
	})

	// 2) пашни: сезонный цикл (сев весной, рост летом, жатва осенью) — закон «сколько посеял»
	w.Each("resnode", func(ne *core.Entity) {
		n := Node(ne)
		if n.Node != NodeField || n.Fallow {
			return
		}
		se := w.Get(n.Settlement)
		if se == nil {
			return
		}
		st := store(se)
		farmers := 0
		if l := lab[n.Settlement]; l != nil {
			farmers = l.byRole["farmer"]
		}
		switch season {
		case core.Spring:
			// сеют, если есть пахари И есть зерно в закромах. Если за зиму зерно проели —
			// сеять нечем, год будет голодный (эмерджентно, без запрета в движке).
			if !n.Sown && farmers > 0 && st[GoodWheat] >= seedPerField {
				st[GoodWheat] -= seedPerField // зерно уходит в землю
				n.SownSeed = seedPerField
				n.Sown = true
				n.Ripe = 0
			}
		case core.Summer:
			if n.Sown {
				n.Ripe = min(1, n.Ripe+0.06) // зреет
			}
		case core.Autumn:
			if n.Sown && n.Ripe >= harvestRipe {
				ratio := s.Uniform(yieldRatioMin, yieldRatioMax)
				st[GoodWheat] += n.SownSeed * ratio // урожай = сам-N от посеянного (сохранение)
				n.Sown = false
				n.SownSeed = 0
				n.Ripe = 0
			} else if n.Sown {
				n.Sown = false // не вызрело — год потерян
				n.SownSeed = 0
				n.Ripe = 0
			}
		}
	})

	// 3) труд с угодий и стад. Общий принцип: труд ПЕРЕНОСИТ/СНИМАЕТ запас, не творит из воздуха.
	w.Each("resnode", func(ne *core.Entity) {
		n := Node(ne)
		if n.Node == NodeField { // пашню собирают жатвой, не ежедневно
			return
		}
		l := lab[n.Settlement]
		if l == nil {
			return
		}
		se := w.Get(n.Settlement)
		if se == nil {
			return
		}
		st := store(se)
		switch n.Node {
		case NodeGarden:
			// огород: землепашцы снимают овощи в тёплый сезон (разнообразит стол)
			if (season == core.Summer || season == core.Autumn) && l.byRole["farmer"] > 0 {
				st[GoodVeg] += float64(l.byRole["farmer"]) * vegPerFarmerDay
			}
		case NodeHerd:
			tendHerd(n, l, st, se, w)
		default:
			// лес/рыба/пастбище/шахта: промысловик снимает конечный запас
			workers := workersFor(l.byRole, n.Node)
			if workers == 0 || n.Stock <= 0 {
				return
			}
			if n.Node == NodeFishery && season == core.Spring {
				return // нерест: лов закрыт
			}
			pull := min(n.Stock, float64(workers)*perWorkerDay)
			n.Stock -= pull
			st[n.Good] += pull
		}
	})

	// 4) переработка цепочками: wheat→bread (cook), ore→tools (smith). Ограничено сырьём и мастерами.
	w.Each("settlement", func(se *core.Entity) {
		st := store(se)
		l := lab[se.ID]
		if l == nil {
			return
		}
		// повар мелет зерно В ХЛЕБ, но прудентно оставляет общине посевной резерв (sowReserve):
		// печём из ИЗЛИШКА зерна, а не из того, что нужно весной на сев. Это и есть «бережливость
		// жителей» вместо запрета в движке — в голод её нарушит само поедание зерна (edibles).
		if cooks := l.byRole["cook"]; cooks > 0 {
			grindable := st[GoodWheat] - sowReserve
			if grindable > 0 {
				use := min(grindable, float64(cooks)*cookGrindDay)
				st[GoodWheat] -= use
				st[GoodBread] += use * breadFromWheat
			}
		}
		if smiths := l.byRole["smith"]; smiths > 0 && st[GoodOre] > 0 {
			use := min(st[GoodOre], float64(smiths)*smithOreDay)
			st[GoodOre] -= use
			st[GoodTools] += use * toolsFromOre
		}
		// повар ещё и сыр из молока (хранится дольше молока)
		if cooks := l.byRole["cook"]; cooks > 0 && st[GoodMilk] > 0 {
			use := min(st[GoodMilk], float64(cooks)*cookCheeseDay)
			st[GoodMilk] -= use
			st[GoodCheese] += use * cheeseFromMilk
		}
		// ткач: шерсть→сукно, шкура→кожа (ремёсла из животноводства)
		if weavers := l.byRole["weaver"]; weavers > 0 {
			cap := float64(weavers) * weaveDay
			if st[GoodWool] > 0 {
				use := min(st[GoodWool], cap)
				st[GoodWool] -= use
				st[GoodCloth] += use * clothFromWool
			}
			if st[GoodHide] > 0 {
				use := min(st[GoodHide], cap)
				st[GoodHide] -= use
				st[GoodLeather] += use * leatherFromHide
			}
		}
	})

	// 5) порча: запасы не живут вечно (еда гниёт) — важнейший тормоз инфляции склада
	w.Each("settlement", func(se *core.Entity) {
		st := store(se)
		for g, v := range st {
			if !perishable(g) {
				continue // инструмент/металл/сукно/кожа/шерсть/шкура/перо не гниют
			}
			st[g] = v * (1 - spoilPerDay)
			if st[g] < 0.01 {
				delete(st, g)
			}
		}
	})

	// 6) десятина: держава собирает долю зерна/инструментов с подвластных земель в казну
	w.Each("realm", func(re *core.Entity) {
		r := Realm(re)
		if r.TaxRate <= 0 {
			return
		}
		for _, sid := range r.Territory {
			se := w.Get(sid)
			if se == nil {
				continue
			}
			st := store(se)
			for _, g := range []string{GoodBread, GoodTools} { // зерно (семена) десятиной не трогаем
				take := st[g] * r.TaxRate
				st[g] -= take
				r.Treasury += take // казна в «мерах добра» (денежную оценку добавим с рынком)
			}
		}
	})
}

// TradeSystem — торговля в пределах державы: зерно течёт из деревень-избытков в деревни-нужду
// (лордов амбар / местный торг). Без неё богатые копят, а бедные мрут при общем изобилии —
// именно это и случалось в трассе. Товар перемещается с потерей на провоз (ничего не создаётся).
// Межгосударственная торговля по рекам/ярмаркам — следующий слой (большой мир).
type TradeSystem struct{}

func (TradeSystem) Name() string { return "trade" }

const (
	tradeBufferDays = 25.0 // целевой запас еды на душу (дней), выше — излишек на продажу
	transportLoss   = 0.1  // 10% теряется в пути (дороги, порча, разбой)
)

func (TradeSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	// население по поселениям — один проход
	pop := map[core.ID]int{}
	w.Each("place", func(e *core.Entity) {
		if core.Ident(e) == nil {
			return
		}
		if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
			pop[pl.Settlement]++
		}
	})

	w.Each("realm", func(re *core.Entity) {
		r := Realm(re)
		// собрать излишки зерна в общий караван державы, раздать нуждающимся
		pooled := 0.0
		type need struct {
			se      *core.Entity
			deficit float64
		}
		var needers []need
		for _, sid := range r.Territory {
			se := w.Get(sid)
			if se == nil {
				continue
			}
			st := store(se)
			target := float64(pop[sid]) * tradeBufferDays
			feed := FeedableFood(se)
			switch {
			case feed > target*1.5: // излишек — часть зерна на продажу
				give := (feed - target) * 0.5
				// но не продаём ниже посевного резерва (та же бережливость общины)
				give = min(give, st[GoodWheat]-sowReserve)
				if give > 0 {
					st[GoodWheat] -= give
					pooled += give
				}
			case feed < target: // нужда
				needers = append(needers, need{se, target - feed})
			}
		}
		if pooled <= 0 || len(needers) == 0 {
			return
		}
		pooled *= (1 - transportLoss) // провоз съедает часть
		totalDef := 0.0
		for _, n := range needers {
			totalDef += n.deficit
		}
		for _, n := range needers {
			share := pooled * (n.deficit / totalDef)
			store(n.se)[GoodWheat] += share // приходит зерном (хлеб испекут на месте)
		}
	})
}

// tendHerd — живое стадо: пастухи снимают возобновляемое (молоко/шерсть/яйца, птица ещё перо),
// а в голод или при переполнении выпаса скот пускают под нож (мясо+шкура), оставляя племя.
// Голов добавляет приплод (общий regen в п.1), здесь — только съём и убой. Сохранение соблюдено.
func tendHerd(n *ResourceNodeC, l *labor, st map[string]float64, se *core.Entity, w *core.World) {
	herders := l.byRole["herder"]
	if herders > 0 && n.Stock > 0 {
		if prod := herdProduct(n.HerdKind); prod != "" {
			amount := min(float64(herders)*herderRate, n.Stock*perAnimalDay)
			st[prod] += amount
			if n.HerdKind == HerdPoultry {
				st[GoodFeather] += amount * 0.3 // с птицы ещё перо
			}
		}
	}
	// забой: если деревня голодает или выпас переполнен — и есть кому забить, и есть что (сверх племени)
	people := l.people
	if people < 1 {
		people = 1
	}
	hungry := FeedableFood(se)/float64(people) < slaughterHunger
	crowded := n.Stock >= n.Cap*0.95
	if (hungry || crowded) && herders > 0 && n.Stock > herdMinKeep {
		k := min(slaughterPerDay, n.Stock-herdMinKeep)
		n.Stock -= k
		st[GoodMeat] += k * meatPerAnimal
		st[GoodHide] += k * hidePerAnimal
	}
}

// perishable — портится ли товар со временем. Еда и сырьё-органика гниют; изделия и металл нет.
func perishable(good string) bool {
	switch good {
	case GoodTools, GoodMetal, GoodWool, GoodCloth, GoodHide, GoodLeather, GoodFeather, GoodWood, GoodOre:
		return false
	}
	return true
}

// workersFor — сколько работников ремесла, кормящего данный узел.
func workersFor(byRole map[string]int, node string) int {
	switch node {
	case NodeForest:
		return byRole["woodcutter"]
	case NodeFishery:
		return byRole["fisher"]
	case NodePasture:
		return byRole["hunter"]
	case NodeMine:
		return byRole["miner"]
	}
	return 0
}

func min(a, b float64) float64 {
	if a < b {
		return a
	}
	return b
}
