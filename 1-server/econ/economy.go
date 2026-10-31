package econ

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Настройки экономики — в одном месте, чтобы крутить баланс не трогая логику.
const (
	seedPerField   = 60.0  // семян на засев общинной пашни (крупная площадь; жатва кормит год)
	seedTarget     = 300.0 // семенной фонд под 4 рабочие пашни + страховка
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
)

// Съедобное (по предпочтению). Зерно-еда (wheat) едят кашей, если нет печёного; семенное
// зерно (seed) — отдельный товар, его не едят вовсе, поэтому сев всегда обеспечен.
var edibles = []string{GoodBread, GoodFish, GoodGame, GoodFlour, GoodWheat}

// SeedPerField — сколько зерна уходит на засев пашни (для генерации стартового резерва семян).
func SeedPerField() float64 { return seedPerField }

// SeedTarget — целевой семенной фонд поселения (для стартовой генерации).
func SeedTarget() float64 { return seedTarget }

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

// FeedableFood — вся доступная в пищу еда (зерно-еда уже входит в edibles; семена не в счёт).
// По ней решаем, сыта ли деревня для рождений.
func FeedableFood(se *core.Entity) float64 { return FoodOnHand(se) }

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

func (EconomySystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	season := clock.Season()
	s := hub.Stream("economy")

	// индекс «поселение → его жители по ремёслам» за ОДИН проход (масштабируемо)
	type labor struct {
		byRole map[string]int
		people int
	}
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
			if !n.Sown && farmers > 0 && st[GoodSeed] >= seedPerField {
				st[GoodSeed] -= seedPerField // семенное зерно уходит в землю
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
				yield := n.SownSeed * ratio // урожай = сам-N от посеянного (сохранение)
				// первым делом пополнить семенной фонд до цели, остальное — зерно-еда
				need := seedTarget - st[GoodSeed]
				if need > 0 {
					take := min(need, yield)
					st[GoodSeed] += take
					yield -= take
				}
				st[GoodWheat] += yield
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

	// 3) добыча трудом: работник ПЕРЕНОСИТ запас из узла в склад (не создаёт из воздуха).
	//    рыбное место закрыто в нерест (весна) — ловить нельзя.
	w.Each("resnode", func(ne *core.Entity) {
		n := Node(ne)
		node, good := n.Node, n.Good
		if node == NodeField { // пашню собирают жатвой, не ежедневно
			return
		}
		l := lab[n.Settlement]
		if l == nil {
			return
		}
		workers := workersFor(l.byRole, node)
		if workers == 0 || n.Stock <= 0 {
			return
		}
		if node == NodeFishery && season == core.Spring {
			return // нерест: лов закрыт
		}
		pull := min(n.Stock, float64(workers)*perWorkerDay)
		n.Stock -= pull
		se := w.Get(n.Settlement)
		if se != nil {
			store(se)[good] += pull
		}
	})

	// 4) переработка цепочками: wheat→bread (cook), ore→tools (smith). Ограничено сырьём и мастерами.
	w.Each("settlement", func(se *core.Entity) {
		st := store(se)
		l := lab[se.ID]
		if l == nil {
			return
		}
		// повар мелет зерно-еду в хлеб (семенной фонд — отдельный товар, его не трогаем)
		if cooks := l.byRole["cook"]; cooks > 0 && st[GoodWheat] > 0 {
			use := min(st[GoodWheat], float64(cooks)*cookGrindDay)
			st[GoodWheat] -= use
			st[GoodBread] += use * breadFromWheat
		}
		if smiths := l.byRole["smith"]; smiths > 0 && st[GoodOre] > 0 {
			use := min(st[GoodOre], float64(smiths)*smithOreDay)
			st[GoodOre] -= use
			st[GoodTools] += use * toolsFromOre
		}
	})

	// 5) порча: запасы не живут вечно (еда гниёт) — важнейший тормоз инфляции склада
	w.Each("settlement", func(se *core.Entity) {
		st := store(se)
		for g, v := range st {
			if g == GoodTools || g == GoodMetal {
				continue // инструмент/металл не гниют
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
			case feed > target*1.5: // излишек — часть зерна-еды на продажу (семена не трогаем)
				give := (feed - target) * 0.5
				give = min(give, st[GoodWheat])
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
